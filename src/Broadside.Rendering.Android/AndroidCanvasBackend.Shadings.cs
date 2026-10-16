using Broadside.Diagnostics;
using Broadside.Graphics;
using AndroidCanvas = Android.Graphics.Canvas;
using AndroidColor = Android.Graphics.Color;
using Matrix = Broadside.Graphics.Matrix;
using Paint = Android.Graphics.Paint;
using PaintFlags = Android.Graphics.PaintFlags;

namespace Broadside.Rendering.Android;

public sealed partial class AndroidCanvasBackend
{
    /// <summary>
    /// Shadings (ISO 32000-2 §8.7.4, spec D20–D22) through their Android form (<see cref="ShadingBuilder"/>), painted under the
    /// shading's transform: by <c>sh</c> within the clip, and as a shading pattern within the filled path or the stroke's outline, over
    /// its Background.
    /// </summary>
    private sealed partial class Backend
    {
        private static readonly Diagnostic ShadingSkipped = new(
            "BackendShadingUnsupported",
            DiagnosticSeverity.Warning,
            "The Android backend met a shading it does not draw (ISO 32000-2 §8.7.4.5): a type it does not know, or, before Android 12 " +
            "(API level 31), a radial shading other than growing concentric circles; those paints were left out of this render.");

        private static readonly Diagnostic MeshRasterized = new(
            "BackendMeshShadingRasterized",
            DiagnosticSeverity.Information,
            "The page's mesh shadings (ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8) were rasterized by the Android backend at the device's " +
            "pixels, without anti-aliasing at the mesh's outer edges, and drawn as bitmaps.");

        // The paint of a shading, the layer a stroke's coverage is drawn in, and the layer a paint within it is kept by (source-in).
        private readonly Paint _shadePaint = new(PaintFlags.AntiAlias);
        private readonly Paint _outerPaint = new(PaintFlags.AntiAlias);
        private readonly Paint _insidePaint = new(PaintFlags.AntiAlias);
        private BlendMode _shadeBlendMode;
        private BlendMode _outerBlendMode;

        /// <summary>The device pixels the replay's initial clip covers, which bound a mesh's rasterization.</summary>
        private PixelRect _deviceBounds;

        /// <summary>Paints the shading within the current clip and its BBox, without its Background (ISO 32000-2 §8.7.4.2, §8.7.4.3).</summary>
        private void DrawShadingCore(in DisplayShading shading)
        {
            if (_compositing.FillAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry || entry.Gradient is null && entry.Mesh is null && entry.Texture is null)
            {
                return;
            }

            AndroidCanvas canvas = _canvas!;
            int count = canvas.Save();
            Prepare(_shadePaint, ref _shadeBlendMode, _compositing.FillAlpha, _compositing.BlendMode);
            PaintShading(entry, shading, _shadePaint);
            canvas.RestoreToCount(count);
        }

        /// <summary>Fills the path with the shading pattern's shading over its Background (ISO 32000-2 §8.7.4.1, §8.7.4.3).</summary>
        private void FillPathWithShadingCore(PathView path, FillRule rule, in DisplayShading shading)
        {
            if (path.IsEmpty || _compositing.FillAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry)
            {
                return;
            }

            AndroidCanvas canvas = _canvas!;
            int count = canvas.Save();
            BuildDevicePath(path, rule);
            canvas.ClipPath(_path);
            if (shading.PaintsBackground && entry.Background is (byte r, byte g, byte b)
                && (_compositing.FillAlpha != 255 || _compositing.BlendMode != BlendMode.Normal))
            {
                // Background and shading composite as one paint: drawn opaque in a layer, the layer with the alpha and blend mode.
                Prepare(_outerPaint, ref _outerBlendMode, _compositing.FillAlpha, _compositing.BlendMode);
                canvas.SaveLayer(null, _outerPaint);
                canvas.DrawColor(new AndroidColor(r, g, b));
                Prepare(_shadePaint, ref _shadeBlendMode, 255, BlendMode.Normal);
                PaintShading(entry, shading, _shadePaint);
            }
            else
            {
                if (shading.PaintsBackground && entry.Background is (byte br, byte bg, byte bb))
                {
                    canvas.DrawColor(new AndroidColor(br, bg, bb));
                }

                Prepare(_shadePaint, ref _shadeBlendMode, _compositing.FillAlpha, _compositing.BlendMode);
                PaintShading(entry, shading, _shadePaint);
            }

            canvas.RestoreToCount(count);
        }

        /// <summary>Paints the area the stroke covers with the shading pattern's shading over its Background (ISO 32000-2 §8.7.4.1, §8.5.3.2).</summary>
        private void StrokePathWithShadingCore(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
            if (path.IsEmpty || _compositing.StrokeAlpha == 0 || GetShading(shading) is not { PaintsNothing: false } entry)
            {
                return;
            }

            if (BeginStrokeArea(path, style, out int count))
            {
                if (shading.PaintsBackground && entry.Background is (byte r, byte g, byte b))
                {
                    _canvas!.DrawColor(new AndroidColor(r, g, b));
                }

                Prepare(_shadePaint, ref _shadeBlendMode, 255, BlendMode.Normal);
                PaintShading(entry, shading, _shadePaint);
                _canvas!.RestoreToCount(count);
            }
        }

        /// <summary>
        /// Opens the two layers a paint within a stroke's outline is drawn in: the outer one holds the stroke's coverage and carries the
        /// stroke alpha and blend mode, the inner one receives the paint and is kept only where the stroke covers (source-in); false,
        /// with nothing opened, when the stroke paints nothing. <paramref name="count"/> restores both.
        /// </summary>
        private bool BeginStrokeArea(PathView path, in StrokeStyle style, out int count)
        {
            AndroidCanvas canvas = _canvas!;
            Prepare(_outerPaint, ref _outerBlendMode, _compositing.StrokeAlpha, _compositing.BlendMode);
            count = canvas.SaveLayer(null, _outerPaint);
            _stroke.SetARGB(255, 0, 0, 0);
            SetBlendMode(_stroke, ref _strokeBlendMode, BlendMode.Normal);
            if (!DrawStroke(path, style))
            {
                canvas.RestoreToCount(count);
                return false;
            }

            canvas.SaveLayer(null, _insidePaint);
            return true;
        }

        /// <summary>Gives a paint the alpha and blend mode, its colour black and no shader.</summary>
        private static void Prepare(Paint paint, ref BlendMode current, byte alpha, BlendMode mode)
        {
            paint.SetARGB(alpha, 0, 0, 0);
            SetBlendMode(paint, ref current, mode);
        }

        /// <summary>The Android form of the shading, built on first use for its conversion; null for one this backend does not draw (reported).</summary>
        private ShadingCache.Entry? GetShading(in DisplayShading shading)
        {
            if (_shadings.Find(shading.Shading, shading.Conversion, shading.DefaultColorSpaces) is { } entry)
            {
                return entry;
            }

            ShadingCache.Entry? built = ShadingBuilder.Build(_document!, shading);
            if (built is null)
            {
                Report(Reported.ShadingUnsupported, ShadingSkipped);
                return null;
            }

            _shadings.Add(shading.Shading, built);
            return built;
        }

        /// <summary>Paints the shading over the current clip under its transform, clipped to its BBox (§8.7.4.3); the caller saves and restores.</summary>
        private void PaintShading(ShadingCache.Entry entry, in DisplayShading shading, Paint paint)
        {
            AndroidCanvas canvas = _canvas!;
            Matrix toTarget = shading.Matrix * _device;
            if (entry.Mesh is { } mesh)
            {
                PaintMesh(entry, mesh, shading, toTarget, paint);
                return;
            }

            if (!Concat(toTarget))
            {
                return;
            }

            if (shading.Shading.BoundingBox is PdfRectangle box)
            {
                ClipRect(box);
            }

            if (entry.Gradient is { } gradient)
            {
                paint.SetShader(gradient);
                canvas.DrawPaint(paint);
                paint.SetShader(null);
            }
            else if (entry.Texture is { } texture && Concat(entry.TextureMatrix))
            {
                PdfRectangle area = entry.TextureArea;
                ClipRect(area);

                // The texture's first row is the domain's smaller y.
                double width = (area.Right - area.Left) / texture.Width;
                double height = (area.Top - area.Bottom) / texture.Height;
                if (Concat(new Matrix(width, 0, 0, height, area.Left, area.Bottom)))
                {
                    paint.FilterBitmap = true;
                    canvas.DrawBitmap(texture.Bitmap, 0, 0, paint);
                }
            }
        }

        private void ClipRect(PdfRectangle box) => _canvas!.ClipRect(
            (float)Math.Min(box.Left, box.Right),
            (float)Math.Min(box.Bottom, box.Top),
            (float)Math.Max(box.Left, box.Right),
            (float)Math.Max(box.Bottom, box.Top));

        /// <summary>
        /// Paints a mesh as its rasterization at the device's pixels (<see cref="MeshRasterizer"/>), kept with the shading for the first
        /// transforms it is painted at, placed on the pixels one to one and clipped to the BBox.
        /// </summary>
        private void PaintMesh(ShadingCache.Entry entry, MeshTriangles mesh, in DisplayShading shading, in Matrix toTarget, Paint paint)
        {
            Report(Reported.MeshRasterized, MeshRasterized);
            if (!_baseInvertible || _deviceBounds.Width <= 0 || _deviceBounds.Height <= 0)
            {
                return;
            }

            Matrix toPixels = toTarget * _pixels;
            if (shading.Shading.BoundingBox is PdfRectangle box)
            {
                ClipToQuad(toTarget.Transform(box.Left, box.Bottom), toTarget.Transform(box.Right, box.Bottom), toTarget.Transform(box.Right, box.Top), toTarget.Transform(box.Left, box.Top));
            }

            PixelRect bounds = Intersect(_deviceBounds, toPixels, mesh.Extent);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            int step = 1;
            while ((long)((bounds.Width + step - 1) / step) * ((bounds.Height + step - 1) / step) > MeshRasterizer.MaxPixels)
            {
                step++;
            }

            if (!entry.Admit(toPixels, bounds, out int slot))
            {
                PaintMeshTexture(entry, mesh, toTarget, toPixels, paint);
                return;
            }

            PixelImage? raster = entry.RasterAt(slot);
            if (raster is null)
            {
                raster = MeshRasterizer.Rasterize(mesh, toPixels, bounds, step);
                entry.SetRaster(slot, raster);
            }

            ConcatFromPixels();
            if (step == 1)
            {
                paint.FilterBitmap = false;
                _canvas!.DrawBitmap(raster.Bitmap, bounds.Left, bounds.Top, paint);
            }
            else if (Concat(new Matrix(step, 0, 0, step, bounds.Left, bounds.Top)))
            {
                paint.FilterBitmap = true;
                _canvas!.DrawBitmap(raster.Bitmap, 0, 0, paint);
            }
        }

        /// <summary>
        /// Paints a mesh at a transform beyond those it is rasterized for at the device: through one rasterization in shading space, at
        /// the scale of the first such transform, which the canvas resamples under each (as an image painted at many positions is).
        /// </summary>
        private void PaintMeshTexture(ShadingCache.Entry entry, MeshTriangles mesh, in Matrix toTarget, in Matrix toPixels, Paint paint)
        {
            PdfRectangle extent = mesh.Extent;
            double left = Math.Min(extent.Left, extent.Right);
            double top = Math.Max(extent.Top, extent.Bottom);
            double width = Math.Abs(extent.Right - extent.Left);
            double height = Math.Abs(extent.Top - extent.Bottom);
            if (entry.MeshTexture is null)
            {
                double scale = Math.Sqrt(Math.Abs(toPixels.Determinant));
                if (!(scale > 0) || !double.IsFinite(scale) || !(width > 0) || !(height > 0))
                {
                    return;
                }

                scale = Math.Min(scale, Math.Sqrt(MeshRasterizer.MaxPixels / (width * height)));
                int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
                int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
                var toTexture = new Matrix(scale, 0, 0, -scale, -left * scale, top * scale);
                entry.MeshTexture = MeshRasterizer.Rasterize(mesh, toTexture, new PixelRect(0, 0, pixelWidth, pixelHeight), 1);
                entry.MeshTextureScale = scale;
            }

            // Texture pixels (y down from the extent's top) to shading space, then to the canvas.
            double side = 1 / entry.MeshTextureScale;
            if (Concat(toTarget) && Concat(new Matrix(side, 0, 0, -side, left, top)))
            {
                paint.FilterBitmap = true;
                _canvas!.DrawBitmap(entry.MeshTexture.Bitmap, 0, 0, paint);
            }
        }

        /// <summary>Reads the device pixels the canvas's clip covers at the start of a replay.</summary>
        private void BeginShadings(AndroidCanvas canvas)
        {
            if (!_baseInvertible || !canvas.GetClipBounds(_clip))
            {
                _deviceBounds = default;
                return;
            }

            PathPoint a = _pixels.Transform(_clip.Left, _clip.Top);
            PathPoint b = _pixels.Transform(_clip.Right, _clip.Top);
            PathPoint c = _pixels.Transform(_clip.Right, _clip.Bottom);
            PathPoint d = _pixels.Transform(_clip.Left, _clip.Bottom);
            double left = Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)));
            double right = Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)));
            double top = Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)));
            double bottom = Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
            const double limit = 1 << 24;
            _deviceBounds = new PixelRect(
                (int)Math.Clamp(left, -limit, limit),
                (int)Math.Clamp(top, -limit, limit),
                (int)Math.Clamp(right, -limit, limit),
                (int)Math.Clamp(bottom, -limit, limit));
        }

        /// <summary>The pixels of <paramref name="pixels"/> that the image of <paramref name="extent"/> under <paramref name="m"/> touches.</summary>
        private static PixelRect Intersect(PixelRect pixels, in Matrix m, PdfRectangle extent)
        {
            PathPoint a = m.Transform(extent.Left, extent.Bottom);
            PathPoint b = m.Transform(extent.Right, extent.Bottom);
            PathPoint c = m.Transform(extent.Right, extent.Top);
            PathPoint d = m.Transform(extent.Left, extent.Top);
            double left = Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)));
            double right = Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)));
            double top = Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)));
            double bottom = Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
            if (!(left < right) || !(top < bottom))
            {
                return default;
            }

            return new PixelRect(
                (int)Math.Max(pixels.Left, left),
                (int)Math.Max(pixels.Top, top),
                (int)Math.Min(pixels.Right, right),
                (int)Math.Min(pixels.Bottom, bottom));
        }
    }
}
