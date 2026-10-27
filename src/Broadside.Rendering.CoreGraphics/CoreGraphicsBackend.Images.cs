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

    /// <summary>Gets how many pixels the images and masks made had in all (fewer for reduced images, issue #428).</summary>
    internal long ImagePixelsConverted => _images.PixelsConverted;

    /// <summary>Gets the bytes of converted pixels kept.</summary>
    internal long CachedImageBytes => _images.Bytes;

    /// <summary>
    /// Draws an image (ISO 32000-2 §8.9.4, §8.9.5): image space maps to the unit square through [1/w 0 0 −1/h 0 1], then the
    /// current transform. A mask of the image's size is folded into the pixels; a soft mask (§11.6.5.2) or explicit mask (§8.9.6.3)
    /// of another size clips the image at its own resolution, so each is resampled from its own grid. With scaled image decode (issue
    /// #362) the image is converted at the reduction <see cref="DisplayImage.GetReduction"/> gives for the device, a mask not folded at
    /// the reduction of its own rule.
    /// </summary>
    private void DrawImageCore(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
    {
        if (_compositing.FillAlpha == 0)
        {
            return;
        }

        ColorConversion rgb = conversion with { Target = DeviceColorModel.Rgb };
        PdfColorConverter? converter = null;
        int reduction = 0;
        if (image.MaxReduction > 0 && TryGetImageSamplesPerPixel(out double samplesPerPixel, out Matrix toPixels))
        {
            // A codec averages before colour conversion, which the converter caps where the two orders differ.
            converter = _document!.GetColorConverter(image.ColorSpace, rgb, defaultColorSpaces);
            if (converter.PaintsNothing)
            {
                return;
            }

            reduction = image.GetReduction(toPixels, samplesPerPixel, converter);
        }

        ImageCache cache = _images;
        ImageCache.Entry entry = cache.Find(image);
        PixelImage? pixels = entry.Matches(rgb, defaultColorSpaces, reduction) ? entry.Image : null;

        // An image kept encoded that cannot be decoded paints nothing (ISO 32000-2 §8.9.5; its reason is a diagnostic). Pixels kept at
        // a finer reduction than asked need no samples (issue #362: zooming out decodes nothing again).
        DisplayImageSamples? decoded = pixels is null || reduction == 0 ? image.GetSamplesAt(reduction) : null;
        if (decoded is null && (pixels is null || reduction == 0))
        {
            return;
        }

        bool owned = false;
        int delivered;
        bool folded;
        if (pixels is null)
        {
            DisplayImageSamples samples = decoded!.Value;
            converter ??= _document!.GetColorConverter(image.ColorSpace, rgb, defaultColorSpaces);
            if (converter.PaintsNothing)
            {
                return;
            }

            PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed && !image.Matte.IsEmpty
                ? _document!.GetColorConverter(indexed.Base, rgb, defaultColorSpaces)
                : null;
            bool foldSoft = ImageConverter.FoldsSoftMask(image, samples, out DisplayImageSamples? softSamples);
            bool foldMask = !foldSoft && ImageConverter.FoldsMask(image, samples);
#pragma warning disable CA2000 // Kept by the cache, or disposed in the finally block below.
            pixels = ImageConverter.Convert(image, samples, foldSoft, softSamples, foldMask, converter, baseConverter);
#pragma warning restore CA2000
            if (pixels is null)
            {
                Report(ReportedKinds.ImageTooLarge, ImageTooLarge);
                return;
            }

            cache.Created++;
            cache.PixelsConverted += (long)samples.Width * samples.Height;
            delivered = samples.Reduction;
            folded = foldSoft || foldMask;
            owned = !cache.Keep(entry, pixels, (long)samples.Width * samples.Height * 4, rgb, defaultColorSpaces, delivered);
            entry.Folded = folded;
        }
        else
        {
            delivered = entry.Delivered;
            folded = entry.Folded;
        }

        CGContext context = _context!;
        context.SaveState();
        try
        {
            context.SetAlpha((nfloat)(_compositing.FillAlpha / 255.0));
            if (!folded && ((object?)image.SoftMask ?? image.Mask) is { } mask)
            {
                PaintMask(mask, SampleUse.Clip, clipEdges: false);
            }

            PaintSamples(pixels, entry, new SampleGrid(pixels.Width, pixels.Height, image.Width, image.Height, delivered), image.Interpolate, SampleUse.Draw, clipEdges: true);
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
    private void FillImageMaskCore(DisplayImageMask mask, in DisplayColor color)
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

    /// <summary>
    /// Gets the fewest samples per device pixel a reduced image keeps (issue #362), and the transform from user space to the pixels
    /// images are resampled in; false when images are drawn at their own resolution.
    /// </summary>
    private bool TryGetImageSamplesPerPixel(out double samplesPerPixel, out Matrix toPixels)
    {
        samplesPerPixel = Root.ImageSamplesPerPixel;
        toPixels = _toDevice * _pixels;
        return samplesPerPixel > 0;
    }

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
    /// Draws, clips to or fills through samples mapped into the unit square under the current transform. Mapped onto few enough device
    /// pixels, they are resampled to those pixels (<see cref="ImageReducer"/>, kept with the image) and placed on them one to one, the
    /// outline clipped with anti-aliasing unless <paramref name="clipEdges"/> is off (a mask whose image clips its own edges); otherwise
    /// CoreGraphics samples them under the transform. Reduced samples (issue #362) reach past the unit square when the reduction does
    /// not divide the image's size; a draw is clipped to it. A clip is left on the context; drawing and filling leave its state as found.
    /// </summary>
    private void PaintSamples(PixelImage samples, ImageCache.Entry entry, in SampleGrid grid, bool interpolate, SampleUse use, bool clipEdges)
    {
        CGContext context = _context!;
        Matrix toTarget = grid.ToUnitSquare * _toDevice;
        if (!toTarget.TryInvert(out _))
        {
            if (use == SampleUse.Clip)
            {
                context.ClipToRect(CGRect.Empty);
            }

            return;
        }

        Matrix toPixels = toTarget * _pixels;
        if (_baseInvertible && ImageReducer.Applies(grid.Width, grid.Height, toPixels, out PixelRect pixels, out bool upright))
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
                if (clipEdges || (grid.Overhangs && use == SampleUse.Draw))
                {
                    ClipToOutline(context, toPixels, grid.OutlineWidth, grid.OutlineHeight, upright);
                }

                Concat(context, placement);
                context.InterpolationQuality = CGInterpolationQuality.None;
                Apply(context, reduced.Image, use, new CGRect(0, 0, 1, 1));
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
        if (grid.Overhangs && use == SampleUse.Draw)
        {
            context.ClipToRect(new CGRect(0, 0, 1, 1));
        }

        Apply(context, samples.Image, use, grid.UnitSquareRect);
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

    /// <summary>Draws, clips to or fills through an image over <paramref name="rect"/> of the current user space (a fill covers the unit square).</summary>
    private static void Apply(CGContext context, CGImage image, SampleUse use, CGRect rect)
    {
        switch (use)
        {
            case SampleUse.Draw:
                context.DrawImage(rect, image);
                break;
            case SampleUse.Clip:
                context.ClipToMask(rect, image);
                break;
            default:
                context.ClipToMask(rect, image);
                context.FillRect(new CGRect(0, 0, 1, 1));
                break;
        }
    }

    /// <summary>Intersects the clip with the outline of the unit square on the device (the current user space), anti-aliased like a fill.</summary>
    /// <param name="context">The context.</param>
    /// <param name="toPixels">The transform from the sample grid to the device.</param>
    /// <param name="width">The unit square's width on the grid: the image's width in samples over the reduction.</param>
    /// <param name="height">The unit square's height on the grid.</param>
    /// <param name="upright">Whether the transform has neither rotation nor skew.</param>
    private static void ClipToOutline(CGContext context, in Matrix toPixels, double width, double height, bool upright)
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

    /// <summary>
    /// Clips to or fills through a mask (a soft mask image or a stencil) as an alpha plane over the unit square: at its own resolution,
    /// or with scaled image decode at the reduction of its own rule (issue #362; a stencil as 8-bit coverage).
    /// </summary>
    private void PaintMask(object mask, SampleUse use, bool clipEdges)
    {
        var soft = mask as DisplayImage;
        var stencil = mask as DisplayImageMask;
        (int width, int height, bool interpolate) = soft is not null ? (soft.Width, soft.Height, soft.Interpolate) : (stencil!.Width, stencil.Height, stencil.Interpolate);
        int reduction = 0;
        if (TryGetImageSamplesPerPixel(out double samplesPerPixel, out Matrix toPixels))
        {
            reduction = soft is not null ? soft.GetReduction(toPixels, samplesPerPixel, null) : stencil!.GetReduction(toPixels, samplesPerPixel);
        }

        ImageCache cache = _images;
        ImageCache.Entry entry = cache.Find(mask);
        PixelImage? alpha = entry.Delivered <= reduction ? entry.Image : null;
        bool owned = false;
        int delivered = entry.Delivered;
        if (alpha is null)
        {
#pragma warning disable CA2000 // Kept by the cache, or disposed in the finally block below.
            if (soft is null)
            {
                delivered = reduction;
                alpha = ImageConverter.ConvertStencil(stencil!, reduction);
            }
            else if (reduction > 0 && soft.GetSamplesAt(reduction) is { } samples)
            {
                delivered = samples.Reduction;
                alpha = ImageConverter.ConvertSoftMask(soft, samples);
            }
            else
            {
                delivered = 0;
                alpha = ImageConverter.ConvertSoftMask(soft);
            }
#pragma warning restore CA2000
            if (alpha is null)
            {
                Report(ReportedKinds.ImageTooLarge, ImageTooLarge);
                return;
            }

            cache.Created++;
            cache.PixelsConverted += (long)alpha.Width * alpha.Height;
            owned = !cache.Keep(entry, alpha, (long)alpha.Width * alpha.Height, default, null, delivered);
        }

        try
        {
            // The mask's samples map onto the unit square at their own resolution (§8.9.6.2, §8.9.6.3, §11.6.5.2).
            PaintSamples(alpha, entry, new SampleGrid(alpha.Width, alpha.Height, width, height, delivered), interpolate, use, clipEdges);
        }
        finally
        {
            if (owned)
            {
                alpha.Dispose();
            }
        }
    }

    /// <summary>
    /// The grid of samples drawn: <see cref="Width"/> × <see cref="Height"/> samples of an image of <see cref="SourceWidth"/> ×
    /// <see cref="SourceHeight"/>, reduced by 2^<see cref="Reduction"/> (issue #362). Reduced sample i covers source samples i·s to
    /// (i + 1)·s (ISO 32000-2 §8.9.4), so the grid maps to the unit square through s, then [1/W 0 0 −1/H 0 1], and reaches past it
    /// when s does not divide the size.
    /// </summary>
    private readonly record struct SampleGrid(int Width, int Height, int SourceWidth, int SourceHeight, int Reduction)
    {
        /// <summary>Gets the transform from the grid (y down from the first row) to the unit square.</summary>
        public Matrix ToUnitSquare => Reduction == 0
            ? new Matrix(1.0 / Width, 0, 0, -1.0 / Height, 0, 1)
            : new Matrix((double)(1 << Reduction) / SourceWidth, 0, 0, -(double)(1 << Reduction) / SourceHeight, 0, 1);

        /// <summary>Gets the unit square's width on the grid.</summary>
        public double OutlineWidth => Reduction == 0 ? Width : (double)SourceWidth / (1 << Reduction);

        /// <summary>Gets the unit square's height on the grid.</summary>
        public double OutlineHeight => Reduction == 0 ? Height : (double)SourceHeight / (1 << Reduction);

        /// <summary>Gets a value indicating whether the grid reaches past the unit square.</summary>
        public bool Overhangs => Reduction > 0 && (OutlineWidth != Width || OutlineHeight != Height);

        /// <summary>Gets the rectangle of user space (y up) the samples cover: the unit square, or more for a grid that reaches past it.</summary>
        public CGRect UnitSquareRect
        {
            get
            {
                if (!Overhangs)
                {
                    return new CGRect(0, 0, 1, 1);
                }

                double width = Width / OutlineWidth;
                double height = Height / OutlineHeight;
                return new CGRect(0, 1 - height, width, height);
            }
        }
    }
}
