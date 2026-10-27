using System.Runtime.InteropServices;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// Transparency groups and soft masks (ISO 32000-2 §11.3.5, §11.4, §11.5, §11.6.5, §11.6.6, §11.7.4.4) on Direct2D: groups Direct2D
/// can composite are layers with an opacity and an opacity brush; Direct2D's primitive blend is source-over only, so groups whose
/// content blends in isolation and knockout groups are drawn by the software rasterizer into a bitmap drawn in their place, and a
/// page that blends with its backdrop in any other blend mode is drawn by the software rasterizer whole. Soft-mask values are
/// computed by the software rasterizer and are the opacity brush of a layer around each masked paint or group.
/// </summary>
internal sealed unsafe partial class Direct2DBackend
{
    /// <summary>The most pixels a group or soft mask is rasterized over (8192 × 8192).</summary>
    private const long MaxRasterPixels = 1L << 26;

    /// <summary>The soft-mask images kept across renders.</summary>
    private const int MaskCacheSize = 8;

    private static readonly Diagnostic KnockoutRasterized = new(
        "BackendKnockoutGroupRasterized",
        DiagnosticSeverity.Information,
        "A knockout transparency group (ISO 32000-2 §11.4.6) that Direct2D cannot composite was drawn by the software rasterizer and drawn as a bitmap.");

    private static readonly Diagnostic ColorSpaceRasterized = new(
        "BackendGroupColorSpaceRasterized",
        DiagnosticSeverity.Information,
        "An isolated transparency group whose content blends in a gray or CMYK group colour space (ISO 32000-2 §11.4.7, §11.3.4) was drawn by the software rasterizer and drawn as a bitmap.");

    private static readonly Diagnostic BlendingGroupRasterized = new(
        "BackendBlendingGroupRasterized",
        DiagnosticSeverity.Information,
        "An isolated transparency group whose content blends in a mode other than Normal (ISO 32000-2 §11.3.5, §11.4.5) was drawn by the software rasterizer and drawn as a bitmap, as Direct2D's primitive blend is source-over only.");

    private static readonly Diagnostic SoftMaskRasterized = new(
        "BackendSoftMaskRasterized",
        DiagnosticSeverity.Information,
        "Soft-mask values (ISO 32000-2 §11.5, §11.6.5.1) were computed by the software rasterizer and applied by Direct2D as a layer's opacity brush.");

    private static readonly Diagnostic GroupApproximated = new(
        "BackendTransparencyGroupApproximated",
        DiagnosticSeverity.Warning,
        "A transparency group or soft mask (ISO 32000-2 §11.4, §11.6.5.1) covered more than 2^26 device pixels, too many to rasterize; it was drawn as its content alone, unmasked.");

    private static readonly Diagnostic PageRasterized = new(
        "BackendPageRasterized",
        DiagnosticSeverity.Information,
        "The page blends with its backdrop in a mode other than Normal (ISO 32000-2 §11.3.5: paints, annotations or groups composited in another blend mode, or non-isolated groups whose content blends, §11.4.8); Direct2D's primitive blend is source-over only, so the software rasterizer drew the page.");

    private GroupInspector? _inspector;
    private SoftwareRasterizer? _rasterizer;
    private MaskImage?[]? _masks;
    private byte[]? _transfer;
    private long _maskClock;

    // The soft mask of the graphics state, saved and restored with it; whether the page needs the software rasterizer (on the root).
    private MaskImage? _softMask;
    private MaskImage?[] _savedMasks = new MaskImage?[16];
    private bool _rasterizePage;

    /// <summary>
    /// Gets a value indicating whether the last replay met a paint, annotation or group that blends with its backdrop in a mode other
    /// than Normal, which Direct2D cannot composite: the renderer draws such a page with the software rasterizer.
    /// </summary>
    public bool RasterizesPage => _rasterizePage;

    private Direct2DBackend Root => _owner ?? this;

    /// <summary>Returns the software rasterizer groups and soft masks fall back to, decoding images as this backend does (issue #362).</summary>
    private SoftwareRasterizer FallbackRasterizer()
    {
        SoftwareRasterizer rasterizer = _rasterizer ??= new SoftwareRasterizer();
        ConfigureScaledImageDecode(rasterizer, ImageSamplesPerPixel);
        return rasterizer;
    }

    /// <summary>Sets a software rasterizer's scaled image decode (issue #362): on at <paramref name="samplesPerPixel"/>, or off at 0.</summary>
    internal static void ConfigureScaledImageDecode(SoftwareRasterizer rasterizer, double samplesPerPixel)
    {
        rasterizer.ScaledImageDecode = samplesPerPixel > 0;
        if (samplesPerPixel > 0)
        {
            rasterizer.ImageSamplesPerPixel = samplesPerPixel;
        }
    }

    /// <summary>Replaces the diagnostics of the replay, which drew nothing that is kept, by the one of a page the software rasterizer drew.</summary>
    public void ReportPageRasterized()
    {
        _diagnostics.Clear();
        _diagnostics.Add(PageRasterized);
    }

    /// <summary>
    /// Sets the soft mask of the paints that follow (§11.6.5.1): its values are rendered once per mask, placement and clip by the
    /// software rasterizer (§11.5.2 alpha, §11.5.3 luminosity 0.30 R + 0.59 G + 0.11 B, then the transfer function) and are the
    /// opacity brush of a layer around each masked paint. <see cref="DisplaySoftMask.IsNone"/> removes it.
    /// </summary>
    public void SetSoftMask(in DisplaySoftMask mask)
    {
        if (!Root._rasterizePage)
        {
            _softMask = mask.IsNone ? null : GetMask(mask);
        }
    }

    /// <summary>
    /// Draws a transparency group (§11.4): content that composites as if drawn directly (§11.4.4 NOTE 5) is replayed; groups whose
    /// content does not blend are layers composited with the group's opacity and soft mask (an isolated group's layer starts
    /// transparent, §11.4.5, and a non-isolated group whose content does not blend has the same result). Knockout groups (§11.4.6)
    /// and isolated groups whose content blends are drawn by the software rasterizer; a group that blends with its backdrop (its own
    /// blend mode is not Normal, it is non-isolated and its content blends, or it is a fill and stroke whose paints blend) makes the
    /// page the software rasterizer's.
    /// </summary>
    public void DrawGroup(in DisplayGroup group)
    {
        if (Root._rasterizePage)
        {
            return;
        }

        GroupContent content = (_inspector ??= new GroupInspector()).Inspect(group, _compositing.BlendMode);
        if (group.BlendMode != BlendMode.Normal || (content.Blends && (!group.IsIsolated || group.Kind == DisplayGroupKind.FillAndStroke)))
        {
            // The group blends with its backdrop: as a whole (its blend mode, or the fill and stroke's paints, which carry theirs), or
            // through its content (non-isolated, §11.4.8). Direct2D cannot read the backdrop to blend with it.
            Root._rasterizePage = true;
            return;
        }

        if (group.IsKnockout && content.Paints > 1)
        {
            Skip(SkippedOperations.KnockoutRasterized, KnockoutRasterized);
            Rasterize(group);
            return;
        }

        bool masked = !group.SoftMask.IsNone;
        if (!content.Blends && group.Opacity >= 1 && !masked)
        {
            group.Replay(this);
            return;
        }

        if (content.Blends)
        {
            if (group.ColorSpace is { ComponentCount: not 3 })
            {
                Skip(SkippedOperations.ColorSpaceRasterized, ColorSpaceRasterized);
            }
            else
            {
                Skip(SkippedOperations.BlendingGroupRasterized, BlendingGroupRasterized);
            }

            Rasterize(group);
            return;
        }

        DrawLayer(group);
    }

    // The paints: each one under a soft mask is drawn in a layer whose opacity brush is the mask's values (§11.6.4.3, §11.6.5.1);
    // one in a blend mode other than Normal makes the page the software rasterizer's (§11.3.5).
    public void FillPath(PathView path, FillRule rule, in DisplayColor color)
    {
        if (BeginMasked(out int mark))
        {
            FillPathCore(path, rule, color);
            EndMasked(mark);
        }
    }

    public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
    {
        if (BeginMasked(out int mark))
        {
            StrokePathCore(path, style, color);
            EndMasked(mark);
        }
    }

    public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
    {
        if (BeginMasked(out int mark))
        {
            DrawImageCore(image, defaultColorSpaces, conversion);
            EndMasked(mark);
        }
    }

    public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
    {
        if (BeginMasked(out int mark))
        {
            FillImageMaskCore(mask, color);
            EndMasked(mark);
        }
    }

    public void DrawGlyphs(in GlyphRun run)
    {
        if (BeginMasked(out int mark))
        {
            DrawGlyphsCore(run);
            EndMasked(mark);
        }
    }

    public void DrawShading(in DisplayShading shading)
    {
        if (BeginMasked(out int mark))
        {
            DrawShadingCore(shading);
            EndMasked(mark);
        }
    }

    public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
    {
        if (BeginMasked(out int mark))
        {
            FillPathWithShadingCore(path, rule, shading);
            EndMasked(mark);
        }
    }

    public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
    {
        if (BeginMasked(out int mark))
        {
            StrokePathWithShadingCore(path, style, shading);
            EndMasked(mark);
        }
    }

    public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
    {
        if (BeginMasked(out int mark))
        {
            FillPathWithTilingPatternCore(path, rule, pattern);
            EndMasked(mark);
        }
    }

    public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
    {
        if (BeginMasked(out int mark))
        {
            StrokePathWithTilingPatternCore(path, style, pattern);
            EndMasked(mark);
        }
    }

    /// <summary>Clears the soft mask and, on the root backend, the page's need of the software rasterizer, at the start of a replay.</summary>
    private void BeginTransparency()
    {
        _softMask = null;
        if (_owner is null)
        {
            _rasterizePage = false;
        }
    }

    /// <summary>Releases the soft-mask images and their brushes, and the software rasterizer.</summary>
    private void ReleaseTransparency()
    {
        _rasterizer?.Dispose();
        _rasterizer = null;
        if (_masks is not null)
        {
            foreach (MaskImage? mask in _masks)
            {
                mask?.Dispose();
            }

            _masks = null;
        }
    }

    /// <summary>
    /// Starts a paint: false when nothing is to be drawn (the page is the software rasterizer's, the paint blends in a mode other than
    /// Normal, or the state's soft mask hides everything); with a soft mask, pushes its layer and returns the clip mark
    /// <see cref="EndMasked"/> pops back to (otherwise -1).
    /// </summary>
    private bool BeginMasked(out int mark)
    {
        mark = -1;
        Direct2DBackend root = Root;
        if (root._rasterizePage)
        {
            return false;
        }

        if (_compositing.BlendMode != BlendMode.Normal)
        {
            root._rasterizePage = true;
            return false;
        }

        if (_softMask is null)
        {
            return true;
        }

        if (_softMask.Image is null)
        {
            return false;
        }

        mark = _clipCount;
        PushLayer(Interop.InfiniteRect, 1, _softMask);
        return true;
    }

    private void EndMasked(int mark)
    {
        if (mark >= 0)
        {
            PopClips(mark);
        }
    }

    /// <summary>
    /// Pushes a layer over <paramref name="bounds"/> with <paramref name="opacity"/> and, when <paramref name="mask"/> is given, the
    /// mask's values placed on their device pixels as its opacity brush (outside them the mask hides everything, so the layer is also
    /// clipped to them). <see cref="RestoreState"/> or <see cref="PopClips"/> pops it.
    /// </summary>
    private void PushLayer(D2D_RECT_F bounds, float opacity, MaskImage? mask)
    {
        ID2D1BitmapBrush1* brush = null;
        if (mask is not null)
        {
            PixelRect area = mask.Bounds;
            D2D_RECT_F rectangle = Rectangle(area);
            PushRectangle(rectangle);
            bounds = Intersect(bounds, rectangle);
            brush = mask.Brush(_context);
        }

        var parameters = new D2D1_LAYER_PARAMETERS1
        {
            contentBounds = bounds,
            geometricMask = null,
            maskAntialiasMode = D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
            maskTransform = Interop.Matrix(Matrix.Identity),
            opacity = opacity,
            opacityBrush = (ID2D1Brush*)brush,
            layerOptions = D2D1_LAYER_OPTIONS1.D2D1_LAYER_OPTIONS1_NONE,
        };

        // The brush is placed on the device and the layer pushed under the identity, so the mask lands in the same place whether
        // Direct2D maps the opacity brush through the world transform or not.
        SetWorldTransform(Matrix.Identity);
        _context->PushLayer(&parameters, null);
        AddClip(new Clip(null, isLayer: true), bounds);
    }

    /// <summary>
    /// A group whose content does not blend (§11.4.5: isolated, or non-isolated with the same result): a layer the content draws into,
    /// starting transparent, composited source-over with the group's opacity through the group's soft mask (§11.6.4.3 NOTE 2), whose
    /// values are the layer's opacity brush. Inside, the state's soft mask starts as None (§11.6.6), except in the implicit group of a
    /// fill and stroke, whose paints carry it.
    /// </summary>
    private void DrawLayer(in DisplayGroup group)
    {
        MaskImage? mask = null;
        if (!group.SoftMask.IsNone)
        {
            mask = GetMask(group.SoftMask);
            if (mask is null)
            {
                group.Replay(this);
                return;
            }

            if (mask.Image is null)
            {
                return;
            }
        }

        SaveState();
        try
        {
            if (group.Kind != DisplayGroupKind.FillAndStroke)
            {
                _softMask = null;
            }

            D2D_RECT_F bounds = TryGetBounds(group.HasBoundingBox, group.BoundingBox, group.Matrix * _device, out PixelRect box)
                ? Rectangle(box)
                : Interop.InfiniteRect;
            if (mask is not null)
            {
                mask.Pins++;
            }

            PushLayer(bounds, Alpha(group.Opacity) / 255f, mask);
            group.Replay(this);
        }
        finally
        {
            if (mask is not null)
            {
                mask.Pins--;
            }

            RestoreState();
        }
    }

    /// <summary>
    /// Draws a group with the software rasterizer (§11.4) into a bitmap over the group's device pixels within the clip, from the
    /// transform and compositing state the content starts with (over a transparent backdrop, which is exact for the groups handed
    /// here: none of them blends with its backdrop), and draws the bitmap here source-over, through the state's soft mask for the
    /// implicit group of a fill and stroke.
    /// </summary>
    private void Rasterize(in DisplayGroup group)
    {
        PixelRect area = GetArea(group.HasBoundingBox, group.BoundingBox, group.Matrix * _device);
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        if ((long)area.Width * area.Height > MaxRasterPixels)
        {
            Skip(SkippedOperations.Groups, GroupApproximated);
            group.Replay(this);
            return;
        }

        MaskImage? mask = group.Kind == DisplayGroupKind.FillAndStroke ? _softMask : null;
        if (mask is { Image: null })
        {
            return;
        }

        SoftwareRasterizer rasterizer = Root.FallbackRasterizer();
        byte[] pixels = new byte[area.Width * area.Height * 4];
        using (var bitmap = new Bitmap(pixels, area.Width, area.Height, area.Width * 4))
        {
            rasterizer.Render(
                group,
                _document!,
                bitmap,
                _device * Matrix.CreateTranslation(-area.Left, -area.Top),
                _ctm,
                _compositing.StrokeAlpha / 255.0,
                _compositing.FillAlpha / 255.0,
                _compositing.BlendMode);
        }

        using PixelImage image = PixelImage.Own(pixels, area.Width, area.Height, 4);
        int mark = _clipCount;
        if (mask is not null)
        {
            PushLayer(Interop.InfiniteRect, 1, mask);
        }

        SetWorldTransform(Matrix.Identity);
        D2D_RECT_F destination = Rectangle(area);
        _context->DrawBitmap(image.Bitmap(_context), &destination, 1, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR, null, null);
        PopClips(mark);
    }

    /// <summary>
    /// Returns the image of a soft mask's values over the clip (§11.6.5.1), from the cache when the same mask was rendered at the
    /// same placement and clip; its <see cref="MaskImage.Image"/> is null when the mask hides everything there. Returns null when the
    /// area is too large to rasterize (the paints are then unmasked).
    /// </summary>
    private MaskImage? GetMask(in DisplaySoftMask mask)
    {
        Skip(SkippedOperations.SoftMaskRasterized, SoftMaskRasterized);
        Direct2DBackend root = Root;
        Matrix toPixels = mask.Matrix * _device;
        PixelRect clip = GetArea(false, default, toPixels);
        MaskImage?[] masks = root._masks ??= new MaskImage?[MaskCacheSize];
        foreach (MaskImage? entry in masks)
        {
            if (entry is not null && ReferenceEquals(entry.Content, mask.Content) && entry.ToPixels == toPixels && entry.Clip == clip)
            {
                entry.Stamp = ++root._maskClock;
                return entry;
            }
        }

        byte[] transfer = root._transfer ??= new byte[256];
        FillTransfer(mask.TransferFunction, transfer);
        bool luminosity = mask.Subtype == PdfSoftMaskType.Luminosity;
        uint backdrop = 0;
        if (luminosity && mask.ColorSpace is { } space)
        {
            Span<float> rgb = stackalloc float[4];
            _document!.GetColorConverter(space, mask.Conversion with { Target = DeviceColorModel.Rgb }, mask.DefaultColorSpaces).Convert(mask.Backdrop, rgb);
            backdrop = 0xFF000000u | ((uint)Channel(rgb[0]) << 16) | ((uint)Channel(rgb[1]) << 8) | Channel(rgb[2]);
        }

        PixelRect area = clip;
        if (transfer[luminosity ? Luminosity(backdrop) : 0] == 0 && TryGetBounds(mask.HasBoundingBox, mask.BoundingBox, toPixels, out PixelRect bounds))
        {
            // Outside the group's bounding box the value is the constant 0 (§11.6.5.1): only the box can show a masked paint.
            area = Intersect(area, bounds);
        }

        if ((long)area.Width * area.Height > MaxRasterPixels)
        {
            Skip(SkippedOperations.Groups, GroupApproximated);
            return null;
        }

#pragma warning disable CA2000 // Kept by the mask cache, which disposes it on eviction.
        PixelImage? image = area.Width <= 0 || area.Height <= 0 ? null : RenderMask(mask, toPixels, area, backdrop, luminosity, transfer);
        var created = new MaskImage(mask.Content, toPixels, clip, area, image) { Stamp = ++root._maskClock };
#pragma warning restore CA2000
        root.AddMask(created, this);
        return created;
    }

    /// <summary>Renders the mask group with the software rasterizer over its backdrop and turns each pixel into its mask value.</summary>
    private PixelImage RenderMask(in DisplaySoftMask mask, in Matrix toPixels, PixelRect area, uint backdrop, bool luminosity, byte[] transfer)
    {
        int width = area.Width;
        int height = area.Height;
        using var bitmap = new Bitmap(width, height);
        MemoryMarshal.Cast<byte, uint>(bitmap.Pixels).Fill(backdrop);
        SoftwareRasterizer rasterizer = Root.FallbackRasterizer();
        rasterizer.Render(mask.Content, bitmap, toPixels * Matrix.CreateTranslation(-area.Left, -area.Top));
        byte[] values = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<uint> row = MemoryMarshal.Cast<byte, uint>(bitmap.GetRow(y))[..width];
            Span<byte> output = values.AsSpan(y * width, width);
            for (int x = 0; x < width; x++)
            {
                uint pixel = row[x];
                output[x] = transfer[luminosity ? Luminosity(pixel) : (int)(pixel >> 24)];
            }
        }

        return PixelImage.Own(values, width, height, 1);
    }

    /// <summary>Keeps a mask image, evicting the least recently used one that no state, saved state or open group goes through.</summary>
    private void AddMask(MaskImage created, Direct2DBackend requester)
    {
        MaskImage?[] masks = _masks!;
        int victim = -1;
        for (int index = 0; index < masks.Length; index++)
        {
            MaskImage? entry = masks[index];
            if (entry is null)
            {
                victim = index;
                break;
            }

            if (entry.Pins == 0 && !IsReferenced(entry) && !requester.IsReferenced(entry) && (victim < 0 || entry.Stamp < masks[victim]!.Stamp))
            {
                victim = index;
            }
        }

        if (victim < 0)
        {
            victim = masks.Length;
            Array.Resize(ref _masks, masks.Length * 2);
            masks = _masks;
        }

        masks[victim]?.Dispose();
        masks[victim] = created;
    }

    private bool IsReferenced(MaskImage entry)
    {
        if (ReferenceEquals(_softMask, entry))
        {
            return true;
        }

        for (int index = 0; index < _depth; index++)
        {
            if (ReferenceEquals(_savedMasks[index], entry))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The whole device pixels of the clip, intersected with a bounding box mapped to the device pixels when there is one.</summary>
    private PixelRect GetArea(bool hasBox, in PdfRectangle box, in Matrix toPixels)
    {
        D2D_RECT_F clip = ClipBounds;
        if (!(clip.right > clip.left && clip.bottom > clip.top))
        {
            return default;
        }

        var area = new PixelRect(
            ToPixel(Math.Floor(clip.left)),
            ToPixel(Math.Floor(clip.top)),
            ToPixel(Math.Ceiling(clip.right)),
            ToPixel(Math.Ceiling(clip.bottom)));
        return TryGetBounds(hasBox, box, toPixels, out PixelRect bounds) ? Intersect(area, bounds) : area;
    }

    /// <summary>A soft mask's values over an area of the device pixels, keyed by the mask group, its placement and the clip.</summary>
    private sealed class MaskImage(DisplayList content, Matrix toPixels, PixelRect clip, PixelRect bounds, PixelImage? image) : IDisposable
    {
        private ID2D1BitmapBrush1* _brush;

        public DisplayList Content { get; } = content;

        public Matrix ToPixels { get; } = toPixels;

        public PixelRect Clip { get; } = clip;

        public PixelRect Bounds { get; } = bounds;

        /// <summary>Gets the values (8-bit alpha), or null when the mask hides the whole clip.</summary>
        public PixelImage? Image { get; } = image;

        public long Stamp { get; set; }

        public int Pins { get; set; }

        /// <summary>Returns the opacity brush of the values placed on their device pixels, made on the first call on the context's device.</summary>
        public ID2D1BitmapBrush1* Brush(ID2D1DeviceContext* context)
        {
            if (_brush is null)
            {
                ID2D1BitmapBrush1* brush;
                context->CreateBitmapBrush(Image!.Bitmap(context), null, null, &brush);
                brush->SetInterpolationMode1(D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR);
                D2D_MATRIX_3X2_F placement = Interop.Matrix(Matrix.CreateTranslation(Bounds.Left, Bounds.Top));
                brush->SetTransform(&placement);
                _brush = brush;
            }

            return _brush;
        }

        public void Dispose()
        {
            if (_brush is not null)
            {
                _brush->Release();
                _brush = null;
            }

            Image?.Dispose();
        }
    }

    private static PixelRect Intersect(PixelRect a, PixelRect b)
    {
        int left = Math.Max(a.Left, b.Left);
        int top = Math.Max(a.Top, b.Top);
        return new PixelRect(left, top, Math.Max(left, Math.Min(a.Right, b.Right)), Math.Max(top, Math.Min(a.Bottom, b.Bottom)));
    }

    /// <summary>The bounds of a bounding box mapped by <paramref name="m"/>, one unit wider on each side for anti-aliased edges; false when there is no usable box.</summary>
    private static bool TryGetBounds(bool hasBox, in PdfRectangle box, in Matrix m, out PixelRect bounds)
    {
        bounds = default;
        if (!hasBox)
        {
            return false;
        }

        PathPoint a = m.Transform(new PathPoint(box.Left, box.Bottom));
        PathPoint b = m.Transform(new PathPoint(box.Right, box.Bottom));
        PathPoint c = m.Transform(new PathPoint(box.Right, box.Top));
        PathPoint d = m.Transform(new PathPoint(box.Left, box.Top));
        double left = Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X))) - 1;
        double right = Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X))) + 1;
        double top = Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y))) - 1;
        double bottom = Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))) + 1;
        if (!double.IsFinite(left + right + top + bottom))
        {
            return false;
        }

        bounds = new PixelRect(ToPixel(left), ToPixel(top), ToPixel(right), ToPixel(bottom));
        return true;
    }

    /// <summary>A whole pixel coordinate kept within ±2^30, so that widths and areas cannot overflow.</summary>
    private static int ToPixel(double value) => (int)Math.Clamp(value, -(1 << 30), 1 << 30);

    /// <summary>The luminosity of an opaque premultiplied BGRA pixel, Y = 0.30 R + 0.59 G + 0.11 B (§11.5.3), as the software rasterizer computes it.</summary>
    private static int Luminosity(uint pixel) =>
        ((30 * (int)((pixel >> 16) & 0xFF)) + (59 * (int)((pixel >> 8) & 0xFF)) + (11 * (int)(pixel & 0xFF)) + 50) / 100;

    /// <summary>Fills a 256-entry table with the transfer function at each 8-bit input, clamped to 0..1 (Table 142, TR).</summary>
    private static void FillTransfer(PdfFunction? function, byte[] table)
    {
        if (function is not { InputCount: 1, OutputCount: 1 })
        {
            for (int index = 0; index < 256; index++)
            {
                table[index] = (byte)index;
            }

            return;
        }

        Span<float> inputs = stackalloc float[256];
        Span<float> outputs = stackalloc float[256];
        for (int index = 0; index < 256; index++)
        {
            inputs[index] = index / 255f;
        }

        function.Evaluate(inputs, outputs, 256);
        for (int index = 0; index < 256; index++)
        {
            float value = outputs[index];
            table[index] = (byte)Math.Round((float.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1)) * 255);
        }
    }

    /// <summary>What a group's content paints, as <see cref="GroupInspector"/> finds it.</summary>
    /// <param name="Paints">How many paints (a glyph run that fills and strokes counts twice, a nested group twice).</param>
    /// <param name="Blends">Whether a paint or nested group composites with a blend mode other than Normal.</param>
    /// <param name="Uniform">Whether every paint composites with the same blend mode, <paramref name="Blend"/>.</param>
    /// <param name="Blend">The blend mode of the first paint.</param>
    /// <param name="PathsOnly">Whether every paint is a fill or stroke of a path or glyphs in a colour.</param>
    /// <param name="HasGroups">Whether the content holds groups.</param>
    /// <param name="ChangesMask">Whether the content sets a soft mask.</param>
    private readonly record struct GroupContent(int Paints, bool Blends, bool Uniform, BlendMode Blend, bool PathsOnly, bool HasGroups, bool ChangesMask);

    /// <summary>A backend that paints nothing and notes what a group's content paints and with which blend modes.</summary>
    private sealed class GroupInspector : IRenderingBackend
    {
        private BlendMode[] _saved = new BlendMode[16];
        private int _depth;
        private BlendMode _blend;
        private int _paints;
        private bool _blends;
        private bool _uniform;
        private BlendMode _first;
        private bool _pathsOnly;
        private bool _hasGroups;
        private bool _changesMask;

        public GroupContent Inspect(in DisplayGroup group, BlendMode held)
        {
            (_depth, _blend, _paints, _blends, _uniform, _first, _pathsOnly, _hasGroups, _changesMask) = (0, held, 0, false, true, BlendMode.Normal, true, false, false);
            group.Replay(this);
            return new GroupContent(_paints, _blends, _uniform, _first, _pathsOnly, _hasGroups, _changesMask);
        }

        public void SaveState()
        {
            if (_depth == _saved.Length)
            {
                Array.Resize(ref _saved, _saved.Length * 2);
            }

            _saved[_depth++] = _blend;
        }

        public void RestoreState()
        {
            if (_depth > 0)
            {
                _blend = _saved[--_depth];
            }
        }

        public void SetTransform(in Matrix transform)
        {
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color) => Paint(path: true, 1);

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color) => Paint(path: true, 1);

        public void IntersectClip(PathView path, FillRule rule)
        {
        }

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) => Paint(path: false, 1);

        public void DrawGlyphs(in GlyphRun run) => Paint(path: true, (run.Fills ? 1 : 0) + (run.Strokes ? 1 : 0));

        public void DrawShading(in DisplayShading shading) => Paint(path: false, 1);

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading) => Paint(path: false, 1);

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading) => Paint(path: false, 1);

        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern) => Paint(path: false, 1);

        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern) => Paint(path: false, 1);

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color) => Paint(path: false, 1);

        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode) => _blend = blendMode;

        public void SetSoftMask(in DisplaySoftMask mask) => _changesMask = true;

        public void DrawGroup(in DisplayGroup group)
        {
            _hasGroups = true;
            _uniform = false;
            Paint(path: false, 2);
            _blends |= group.BlendMode != BlendMode.Normal;
            BlendMode blend = _blend;
            group.Replay(this);
            _blend = blend;
        }

        public void BeginAnnotation(in DisplayAnnotation annotation)
        {
        }

        public void EndAnnotation()
        {
        }

        public void SetDeviceParameters(in DisplayDeviceParameters parameters)
        {
        }

        private void Paint(bool path, int count)
        {
            if (count == 0)
            {
                return;
            }

            _pathsOnly &= path;
            _blends |= _blend != BlendMode.Normal;
            if (_paints == 0)
            {
                _first = _blend;
            }
            else if (_first != _blend)
            {
                _uniform = false;
            }

            _paints += count;
        }
    }
}
