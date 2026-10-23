using Broadside.Diagnostics;
using Broadside.Graphics;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// Images and stencil masks (ISO 32000-2 §8.9, spec D15, D16): each interned display-list image becomes one Direct2D bitmap per
/// backend, drawn into the unit square under the current transform; masks become 8-bit alpha bitmaps that clip as the opacity brush of
/// a layer.
/// </summary>
internal sealed unsafe partial class Direct2DBackend : IDisposable
{
    private static readonly Diagnostic ImageTooLarge = new(
        "BackendImageTooLarge",
        DiagnosticSeverity.Warning,
        "An image or mask has more pixels than the Direct2D backend can convert in memory (ISO 32000-2 §8.9.5); it was left out of this render.");

    private readonly ImageCache _images;
    private readonly WeakReference<PdfDocument?> _budgetDocument = new(null);

    /// <summary>What a mask or image's samples do: drawn, intersected with the clip, or filled with the fill colour through.</summary>
    private enum SampleUse
    {
        Draw,
        Clip,
        Fill,
    }

    /// <summary>Gets how many Direct2D bitmaps were made from display-list images and masks.</summary>
    internal int ImagesCreated => _images.Created;

    /// <summary>Gets the bytes of converted pixels kept.</summary>
    internal long CachedImageBytes => _images.Bytes;

    /// <summary>
    /// Draws an image (ISO 32000-2 §8.9.4, §8.9.5): image space maps to the unit square through [1/w 0 0 −1/h 0 1], then the
    /// current transform. A mask of the image's size is folded into the pixels; a soft mask (§11.6.5.2) or explicit mask (§8.9.6.3)
    /// of another size clips the image at its own resolution, so each is resampled from its own grid.
    /// </summary>
    private void DrawImageCore(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
    {
        // An image kept encoded that cannot be decoded paints nothing (ISO 32000-2 §8.9.5; its reason is a diagnostic).
        if (_compositing.FillAlpha == 0 || image.GetSamplesAt(0) is null)
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
                Skip(SkippedOperations.ImageTooLarge, ImageTooLarge);
                return;
            }

            cache.Created++;
            owned = !cache.Keep(entry, pixels, (long)image.Width * image.Height * 4, rgb, defaultColorSpaces);
        }

        int mark = _clipCount;
        try
        {
            if (SeparateMask(image) is { } mask)
            {
                PaintMask(mask, SampleUse.Clip, clipEdges: false, 1);
            }

            PaintSamples(pixels, entry, image.Width, image.Height, image.Interpolate, SampleUse.Draw, clipEdges: true, _compositing.FillAlpha / 255f);
        }
        finally
        {
            PopClips(mark);
            if (owned)
            {
                pixels.Dispose();
            }
        }
    }

    /// <summary>
    /// Fills a stencil mask with the colour (ISO 32000-2 §8.9.6.2): the mask maps onto the unit square like an image and is the opacity
    /// of a fill of the unit square in the colour.
    /// </summary>
    private void FillImageMaskCore(DisplayImageMask mask, in DisplayColor color)
    {
        PdfColorConverter converter = Converter(color);
        if (_compositing.FillAlpha == 0 || converter.PaintsNothing)
        {
            return;
        }

        SetColor(converter, color.Components, _compositing.FillAlpha);
        int mark = _clipCount;
        try
        {
            PaintMask(mask, SampleUse.Fill, clipEdges: true, 1);
        }
        finally
        {
            PopClips(mark);
        }
    }

    /// <summary>Releases the converted images, shadings and pattern tiles, with their Direct2D resources.</summary>
    public void Dispose()
    {
        ReleaseDeviceResources();
    }

    /// <summary>
    /// Releases everything this backend made on the device (the images', shadings' and tiles' Direct2D resources, with the pixels they
    /// were made from): the device was lost, or the renderer is disposed.
    /// </summary>
    internal void ReleaseDeviceResources()
    {
        if (_owner is not null)
        {
            return;
        }

        _images.Dispose();
        _shadings.Dispose();
        ClearTiles();
        ReleaseTransparency();
    }

    /// <summary>Reads the document's image budget at the start of a replay.</summary>
    private void BeginImages(PdfDocument document)
    {
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

    /// <summary>
    /// The interpolation of an image Direct2D resamples itself: reduced on either axis (rotated, or enlarged on the other axis), its
    /// high-quality filter, which averages the samples under a pixel; enlarged, square samples unless the image asks for interpolation
    /// (§8.9.5.3).
    /// </summary>
    private static D2D1_INTERPOLATION_MODE Quality(in Matrix imageToDevice, bool interpolate)
    {
        double x = Math.Sqrt((imageToDevice.A * imageToDevice.A) + (imageToDevice.B * imageToDevice.B));
        double y = Math.Sqrt((imageToDevice.C * imageToDevice.C) + (imageToDevice.D * imageToDevice.D));
        if (x < 1 || y < 1)
        {
            return D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC;
        }

        return interpolate ? D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR : D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR;
    }

    /// <summary>
    /// Draws, clips to or fills through samples of <paramref name="width"/> × <paramref name="height"/> mapped into the unit square
    /// under the current transform. Mapped onto few enough device pixels, they are resampled to those pixels (<see cref="ImageReducer"/>,
    /// kept with the image) and placed on them one to one, the outline clipped with anti-aliasing unless <paramref name="clipEdges"/> is
    /// off (a mask whose image clips its own edges); otherwise Direct2D samples them under the transform. Clips are left pushed for the
    /// caller to pop.
    /// </summary>
    private void PaintSamples(PixelImage samples, ImageCache.Entry entry, int width, int height, bool interpolate, SampleUse use, bool clipEdges, float opacity)
    {
        Matrix toDevice = UnitSquare(width, height) * _toDevice;
        if (!toDevice.TryInvert(out _))
        {
            if (use == SampleUse.Clip)
            {
                PushRectangle(default);
            }

            return;
        }

        if (ImageReducer.Applies(width, height, toDevice, out PixelRect pixels, out bool upright))
        {
            PixelImage? reduced = null;
            bool owned = false;
            if (entry.Admit(toDevice, out int slot))
            {
                reduced = entry.ReductionAt(slot);
#pragma warning disable CA2000 // Kept by the cache, or disposed after drawing.
                if (reduced is null && ImageReducer.Reduce(samples, toDevice, pixels, interpolate) is { } made)
#pragma warning restore CA2000
                {
                    reduced = made;
                    long bytes = (long)pixels.Width * pixels.Height * samples.Channels;
                    owned = !_images.KeepReduction(entry, slot, made, bytes);
                }
            }

            if (reduced is not null)
            {
                if (clipEdges)
                {
                    ClipToOutline(toDevice, width, height, upright);
                }

                var block = new D2D_RECT_F { left = pixels.Left, top = pixels.Top, right = pixels.Right, bottom = pixels.Bottom };
                Apply(reduced, Matrix.Identity, block, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR, use, opacity);
                if (owned)
                {
                    reduced.Dispose();
                }

                return;
            }
        }

        // Image space (y down, as the samples' rows) is the user space of the draw.
        var whole = new D2D_RECT_F { left = 0, top = 0, right = width, bottom = height };
        Apply(samples, toDevice, whole, Quality(toDevice, interpolate), use, opacity);
    }

    /// <summary>
    /// Draws, clips to or fills through an image placed on <paramref name="destination"/> under the world transform
    /// <paramref name="world"/>. A clip is a layer whose opacity brush is the image; a fill fills the destination with the solid brush
    /// through that layer.
    /// </summary>
    private void Apply(PixelImage image, in Matrix world, in D2D_RECT_F destination, D2D1_INTERPOLATION_MODE interpolation, SampleUse use, float opacity)
    {
        ID2D1Bitmap* bitmap = image.Bitmap(_context);
        SetWorldTransform(world);
        if (use == SampleUse.Draw)
        {
            D2D_RECT_F rectangle = destination;
            _context->DrawBitmap(bitmap, &rectangle, opacity, interpolation, null, null);
            return;
        }

        ID2D1BitmapBrush1* brush = _surface!.MaskBrush;
        brush->SetBitmap(bitmap);
        brush->SetInterpolationMode1(interpolation);
        brush->SetExtendModeX(D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP);
        brush->SetExtendModeY(D2D1_EXTEND_MODE.D2D1_EXTEND_MODE_CLAMP);
        brush->SetOpacity(1);
        var placement = new Matrix(
            (destination.right - destination.left) / image.Width,
            0,
            0,
            (destination.bottom - destination.top) / image.Height,
            destination.left,
            destination.top);
        // The brush is placed on the device directly and the layer pushed under the identity, so the mask lands in the same place
        // whether Direct2D maps the opacity brush through the world transform or not.
        D2D_MATRIX_3X2_F transform = Interop.Matrix(placement * world);
        brush->SetTransform(&transform);
        SetWorldTransform(Matrix.Identity);
        var parameters = new D2D1_LAYER_PARAMETERS1
        {
            contentBounds = Interop.InfiniteRect,
            geometricMask = null,
            maskAntialiasMode = D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
            maskTransform = Interop.Matrix(Matrix.Identity),
            opacity = 1,
            opacityBrush = (ID2D1Brush*)brush,
            layerOptions = D2D1_LAYER_OPTIONS1.D2D1_LAYER_OPTIONS1_NONE,
        };
        _context->PushLayer(&parameters, null);
        AddClip(new Clip(null, isLayer: true), _area);
        if (use == SampleUse.Fill)
        {
            SetWorldTransform(world);
            D2D_RECT_F rectangle = destination;
            _context->FillRectangle(&rectangle, (ID2D1Brush*)_brush);
        }
    }

    /// <summary>Intersects the clip with the outline of the samples on the device, anti-aliased like a fill.</summary>
    private void ClipToOutline(in Matrix toDevice, int width, int height, bool upright) =>
        PushQuad(toDevice.Transform(0, 0), toDevice.Transform(width, 0), toDevice.Transform(width, height), toDevice.Transform(0, height), upright);

    /// <summary>
    /// Pushes a layer whose geometric mask is <paramref name="geometry"/> (owned from now on) mapped to the device by
    /// <paramref name="toDevice"/>; <paramref name="aliased"/> keeps whole pixels whose centre is inside, as the rasterizer's per-pixel
    /// shading evaluation does.
    /// </summary>
    private void PushGeometry(ID2D1PathGeometry* geometry, in Matrix toDevice, bool aliased = false)
    {
        var parameters = new D2D1_LAYER_PARAMETERS1
        {
            contentBounds = Interop.InfiniteRect,
            geometricMask = (ID2D1Geometry*)geometry,
            maskAntialiasMode = aliased ? D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED : D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
            maskTransform = Interop.Matrix(toDevice),
            opacity = 1,
            opacityBrush = null,
            layerOptions = D2D1_LAYER_OPTIONS1.D2D1_LAYER_OPTIONS1_NONE,
        };
        SetWorldTransform(Matrix.Identity);
        try
        {
            _context->PushLayer(&parameters, null);
        }
        catch
        {
            geometry->Release();
            throw;
        }

        AddClip(new Clip((ID2D1Geometry*)geometry), GeometryBounds((ID2D1Geometry*)geometry, toDevice));
    }

    /// <summary>Clips to or fills through a mask (a soft mask image or a stencil) as an alpha plane over the unit square.</summary>
    private void PaintMask(object mask, SampleUse use, bool clipEdges, float opacity)
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
                Skip(SkippedOperations.ImageTooLarge, ImageTooLarge);
                if (use == SampleUse.Clip)
                {
                    PushRectangle(default);
                }

                return;
            }

            cache.Created++;
            owned = !cache.Keep(entry, alpha, (long)width * height, default, null);
        }

        try
        {
            // The mask's samples map onto the unit square at their own resolution (§8.9.6.2, §8.9.6.3, §11.6.5.2).
            PaintSamples(alpha, entry, width, height, interpolate, use, clipEdges, opacity);
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
