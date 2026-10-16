using Broadside.Diagnostics;
using Broadside.Graphics;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>
/// Images and stencil masks (ISO 32000-2 §8.9, spec D15, D16): each interned display-list image becomes one CoreGraphics image per
/// backend, drawn into the unit square under the current transform; masks become 8-bit DeviceGray images that clip as alpha.
/// </summary>
internal sealed partial class CoreGraphicsBackend
{
    private static readonly Diagnostic ImageTooLarge = new(
        "BackendImageTooLarge",
        DiagnosticSeverity.Warning,
        "An image or mask has more pixels than the CoreGraphics backend can convert in memory (ISO 32000-2 §8.9.5); it was left out of this render.");

    private readonly ImageCache _images;
    private readonly WeakReference<PdfDocument?> _budgetDocument = new(null);

    /// <summary>
    /// The transform from the replay's target space to the pixel space images are resampled, meshes rasterized and lattices laid in,
    /// and its inverse: the identity when the target space is the device pixels up to a flip and a whole-pixel offset (so that the
    /// work runs in the software rasterizer's own device space, ties at pixel centres falling as they fall there), else the context's
    /// transform to its device pixels.
    /// </summary>
    private Matrix _pixels;
    private Matrix _fromPixels;
    private bool _baseInvertible;

    /// <summary>What a mask or image's samples do: drawn, intersected with the clip, or filled with the fill colour through.</summary>
    private enum SampleUse
    {
        Draw,
        Clip,
        Fill,
    }

    /// <summary>Gets how many CoreGraphics images were made from display-list images and masks.</summary>
    internal int ImagesCreated => _images.Created;

    /// <summary>Gets the bytes of converted pixels kept.</summary>
    internal long CachedImageBytes => _images.Bytes;

    /// <summary>
    /// Draws an image (ISO 32000-2 §8.9.4, §8.9.5): image space maps to the unit square through [1/w 0 0 −1/h 0 1], then the
    /// current transform. A mask of the image's size is folded into the pixels; a soft mask (§11.6.5.2) or explicit mask (§8.9.6.3)
    /// of another size clips the image at its own resolution, so each is resampled from its own grid.
    /// </summary>
    public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
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
                Report(ReportedKinds.ImageTooLarge, ImageTooLarge);
                return;
            }

            cache.Created++;
            owned = !cache.Keep(entry, pixels, (long)image.Width * image.Height * 4, rgb, defaultColorSpaces);
        }

        CGContext context = _context!;
        context.SaveState();
        try
        {
            context.SetAlpha((nfloat)(_compositing.FillAlpha / 255.0));
            if (SeparateMask(image) is { } mask)
            {
                PaintMask(mask, SampleUse.Clip, clipEdges: false);
            }

            PaintSamples(pixels, entry, image.Width, image.Height, image.Interpolate, SampleUse.Draw, clipEdges: true);
        }
        finally
        {
            context.RestoreState();
            if (owned)
            {
                pixels.Dispose();
            }
        }
    }

    /// <summary>
    /// Fills a stencil mask with the colour (ISO 32000-2 §8.9.6.2): the mask maps onto the unit square like an image and clips a fill
    /// of the unit square in the colour.
    /// </summary>
    public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
    {
        if (_compositing.FillAlpha == 0 || !TryLoadColor(color, _compositing.FillAlpha))
        {
            return;
        }

        CGContext context = _context!;
        context.SaveState();
        context.SetFillColor(_color);
        PaintMask(mask, SampleUse.Fill, clipEdges: true);
        context.RestoreState();
    }

    /// <summary>Reads the context's device mapping and the document's image budget at the start of a replay.</summary>
    private void BeginImages(CGContext context, PdfDocument document)
    {
        CGAffineTransform ctm = context.GetCTM();
        var device = new Matrix(ctm.A, ctm.B, ctm.C, ctm.D, ctm.Tx, ctm.Ty);
        bool aligned = device.B == 0 && device.C == 0 && Math.Abs(device.A) == 1 && Math.Abs(device.D) == 1
            && device.E == Math.Floor(device.E) && device.F == Math.Floor(device.F);
        _pixels = aligned ? Matrix.Identity : device;
        _baseInvertible = _pixels.TryInvert(out _fromPixels) && double.IsFinite(_fromPixels.A + _fromPixels.B + _fromPixels.C + _fromPixels.D + _fromPixels.E + _fromPixels.F);
        if (_owner is null && (!_budgetDocument.TryGetTarget(out PdfDocument? last) || !ReferenceEquals(last, document)))
        {
            // The document's image budget (spec D10) bounds the converted pixels kept; read once per document, as reading allocates.
            _images.Budget = document.CacheStatistics.Images.Budget;
            _budgetDocument.SetTarget(document);
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

    private static void Concat(CGContext context, in Matrix m) =>
        context.ConcatCTM(new CGAffineTransform((nfloat)m.A, (nfloat)m.B, (nfloat)m.C, (nfloat)m.D, (nfloat)m.E, (nfloat)m.F));

    /// <summary>
    /// The interpolation of an image CoreGraphics resamples itself: reduced on either axis (rotated, or enlarged on the other axis),
    /// its high-quality filter, which averages the samples under a pixel; enlarged, square samples unless the image asks for
    /// interpolation (§8.9.5.3).
    /// </summary>
    private static CGInterpolationQuality Quality(in Matrix imageToDevice, bool interpolate)
    {
        double x = Math.Sqrt((imageToDevice.A * imageToDevice.A) + (imageToDevice.B * imageToDevice.B));
        double y = Math.Sqrt((imageToDevice.C * imageToDevice.C) + (imageToDevice.D * imageToDevice.D));
        if (x < 1 || y < 1)
        {
            return CGInterpolationQuality.High;
        }

        return interpolate ? CGInterpolationQuality.Low : CGInterpolationQuality.None;
    }

    /// <summary>
    /// Draws, clips to or fills through samples of <paramref name="width"/> × <paramref name="height"/> mapped into the unit square
    /// under the current transform. Mapped onto few enough device pixels, they are resampled to those pixels (<see cref="ImageReducer"/>,
    /// kept with the image) and placed on them one to one, the outline clipped with anti-aliasing unless <paramref name="clipEdges"/> is
    /// off (a mask whose image clips its own edges); otherwise CoreGraphics samples them under the transform. A clip is left on the
    /// context; drawing and filling leave its state as found.
    /// </summary>
    private void PaintSamples(PixelImage samples, ImageCache.Entry entry, int width, int height, bool interpolate, SampleUse use, bool clipEdges)
    {
        CGContext context = _context!;
        Matrix toTarget = UnitSquare(width, height) * _toDevice;
        if (!toTarget.TryInvert(out _))
        {
            if (use == SampleUse.Clip)
            {
                context.ClipToRect(CGRect.Empty);
            }

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
                // Device pixels as user space; the block's first row at its top (image space, y down, as the samples' rows).
                Matrix placement = new(pixels.Width, 0, 0, -pixels.Height, pixels.Left, pixels.Top + pixels.Height);
                if (use != SampleUse.Clip)
                {
                    context.SaveState();
                }

                Concat(context, _fromPixels);
                if (clipEdges)
                {
                    ClipToOutline(context, toPixels, width, height, upright);
                }

                Concat(context, placement);
                context.InterpolationQuality = CGInterpolationQuality.None;
                Apply(context, reduced.Image, use);
                if (use == SampleUse.Clip)
                {
                    if ((placement * _fromPixels).TryInvert(out Matrix back))
                    {
                        Concat(context, back);
                    }
                }
                else
                {
                    context.RestoreState();
                }

                if (owned)
                {
                    reduced.Dispose();
                }

                return;
            }
        }

        if (use != SampleUse.Clip)
        {
            context.SaveState();
        }

        Concat(context, _toDevice);
        context.InterpolationQuality = Quality(toPixels, interpolate);
        Apply(context, samples.Image, use);
        if (use == SampleUse.Clip)
        {
            if (_toDevice.TryInvert(out Matrix back))
            {
                Concat(context, back);
            }
        }
        else
        {
            context.RestoreState();
        }
    }

    /// <summary>Draws, clips to or fills through an image over the unit square of the current user space.</summary>
    private static void Apply(CGContext context, CGImage image, SampleUse use)
    {
        var unit = new CGRect(0, 0, 1, 1);
        switch (use)
        {
            case SampleUse.Draw:
                context.DrawImage(unit, image);
                break;
            case SampleUse.Clip:
                context.ClipToMask(unit, image);
                break;
            default:
                context.ClipToMask(unit, image);
                context.FillRect(unit);
                break;
        }
    }

    /// <summary>Intersects the clip with the outline of the samples on the device (the current user space), anti-aliased like a fill.</summary>
    private static void ClipToOutline(CGContext context, in Matrix toPixels, int width, int height, bool upright)
    {
        PathPoint p0 = toPixels.Transform(0, 0);
        PathPoint p2 = toPixels.Transform(width, height);
        if (upright)
        {
            context.ClipToRect(new CGRect(Math.Min(p0.X, p2.X), Math.Min(p0.Y, p2.Y), Math.Abs(p2.X - p0.X), Math.Abs(p2.Y - p0.Y)));
            return;
        }

        PathPoint p1 = toPixels.Transform(width, 0);
        PathPoint p3 = toPixels.Transform(0, height);
        context.BeginPath();
        context.MoveTo((nfloat)p0.X, (nfloat)p0.Y);
        context.AddLineToPoint((nfloat)p1.X, (nfloat)p1.Y);
        context.AddLineToPoint((nfloat)p2.X, (nfloat)p2.Y);
        context.AddLineToPoint((nfloat)p3.X, (nfloat)p3.Y);
        context.ClosePath();
        context.Clip();
    }

    /// <summary>Clips to or fills through a mask (a soft mask image or a stencil) as an alpha plane over the unit square.</summary>
    private void PaintMask(object mask, SampleUse use, bool clipEdges)
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
                Report(ReportedKinds.ImageTooLarge, ImageTooLarge);
                return;
            }

            cache.Created++;
            owned = !cache.Keep(entry, alpha, (long)width * height, default, null);
        }

        try
        {
            // The mask's samples map onto the unit square at their own resolution (§8.9.6.2, §8.9.6.3, §11.6.5.2).
            PaintSamples(alpha, entry, width, height, interpolate, use, clipEdges);
        }
        finally
        {
            if (owned)
            {
                alpha.Dispose();
            }
        }
    }
}
