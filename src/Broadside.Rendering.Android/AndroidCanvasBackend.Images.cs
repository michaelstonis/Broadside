using Broadside.Diagnostics;
using Broadside.Graphics;
using AndroidCanvas = Android.Graphics.Canvas;
using AndroidMatrix = Android.Graphics.Matrix;
using Matrix = Broadside.Graphics.Matrix;
using Paint = Android.Graphics.Paint;
using PaintFlags = Android.Graphics.PaintFlags;
using PorterDuff = Android.Graphics.PorterDuff;
using PorterDuffXfermode = Android.Graphics.PorterDuffXfermode;
using Rect = Android.Graphics.Rect;
using RectF = Android.Graphics.RectF;

namespace Broadside.Rendering.Android;

public sealed partial class AndroidCanvasBackend
{
    /// <summary>Gets how many Android bitmaps the backend made from display-list images and masks.</summary>
    internal int ImagesCreated => _backend.ImagesCreated;

    /// <summary>Gets the bytes of converted pixels the backend keeps.</summary>
    internal long CachedImageBytes => _backend.CachedImageBytes;

    /// <summary>Gets how many tiling pattern cells the backend rendered into tiles.</summary>
    internal int CellsRecorded => _backend.CellsRecorded;

    /// <summary>
    /// Images and stencil masks (ISO 32000-2 §8.9, spec D15, D16): each interned display-list image becomes one Android bitmap per
    /// backend, drawn into the unit square under the current transform; masks become alpha bitmaps, painted in the paint's colour.
    /// </summary>
    private sealed partial class Backend
    {
        private static readonly Diagnostic ImageTooLarge = new(
            "BackendImageTooLarge",
            DiagnosticSeverity.Warning,
            "An image or mask has more pixels than the Android backend can convert in memory (ISO 32000-2 §8.9.5); it was left out of this render.");

        private readonly Backend? _owner;
        private readonly int _patternDepth;
        private readonly ImageCache _images;
        private readonly ShadingCache _shadings;
        private readonly WeakReference<PdfDocument?> _budgetDocument = new(null);

        // The paint of an image, of a masked image's samples and its mask, of a stencil mask, and of the layer that applies a mask.
        private readonly Paint _imagePaint = new(PaintFlags.AntiAlias);
        private readonly Paint _plainPaint = new(PaintFlags.AntiAlias);
        private readonly Paint _maskPaint = new(PaintFlags.AntiAlias);
        private readonly Paint _maskInPaint = new(PaintFlags.AntiAlias);
        private readonly PorterDuffXfermode _dstIn = new(PorterDuff.Mode.DstIn!);
        private readonly PorterDuffXfermode _srcIn = new(PorterDuff.Mode.SrcIn!);
        private readonly AndroidMatrix _canvasMatrix = new();
        private readonly RectF _point = new();
        private readonly Rect _clip = new();
        private BlendMode _imageBlendMode;
        private BlendMode _maskBlendMode;
        private Reported _reported;

        /// <summary>
        /// The transform from the canvas's coordinates to the pixel space images are resampled, meshes rasterized and lattices laid in,
        /// and its inverse: the identity when the canvas's matrix is a whole-pixel translation (so that the work runs in the software
        /// rasterizer's own device space, ties at pixel centres falling as they fall there), else the canvas's matrix.
        /// </summary>
        private Matrix _pixels;
        private Matrix _fromPixels;
        private bool _pixelsIdentity;
        private bool _baseInvertible;

        /// <summary>The kinds of diagnostic already reported in this render.</summary>
        [Flags]
        private enum Reported
        {
            None = 0,
            ImageTooLarge = 1,
            ShadingUnsupported = 2,
            MeshRasterized = 4,
            TilingApproximated = 8,
            KnockoutRasterized = 16,
            ColorSpaceRasterized = 32,
            NonIsolatedRasterized = 64,
            NonIsolatedApproximated = 128,
            SoftMaskRasterized = 256,
            GroupApproximated = 512,
        }

        public int ImagesCreated => _images.Created;

        public long CachedImageBytes => _images.Bytes;

        /// <summary>
        /// Draws an image (ISO 32000-2 §8.9.4, §8.9.5): image space maps to the unit square through [1/w 0 0 −1/h 0 1], then the
        /// current transform. A mask of the image's size is folded into the pixels; a soft mask (§11.6.5.2) or explicit mask (§8.9.6.3)
        /// of another size is drawn at its own resolution onto the image in a layer, so each is resampled from its own grid.
        /// </summary>
        private void DrawImageCore(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            if (_compositing.FillAlpha == 0)
            {
                return;
            }

            ImageCache cache = _images;
            ImageCache.Entry entry = cache.Find(image);
            ColorConversion rgb = conversion with { Target = DeviceColorModel.Rgb };
            PixelImage? pixels = entry.Matches(rgb, defaultColorSpaces) ? entry.Image : null;
            bool owned = false;
            if (pixels is null)
            {
                PdfColorConverter converter = _document!.GetColorConverter(image.ColorSpace, rgb, defaultColorSpaces);
                if (converter.PaintsNothing)
                {
                    return;
                }

                PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed && !image.Matte.IsEmpty
                    ? _document.GetColorConverter(indexed.Base, rgb, defaultColorSpaces)
                    : null;
#pragma warning disable CA2000 // Kept by the cache, or disposed in the finally block below.
                pixels = ImageConverter.Convert(image, converter, baseConverter);
#pragma warning restore CA2000
                if (pixels is null)
                {
                    Report(Reported.ImageTooLarge, ImageTooLarge);
                    return;
                }

                cache.Created++;
                owned = !cache.Keep(entry, pixels, (long)image.Width * image.Height * 4, rgb, defaultColorSpaces);
            }

            AndroidCanvas canvas = _canvas!;
            try
            {
                _imagePaint.Alpha = _compositing.FillAlpha;
                SetBlendMode(_imagePaint, ref _imageBlendMode);
                if (SeparateMask(image) is not { } mask)
                {
                    PaintSamples(pixels, entry, image.Width, image.Height, image.Interpolate, _imagePaint, clipEdges: true);
                    return;
                }

                // The canvas draws an alpha bitmap as coverage of the paint's colour, so the mask is drawn plainly into a layer of its
                // own, which keeps the image where the mask is opaque (destination-in) when it is composited.
                int count = canvas.SaveLayer(null, _imagePaint);
                PaintSamples(pixels, entry, image.Width, image.Height, image.Interpolate, _plainPaint, clipEdges: true);
                canvas.SaveLayer(null, _maskInPaint);
                PaintMask(mask, _plainPaint, clipEdges: false);
                canvas.RestoreToCount(count);
            }
            finally
            {
                if (owned)
                {
                    pixels.Dispose();
                }
            }
        }

        /// <summary>
        /// Fills a stencil mask with the colour (ISO 32000-2 §8.9.6.2): the mask maps onto the unit square like an image and is drawn
        /// as an alpha bitmap, which the canvas paints in the paint's colour.
        /// </summary>
        private void FillImageMaskCore(DisplayImageMask mask, in DisplayColor color)
        {
            PdfColorConverter converter = Converter(color);
            if (_compositing.FillAlpha == 0 || converter.PaintsNothing)
            {
                return;
            }

            SetColor(_maskPaint, converter, color.Components, _compositing.FillAlpha);
            SetBlendMode(_maskPaint, ref _maskBlendMode);
            PaintMask(mask, _maskPaint, clipEdges: true);
        }

        /// <summary>
        /// Reads the canvas's matrix and clip and the document's image budget at the start of a replay. The matrix is read through
        /// the images of three points, which needs no Java array.
        /// </summary>
        private void BeginPaints(AndroidCanvas canvas, PdfDocument document)
        {
#pragma warning disable CS0618, CA1422 // Canvas.getMatrix(Matrix) is deprecated for hardware canvases, whose matrix it may not report; it is the one call that fills a kept matrix.
            canvas.GetMatrix(_canvasMatrix);
#pragma warning restore CS0618, CA1422
            Matrix device = Matrix.Identity;
            if (!_canvasMatrix.IsIdentity)
            {
                (double ex, double fy) = MapPoint(0, 0);
                (double ax, double by) = MapPoint(1, 0);
                (double cx, double dy) = MapPoint(0, 1);
                device = new Matrix(ax - ex, by - fy, cx - ex, dy - fy, ex, fy);
            }

            bool aligned = device.B == 0 && device.C == 0 && device.A == 1 && device.D == 1 && device.E == Math.Floor(device.E) && device.F == Math.Floor(device.F);
            _pixels = aligned ? Matrix.Identity : device;
            _pixelsIdentity = aligned;
            _baseInvertible = _pixels.TryInvert(out _fromPixels) && double.IsFinite(_fromPixels.A + _fromPixels.B + _fromPixels.C + _fromPixels.D + _fromPixels.E + _fromPixels.F);
            if (_owner is null && (!_budgetDocument.TryGetTarget(out PdfDocument? last) || !ReferenceEquals(last, document)))
            {
                // The document's image budget (spec D10) bounds the converted pixels kept; read once per document, as reading allocates.
                _images.Budget = document.CacheStatistics.Images.Budget;
                _budgetDocument.SetTarget(document);
            }

            BeginShadings(canvas);
        }

        private (double X, double Y) MapPoint(float x, float y)
        {
            _point.Set(x, y, x, y);
            _canvasMatrix.MapRect(_point);
            return (_point.Left, _point.Top);
        }

        /// <summary>Reports a diagnostic once per render.</summary>
        private void Report(Reported kind, Diagnostic diagnostic)
        {
            if ((_reported & kind) == 0)
            {
                _reported |= kind;
                Diagnostics.Add(diagnostic);
            }
        }

        /// <summary>Concatenates <paramref name="m"/> to the canvas's matrix; false, with nothing changed, when it maps the plane onto a line.</summary>
        private bool Concat(in Matrix m)
        {
            if (!SetMatrix(m))
            {
                return false;
            }

            _canvas!.Concat(_matrix);
            return true;
        }

        /// <summary>Concatenates the transform from pixel space back to the canvas's coordinates (nothing when they are the same).</summary>
        private void ConcatFromPixels()
        {
            if (!_pixelsIdentity)
            {
                Concat(_fromPixels);
            }
        }

        /// <summary>The image's soft mask, else its explicit mask, when it is not folded into the pixels (another size); otherwise null.</summary>
        private static object? SeparateMask(DisplayImage image)
        {
            if (image.SoftMask is { } soft)
            {
                return ImageConverter.FoldsSoftMask(image) ? null : soft;
            }

            if (image.Mask is { } mask)
            {
                return ImageConverter.FoldsMask(image) ? null : mask;
            }

            return null;
        }

        private static Matrix UnitSquare(int width, int height) => new(1.0 / width, 0, 0, -1.0 / height, 0, 1);

        /// <summary>
        /// Whether the canvas filters an image it samples itself: reduced on either axis (rotated, or enlarged on the other axis),
        /// bilinearly; enlarged, square samples unless the image asks for interpolation (§8.9.5.3).
        /// </summary>
        private static bool Filters(in Matrix imageToDevice, bool interpolate)
        {
            double x = Math.Sqrt((imageToDevice.A * imageToDevice.A) + (imageToDevice.B * imageToDevice.B));
            double y = Math.Sqrt((imageToDevice.C * imageToDevice.C) + (imageToDevice.D * imageToDevice.D));
            return x < 1 || y < 1 || interpolate;
        }

        /// <summary>
        /// Draws an image or mask of <paramref name="width"/> × <paramref name="height"/> samples into the unit square under the
        /// current transform. Mapped onto few enough device pixels, it is resampled to those pixels (<see cref="ImageReducer"/>, kept
        /// with the image) and placed on them one to one, its outline clipped with anti-aliasing unless <paramref name="clipEdges"/> is
        /// off (a mask applied in a layer whose image already has its edges); otherwise the canvas samples it under the transform.
        /// </summary>
        private void PaintSamples(PixelImage samples, ImageCache.Entry entry, int width, int height, bool interpolate, Paint paint, bool clipEdges)
        {
            AndroidCanvas canvas = _canvas!;
            Matrix toTarget = UnitSquare(width, height) * _toDevice;
            if (!toTarget.TryInvert(out _))
            {
                return;
            }

            Matrix toPixels = toTarget * _pixels;
            if (_baseInvertible && ImageReducer.Applies(width, height, toPixels, out PixelRect pixels, out bool upright))
            {
                PixelImage? reduced = null;
                bool owned = false;
                if (entry.Admit(toPixels, out int slot))
                {
                    reduced = entry.ReductionAt(slot);
#pragma warning disable CA2000 // Kept by the cache, or disposed after drawing.
                    if (reduced is null && ImageReducer.Reduce(samples, toPixels, pixels, interpolate) is { } made)
#pragma warning restore CA2000
                    {
                        reduced = made;
                        long bytes = (long)pixels.Width * pixels.Height * samples.Channels;
                        owned = !_images.KeepReduction(entry, slot, made, bytes);
                    }
                }

                if (reduced is not null)
                {
                    int count = canvas.Save();
                    ConcatFromPixels();
                    if (clipEdges)
                    {
                        ClipToOutline(toPixels, width, height, upright);
                    }

                    paint.FilterBitmap = false;
                    canvas.DrawBitmap(reduced.Bitmap, pixels.Left, pixels.Top, paint);
                    canvas.RestoreToCount(count);
                    if (owned)
                    {
                        reduced.Dispose();
                    }

                    return;
                }
            }

            int saved = canvas.Save();
            if (Concat(toTarget))
            {
                paint.FilterBitmap = Filters(toPixels, interpolate);
                canvas.DrawBitmap(samples.Bitmap, 0, 0, paint);
            }

            canvas.RestoreToCount(saved);
        }

        /// <summary>Intersects the clip with the outline of the samples in pixel space (the canvas's current space), anti-aliased like a fill.</summary>
        private void ClipToOutline(in Matrix toPixels, int width, int height, bool upright)
        {
            AndroidCanvas canvas = _canvas!;
            PathPoint p0 = toPixels.Transform(0, 0);
            PathPoint p2 = toPixels.Transform(width, height);
            if (upright)
            {
                canvas.ClipRect((float)Math.Min(p0.X, p2.X), (float)Math.Min(p0.Y, p2.Y), (float)Math.Max(p0.X, p2.X), (float)Math.Max(p0.Y, p2.Y));
                return;
            }

            PathPoint p1 = toPixels.Transform(width, 0);
            PathPoint p3 = toPixels.Transform(0, height);
            ClipToQuad(p0, p1, p2, p3);
        }

        /// <summary>Intersects the clip with a quadrilateral in the canvas's current space.</summary>
        private void ClipToQuad(PathPoint p0, PathPoint p1, PathPoint p2, PathPoint p3)
        {
            _path.Reset();
            _path.SetFillType(Winding);
            _path.MoveTo((float)p0.X, (float)p0.Y);
            _path.LineTo((float)p1.X, (float)p1.Y);
            _path.LineTo((float)p2.X, (float)p2.Y);
            _path.LineTo((float)p3.X, (float)p3.Y);
            _path.Close();
            _canvas!.ClipPath(_path);
        }

        /// <summary>Draws a mask (a soft mask image or a stencil) as an alpha bitmap over the unit square.</summary>
        private void PaintMask(object mask, Paint paint, bool clipEdges)
        {
            ImageCache cache = _images;
            ImageCache.Entry entry = cache.Find(mask);
            PixelImage? alpha = entry.Image;
            bool owned = false;
            int width;
            int height;
            bool interpolate;
            if (mask is DisplayImage soft)
            {
                (width, height, interpolate) = (soft.Width, soft.Height, soft.Interpolate);
            }
            else
            {
                var stencil = (DisplayImageMask)mask;
                (width, height, interpolate) = (stencil.Width, stencil.Height, stencil.Interpolate);
            }

            if (alpha is null)
            {
#pragma warning disable CA2000 // Kept by the cache, or disposed in the finally block below.
                alpha = mask is DisplayImage image ? ImageConverter.ConvertSoftMask(image) : ImageConverter.ConvertStencil((DisplayImageMask)mask);
#pragma warning restore CA2000
                if (alpha is null)
                {
                    Report(Reported.ImageTooLarge, ImageTooLarge);
                    return;
                }

                cache.Created++;
                owned = !cache.Keep(entry, alpha, (long)width * height, default, null);
            }

            try
            {
                // The mask's samples map onto the unit square at their own resolution (§8.9.6.2, §8.9.6.3, §11.6.5.2).
                PaintSamples(alpha, entry, width, height, interpolate, paint, clipEdges);
            }
            finally
            {
                if (owned)
                {
                    alpha.Dispose();
                }
            }
        }

        /// <summary>Releases the paints, the kept bitmaps and shaders, and the pattern tiles (the caches only when this backend owns them).</summary>
        private void DisposePaints()
        {
            _imagePaint.Dispose();
            _plainPaint.Dispose();
            _maskPaint.Dispose();
            _maskInPaint.Dispose();
            _shadePaint.Dispose();
            _outerPaint.Dispose();
            _insidePaint.Dispose();
            _tilePaint.Dispose();
            _dstIn.Dispose();
            _srcIn.Dispose();
            _canvasMatrix.Dispose();
            _point.Dispose();
            _clip.Dispose();
            ClearTiles();
            _cellBackend?.Dispose();
            if (_owner is null)
            {
                _images.Dispose();
                _shadings.Dispose();
            }
        }
    }
}
