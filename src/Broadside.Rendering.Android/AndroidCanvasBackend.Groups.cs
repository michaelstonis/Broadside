using System.Runtime.InteropServices;
using Broadside.Diagnostics;
using Broadside.Graphics;
using AndroidCanvas = Android.Graphics.Canvas;
using Matrix = Broadside.Graphics.Matrix;
using Paint = Android.Graphics.Paint;
using Rect = Android.Graphics.Rect;

namespace Broadside.Rendering.Android;

/// <summary>
/// Transparency groups and soft masks (ISO 32000-2 §11.4, §11.5, §11.6.5, §11.6.6, §11.7.4.4) on the Android canvas: groups the
/// canvas can composite are canvas layers, the others are drawn by the software rasterizer into a bitmap composited in their place;
/// soft-mask values are computed by the software rasterizer and applied to each masked paint, or a group's layer, as an alpha bitmap
/// drawn destination-in.
/// </summary>
public sealed partial class AndroidCanvasBackend
{
    private sealed partial class Backend
    {
        /// <summary>The most pixels a group or soft mask is rasterized over (8192 × 8192).</summary>
        private const long MaxRasterPixels = 1L << 26;

        /// <summary>The soft-mask bitmaps kept across renders.</summary>
        private const int MaskCacheSize = 8;

        /// <summary>The paint mode of a knockout layer's paints: replace what the layer holds by coverage (the canvas's Src).</summary>
        private const BlendMode Replace = (BlendMode)(-1);

        private static readonly Diagnostic KnockoutRasterized = new(
            "BackendKnockoutGroupRasterized",
            DiagnosticSeverity.Information,
            "A knockout transparency group (ISO 32000-2 §11.4.6) that the Android canvas cannot composite was drawn by the software rasterizer and composited as a bitmap.");

        private static readonly Diagnostic ColorSpaceRasterized = new(
            "BackendGroupColorSpaceRasterized",
            DiagnosticSeverity.Information,
            "A transparency group that blends in a gray or CMYK group colour space (ISO 32000-2 §11.4.7, §11.3.4) was drawn by the software rasterizer and composited as a bitmap.");

        private static readonly Diagnostic NonIsolatedRasterized = new(
            "BackendNonIsolatedGroupRasterized",
            DiagnosticSeverity.Information,
            "A non-isolated transparency group whose content blends in one blend mode (ISO 32000-2 §11.4.5, §11.4.8) was drawn by the software rasterizer and composited as a bitmap in that blend mode, as the Android canvas has no layer that starts from the backdrop.");

        private static readonly Diagnostic NonIsolatedApproximated = new(
            "BackendNonIsolatedGroupApproximated",
            DiagnosticSeverity.Warning,
            "A non-isolated transparency group whose content blends (ISO 32000-2 §11.4.5, §11.4.8) in several blend modes, in nested groups, or under a group blend mode was drawn by the software rasterizer against a transparent backdrop, as if isolated.");

        private static readonly Diagnostic SoftMaskRasterized = new(
            "BackendSoftMaskRasterized",
            DiagnosticSeverity.Information,
            "Soft-mask values (ISO 32000-2 §11.5, §11.6.5.1) were computed by the software rasterizer and applied by the Android canvas.");

        private static readonly Diagnostic GroupApproximated = new(
            "BackendTransparencyGroupApproximated",
            DiagnosticSeverity.Warning,
            "A transparency group or soft mask (ISO 32000-2 §11.4, §11.6.5.1) covered more than 2^26 device pixels, too many to rasterize; it was drawn as its content alone, unmasked.");

        // The paint of a group's layer or rasterized bitmap (alpha, blend mode), and of a soft mask's values drawn into a mask layer.
        private readonly Paint _groupPaint = new();
        private readonly Paint _maskValuesPaint = new();
        private readonly Rect _groupClip = new();
        private BlendMode _groupBlendMode;

        private GroupInspector? _inspector;
        private SoftwareRasterizer? _rasterizer;
        private MaskImage?[]? _masks;
        private byte[]? _transfer;
        private long _maskClock;

        // The soft mask of the graphics state, saved and restored with it; whether a layer already carries it (its paints are not
        // masked again); whether paints replace (knock out) what the current knockout layer holds; the open masked paint's layer
        // (its canvas save count, or -1) and the compositing state it was drawn under.
        private MaskImage? _softMask;
        private bool _masking;
        private bool _knockout;
        private int _maskedPaintCount = -1;
        private Compositing _maskedCompositing;

        private Backend Root => _owner ?? this;

        /// <summary>
        /// Sets the soft mask of the paints that follow (§11.6.5.1): its values are rendered once per mask, placement and clip by the
        /// software rasterizer (§11.5.2 alpha, §11.5.3 luminosity 0.30 R + 0.59 G + 0.11 B, then the transfer function) and applied to
        /// each masked paint as an alpha bitmap. <see cref="DisplaySoftMask.IsNone"/> removes it.
        /// </summary>
        public void SetSoftMask(in DisplaySoftMask mask) => _softMask = mask.IsNone ? null : GetMask(mask);

        /// <summary>
        /// Draws a transparency group (§11.4): content that composites as if drawn directly (§11.4.4 NOTE 5) is replayed; isolated
        /// groups, and non-isolated ones whose content does not blend, are canvas layers composited with the group's opacity, blend
        /// mode and soft mask; the implicit knockout group of a fill and stroke (§11.7.4.4) is a layer its paints replace into. Every
        /// other group, knockout, non-isolated with blending content, or blending in a gray or CMYK colour space, is drawn by the
        /// software rasterizer.
        /// </summary>
        public void DrawGroup(in DisplayGroup group)
        {
            GroupContent content = (_inspector ??= new GroupInspector()).Inspect(group, _compositing.BlendMode);
            bool masked = !group.SoftMask.IsNone;
            if (group.IsKnockout && content.Paints > 1)
            {
                if (group.Kind == DisplayGroupKind.FillAndStroke && content.PathsOnly && content.Uniform && !content.ChangesMask
                    && OperatingSystem.IsAndroidVersionAtLeast(29))
                {
                    DrawKnockoutLayer(group, content.Blend);
                    return;
                }

                Report(Reported.KnockoutRasterized, KnockoutRasterized);
                Rasterize(group, content);
                return;
            }

            if (group.Opacity >= 1 && group.BlendMode == BlendMode.Normal && !masked && (!group.IsIsolated || !content.Blends))
            {
                group.Replay(this);
                return;
            }

            if ((content.Blends || group.BlendMode != BlendMode.Normal) && group.ColorSpace is { ComponentCount: not 3 })
            {
                Report(Reported.ColorSpaceRasterized, ColorSpaceRasterized);
                Rasterize(group, content);
                return;
            }

            if (!group.IsIsolated && content.Blends)
            {
                if (ComposesInItsBlend(group, content))
                {
                    Report(Reported.NonIsolatedRasterized, NonIsolatedRasterized);
                }

                Rasterize(group, content);
                return;
            }

            DrawIsolatedLayer(group);
        }

        // The paints: each one under a soft mask is drawn into a layer that the mask's values keep destination-in (§11.6.4.3, §11.6.5.1).
        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            if (BeginMasked())
            {
                FillPathCore(path, rule, color);
                EndMaskedPaint();
            }
        }

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
            if (BeginMasked())
            {
                StrokePathCore(path, style, color);
                EndMaskedPaint();
            }
        }

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            if (BeginMasked())
            {
                DrawImageCore(image, defaultColorSpaces, conversion);
                EndMaskedPaint();
            }
        }

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
        {
            if (BeginMasked())
            {
                FillImageMaskCore(mask, color);
                EndMaskedPaint();
            }
        }

        public void DrawGlyphs(in GlyphRun run)
        {
            if (BeginMasked())
            {
                DrawGlyphsCore(run);
                EndMaskedPaint();
            }
        }

        public void DrawShading(in DisplayShading shading)
        {
            if (BeginMasked())
            {
                DrawShadingCore(shading);
                EndMaskedPaint();
            }
        }

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
        {
            if (BeginMasked())
            {
                FillPathWithShadingCore(path, rule, shading);
                EndMaskedPaint();
            }
        }

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
            if (BeginMasked())
            {
                StrokePathWithShadingCore(path, style, shading);
                EndMaskedPaint();
            }
        }

        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
        {
            if (BeginMasked())
            {
                FillPathWithTilingPatternCore(path, rule, pattern);
                EndMaskedPaint();
            }
        }

        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
        {
            if (BeginMasked())
            {
                StrokePathWithTilingPatternCore(path, style, pattern);
                EndMaskedPaint();
            }
        }

        /// <summary>
        /// Whether a non-isolated group of blending content composites exactly enough as its isolated result drawn in its paints' one
        /// blend mode: the group's own mode is Normal, every paint blends in one mode and there are no nested groups.
        /// </summary>
        private static bool ComposesInItsBlend(in DisplayGroup group, in GroupContent content) =>
            group.BlendMode == BlendMode.Normal && content.Uniform && !content.HasGroups;

        private void BeginTransparency()
        {
            _softMask = null;
            _masking = false;
            _knockout = false;
            _maskedPaintCount = -1;
        }

        private void DisposeTransparency()
        {
            _groupPaint.Dispose();
            _maskValuesPaint.Dispose();
            _groupClip.Dispose();
            _rasterizer?.Dispose();
            _rasterizer = null;
            if (_masks is not null)
            {
                foreach (MaskImage? mask in _masks)
                {
                    mask?.Image?.Dispose();
                }

                _masks = null;
            }
        }

        /// <summary>
        /// Opens the layer the paint that follows draws into when the state has a soft mask: composited with the paint's blend mode,
        /// the paint drawn into it in the Normal mode, so that <see cref="EndMaskedPaint"/> can keep it where the mask's values are.
        /// False when the mask hides everything (nothing to draw); true with nothing opened when there is no mask, or a layer already
        /// carries it.
        /// </summary>
        private bool BeginMasked()
        {
            if (_softMask is null || _masking)
            {
                return true;
            }

            if (_softMask.Image is null)
            {
                return false;
            }

            _maskedPaintCount = SaveLayer(_softMask, 255, _compositing.BlendMode);
            _maskedCompositing = _compositing;
            _compositing = _compositing with { BlendMode = BlendMode.Normal };
            return true;
        }

        /// <summary>Applies the soft mask to the open masked paint's layer and composites it, if one is open.</summary>
        private void EndMaskedPaint()
        {
            if (_maskedPaintCount >= 0)
            {
                _compositing = _maskedCompositing;
                ApplyMask(_softMask!, _maskedPaintCount);
                _maskedPaintCount = -1;
            }
        }

        /// <summary>
        /// Opens a canvas layer composited with <paramref name="alpha"/> and <paramref name="mode"/>, over the soft mask's area (outside
        /// it the mask's values are 0) when the canvas's coordinates are its pixels, else over the clip.
        /// </summary>
        private int SaveLayer(MaskImage? mask, byte alpha, BlendMode mode)
        {
            _groupPaint.Alpha = alpha;
            SetBlendMode(_groupPaint, ref _groupBlendMode, mode);
            if (mask is not null && _pixelsIdentity)
            {
                PixelRect area = mask.Bounds;
                return _canvas!.SaveLayer(area.Left, area.Top, area.Right, area.Bottom, _groupPaint);
            }

            return _canvas!.SaveLayer(null, _groupPaint);
        }

        /// <summary>
        /// Keeps what the layers opened since <paramref name="count"/> hold where the soft mask's values are (destination-in, through a
        /// layer of its own: the canvas draws an alpha bitmap as coverage), then composites them by restoring to <paramref name="count"/>.
        /// </summary>
        private void ApplyMask(MaskImage mask, int count)
        {
            AndroidCanvas canvas = _canvas!;
            canvas.SaveLayer(null, _maskInPaint);
            ConcatFromPixels();
            PixelRect area = mask.Bounds;
            canvas.DrawBitmap(mask.Image!.Bitmap, area.Left, area.Top, _maskValuesPaint);
            canvas.RestoreToCount(count);
        }

        /// <summary>
        /// An isolated group (§11.4.5), or a non-isolated one whose content does not blend (the same result): a canvas layer the
        /// content draws into, kept where the group's soft mask's values are (§11.6.4.3 NOTE 2) and composited with the group's
        /// opacity and blend mode.
        /// </summary>
        private void DrawIsolatedLayer(in DisplayGroup group)
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

                mask.Pins++;
            }

            int count = BeginLayer(group, Alpha(group.Opacity), group.BlendMode);
            bool masking = _masking;
            bool outer = _knockout;
            _masking = false;
            _knockout = false;
            try
            {
                group.Replay(this);
            }
            finally
            {
                _masking = masking;
                _knockout = outer;
                if (mask is not null)
                {
                    mask.Pins--;
                    ApplyMask(mask, count);
                }
                else
                {
                    _canvas!.RestoreToCount(count);
                }
            }
        }

        /// <summary>
        /// The implicit knockout group of a fill and stroke (§11.7.4.4) whose paints share one blend mode: a canvas layer the paints
        /// replace into by their coverage (the canvas's Src), composited onto the backdrop with that blend mode and the state's soft
        /// mask. For the Normal mode this is the knockout result exactly; for the others it differs only where the stroke's edge
        /// partly covers the fill.
        /// </summary>
        private void DrawKnockoutLayer(in DisplayGroup group, BlendMode blend)
        {
            bool carriesMask = _softMask is not null && !_masking;
            if (carriesMask && _softMask!.Image is null)
            {
                return;
            }

            MaskImage? mask = carriesMask ? _softMask : null;
            int count = BeginLayer(group, 255, blend);
            bool masking = _masking;
            bool knockout = _knockout;
            _masking |= carriesMask;
            _knockout = true;
            try
            {
                group.Replay(this);
            }
            finally
            {
                _knockout = knockout;
                _masking = masking;
                if (mask is not null)
                {
                    ApplyMask(mask, count);
                }
                else
                {
                    _canvas!.RestoreToCount(count);
                }
            }
        }

        /// <summary>Opens a canvas layer over the group's bounding box, or over the clip when it has none.</summary>
        private int BeginLayer(in DisplayGroup group, byte alpha, BlendMode mode)
        {
            _groupPaint.Alpha = alpha;
            SetBlendMode(_groupPaint, ref _groupBlendMode, mode);
            ReportBlendMode(mode);
            return TryGetBounds(group.HasBoundingBox, group.BoundingBox, group.Matrix * _device, out PixelRect bounds)
                ? _canvas!.SaveLayer(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, _groupPaint)
                : _canvas!.SaveLayer(null, _groupPaint);
        }

        /// <summary>
        /// Draws a group with the software rasterizer (§11.4) into a bitmap over the group's device pixels within the clip, from the
        /// transform and compositing state the content starts with, and composites the bitmap here: with the group's blend mode, or,
        /// for a non-isolated group in the Normal mode whose paints share one blend mode, with that mode (its paints blend with the
        /// backdrop). A non-isolated group whose content blends otherwise is reported: it was drawn against a transparent backdrop.
        /// </summary>
        private void Rasterize(in DisplayGroup group, in GroupContent content)
        {
            BlendMode composite = group.BlendMode;
            if (!group.IsIsolated && content.Blends)
            {
                if (ComposesInItsBlend(group, content))
                {
                    composite = content.Blend;
                }
                else
                {
                    Report(Reported.NonIsolatedApproximated, NonIsolatedApproximated);
                }
            }

            if (!_baseInvertible)
            {
                return;
            }

            PixelRect area = GetArea(group.HasBoundingBox, group.BoundingBox, group.Matrix * _device * _pixels);
            if (area.Width <= 0 || area.Height <= 0)
            {
                return;
            }

            if ((long)area.Width * area.Height > MaxRasterPixels)
            {
                Report(Reported.GroupApproximated, GroupApproximated);
                group.Replay(this);
                return;
            }

            bool carriesMask = group.Kind == DisplayGroupKind.FillAndStroke && _softMask is not null && !_masking;
            if (carriesMask && _softMask!.Image is null)
            {
                return;
            }

            Backend root = Root;
            SoftwareRasterizer rasterizer = root._rasterizer ??= new SoftwareRasterizer();
            byte[] pixels = new byte[area.Width * area.Height * 4];
            using (var bitmap = new Bitmap(pixels, area.Width, area.Height, area.Width * 4))
            {
                rasterizer.Render(
                    group,
                    _document!,
                    bitmap,
                    _device * _pixels * Matrix.CreateTranslation(-area.Left, -area.Top),
                    _ctm,
                    _compositing.StrokeAlpha / 255.0,
                    _compositing.FillAlpha / 255.0,
                    _compositing.BlendMode);
            }

            using PixelImage image = PixelImage.Own(pixels, area.Width, area.Height, 4);
            AndroidCanvas canvas = _canvas!;
            ReportBlendMode(composite);
            int count = carriesMask ? SaveLayer(_softMask, 255, composite) : -1;
            _groupPaint.Alpha = 255;
            SetBlendMode(_groupPaint, ref _groupBlendMode, carriesMask ? BlendMode.Normal : composite);
            int inner = canvas.Save();
            ConcatFromPixels();
            canvas.DrawBitmap(image.Bitmap, area.Left, area.Top, _groupPaint);
            canvas.RestoreToCount(inner);
            if (carriesMask)
            {
                ApplyMask(_softMask!, count);
            }
        }

        /// <summary>
        /// Returns the bitmap of a soft mask's values over the clip (§11.6.5.1), from the cache when the same mask was rendered at the
        /// same placement and clip; its <see cref="MaskImage.Image"/> is null when the mask hides everything there. Returns null when
        /// the area is too large to rasterize, or the canvas cannot place pixels (the paints are then unmasked).
        /// </summary>
        private MaskImage? GetMask(in DisplaySoftMask mask)
        {
            Report(Reported.SoftMaskRasterized, SoftMaskRasterized);
            if (!_baseInvertible)
            {
                return null;
            }

            Backend root = Root;
            Matrix toPixels = mask.Matrix * _device * _pixels;
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
                backdrop = 0xFF000000u | ((uint)MaskChannel(rgb[0]) << 16) | ((uint)MaskChannel(rgb[1]) << 8) | MaskChannel(rgb[2]);
            }

            PixelRect area = clip;
            if (transfer[luminosity ? Luminosity(backdrop) : 0] == 0 && TryGetBounds(mask.HasBoundingBox, mask.BoundingBox, toPixels, out PixelRect bounds))
            {
                // Outside the group's bounding box the value is the constant 0 (§11.6.5.1): only the box can show a masked paint.
                area = Intersect(area, bounds);
            }

            if ((long)area.Width * area.Height > MaxRasterPixels)
            {
                Report(Reported.GroupApproximated, GroupApproximated);
                return null;
            }

#pragma warning disable CA2000 // Kept by the mask cache, which disposes it on eviction.
            PixelImage? image = area.Width <= 0 || area.Height <= 0 ? null : RenderMask(mask, toPixels, area, backdrop, luminosity, transfer);
#pragma warning restore CA2000
            var created = new MaskImage(mask.Content, toPixels, clip, area, image) { Stamp = ++root._maskClock };
            root.AddMask(created, this);
            return created;
        }

        /// <summary>Renders the mask group with the software rasterizer over its backdrop and turns each pixel into its mask value.</summary>
        private PixelImage RenderMask(in DisplaySoftMask mask, in Matrix toPixels, PixelRect area, uint backdrop, bool luminosity, byte[] transfer)
        {
            int width = area.Width;
            int height = area.Height;
            using var bitmap = new Bitmap(width, height);
            if (backdrop != 0)
            {
                MemoryMarshal.Cast<byte, uint>(bitmap.Pixels).Fill(backdrop);
            }

            SoftwareRasterizer rasterizer = Root._rasterizer ??= new SoftwareRasterizer();
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

        /// <summary>Keeps a mask bitmap, evicting the least recently used one that no state, saved state or open group goes through.</summary>
        private void AddMask(MaskImage created, Backend requester)
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

            masks[victim]?.Image?.Dispose();
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
                if (ReferenceEquals(_stack[index].Mask, entry))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The whole device pixels of the clip, intersected with a bounding box mapped to the device pixels when there is one.</summary>
        private PixelRect GetArea(bool hasBox, in PdfRectangle box, in Matrix toPixels)
        {
            if (!_canvas!.GetClipBounds(_groupClip))
            {
                return default;
            }

            PathPoint a = _pixels.Transform(_groupClip.Left, _groupClip.Top);
            PathPoint b = _pixels.Transform(_groupClip.Right, _groupClip.Top);
            PathPoint c = _pixels.Transform(_groupClip.Right, _groupClip.Bottom);
            PathPoint d = _pixels.Transform(_groupClip.Left, _groupClip.Bottom);
            var area = new PixelRect(
                ToPixel(Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)))),
                ToPixel(Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)))),
                ToPixel(Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)))),
                ToPixel(Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)))));
            return TryGetBounds(hasBox, box, toPixels, out PixelRect bounds) ? Intersect(area, bounds) : area;
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

        private static byte MaskChannel(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);

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
    }

    /// <summary>A soft mask's values over an area of the device pixels, keyed by the mask group, its placement and the clip.</summary>
    private sealed class MaskImage(DisplayList content, Matrix toPixels, PixelRect clip, PixelRect bounds, PixelImage? image)
    {
        public DisplayList Content { get; } = content;

        public Matrix ToPixels { get; } = toPixels;

        public PixelRect Clip { get; } = clip;

        public PixelRect Bounds { get; } = bounds;

        /// <summary>Gets the values (an alpha bitmap), or null when the mask hides the whole clip.</summary>
        public PixelImage? Image { get; } = image;

        public long Stamp { get; set; }

        public int Pins { get; set; }
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

        public void FillPath(PathView path, FillRule rule, in DisplayColor color) => Note(path: true, 1);

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color) => Note(path: true, 1);

        public void IntersectClip(PathView path, FillRule rule)
        {
        }

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) => Note(path: false, 1);

        public void DrawGlyphs(in GlyphRun run) => Note(path: true, (run.Fills ? 1 : 0) + (run.Strokes ? 1 : 0));

        public void DrawShading(in DisplayShading shading) => Note(path: false, 1);

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading) => Note(path: false, 1);

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading) => Note(path: false, 1);

        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern) => Note(path: false, 1);

        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern) => Note(path: false, 1);

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color) => Note(path: false, 1);

        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode) => _blend = blendMode;

        public void SetSoftMask(in DisplaySoftMask mask) => _changesMask = true;

        public void DrawGroup(in DisplayGroup group)
        {
            _hasGroups = true;
            _uniform = false;
            Note(path: false, 2);
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

        private void Note(bool path, int count)
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
