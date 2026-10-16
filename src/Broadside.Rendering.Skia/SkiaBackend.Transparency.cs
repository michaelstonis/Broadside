using System.Buffers;
using System.Runtime.InteropServices;
using Broadside.Diagnostics;
using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>
    /// Transparency groups and soft masks (ISO 32000-2 §11.4, §11.5, §11.6.5, §11.6.6) on the Skia canvas: groups Skia can composite
    /// are layers, the others are drawn by the software rasterizer into an image composited in their place; soft-mask values are
    /// computed by the software rasterizer and applied to each masked paint, or to a group's result, through a layer and
    /// <see cref="SKBlendMode.DstIn"/>.
    /// </summary>
    private sealed partial class Replayer
    {
        /// <summary>The most pixels a group or soft mask is rasterized over (8192 × 8192).</summary>
        private const long MaxRasterPixels = 1L << 26;

        /// <summary>The soft-mask images kept across renders.</summary>
        private const int MaskCacheSize = 8;

        /// <summary>Mask values and rasterized groups are drawn pixel for pixel.</summary>
        private static readonly SKSamplingOptions Pixels = new(SKFilterMode.Nearest, SKMipmapMode.None);

        private static readonly Diagnostic KnockoutRasterized = new(
            "BackendKnockoutGroupRasterized",
            DiagnosticSeverity.Information,
            "A knockout transparency group (ISO 32000-2 §11.4.6) that Skia cannot composite was drawn by the software rasterizer and composited as an image.");

        private static readonly Diagnostic ColorSpaceRasterized = new(
            "BackendGroupColorSpaceRasterized",
            DiagnosticSeverity.Information,
            "A transparency group that blends in a gray or CMYK group colour space (ISO 32000-2 §11.4.7, §11.3.4) was drawn by the software rasterizer and composited as an image.");

        private static readonly Diagnostic NonIsolatedApproximated = new(
            "BackendNonIsolatedGroupApproximated",
            DiagnosticSeverity.Warning,
            "A non-isolated transparency group whose content blends (ISO 32000-2 §11.4.5, §11.4.8) and whose result composites with a blend mode or a soft mask was drawn by the software rasterizer against a transparent backdrop, as if isolated.");

        private static readonly Diagnostic SoftMaskRasterized = new(
            "BackendSoftMaskRasterized",
            DiagnosticSeverity.Information,
            "Soft-mask values (ISO 32000-2 §11.5, §11.6.5.1) were computed by the software rasterizer and applied by Skia.");

        private static readonly Diagnostic GroupApproximated = new(
            "BackendTransparencyGroupApproximated",
            DiagnosticSeverity.Warning,
            "A transparency group or soft mask (ISO 32000-2 §11.4, §11.6.5.1) covered more than 2^26 device pixels, too many to rasterize; it was drawn as its content alone, unmasked.");

        private readonly SKPaint _groupPaint = new() { IsAntialias = true };
        private readonly SKPaint _maskLayer = new() { IsAntialias = true };
        private readonly SKPaint _dstIn = new() { BlendMode = SKBlendMode.DstIn };
        private readonly SKPaint _lerp = new();
        private SKBlender?[]? _lerps;
        private GroupInspector? _inspector;
        private SoftwareRasterizer? _rasterizer;
        private MaskImage?[]? _masks;
        private byte[]? _transfer;
        private long _maskClock;

        // The soft mask of the graphics state, saved and restored with it; whether a masked paint's layer is open; whether paints
        // replace (knock out) what the current knockout layer holds.
        private MaskImage? _softMask;
        private bool _masking;
        private bool _knockout;
        private int _maskSave;

        /// <summary>The blend mode a paint is drawn with: Src in a knockout layer, SrcOver in a masked paint's layer (the layer blends), else the state's.</summary>
        private SKBlendMode PaintBlend => _knockout ? SKBlendMode.Src : _masking ? SKBlendMode.SrcOver : _compositing.BlendMode;

        private Replayer Root => _owner ?? this;

        /// <summary>
        /// Sets the soft mask of the paints that follow (§11.6.5.1): its values are rendered once per mask, placement and clip by the
        /// software rasterizer (§11.5.2 alpha, §11.5.3 luminosity 0.30 R + 0.59 G + 0.11 B, then the transfer function) and each
        /// masked paint is drawn into a layer that <see cref="SKBlendMode.DstIn"/> with the values masks and the paint's blend mode
        /// composites. <see cref="DisplaySoftMask.IsNone"/> removes it.
        /// </summary>
        public void SetSoftMask(in DisplaySoftMask mask) => _softMask = mask.IsNone ? null : GetMask(mask);

        /// <summary>
        /// Draws a transparency group (§11.4): content that composites as if drawn directly (§11.4.4 NOTE 5) is replayed; isolated
        /// groups, and non-isolated ones whose content does not blend, are layers composited with the group's opacity, blend mode and
        /// soft mask; a non-isolated group of blending content at an opacity below 1 is a layer started from the backdrop and
        /// interpolated towards it (§11.4.8); the implicit knockout group of a fill and stroke (§11.7.4.4) is a layer its paints
        /// replace into. Every other group, knockout or blending in a gray or CMYK colour space, is drawn by the software rasterizer.
        /// </summary>
        public void DrawGroup(in DisplayGroup group)
        {
            GroupContent content = (_inspector ??= new GroupInspector()).Inspect(group, _compositing.BlendMode);
            bool masked = !group.SoftMask.IsNone;
            if (group.IsKnockout && content.Paints > 1)
            {
                if (group.Kind == DisplayGroupKind.FillAndStroke && content.PathsOnly && content.Uniform && !content.ChangesMask)
                {
                    DrawKnockoutLayer(group, content.Blend);
                    return;
                }

                Report(32, KnockoutRasterized);
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
                Report(1024, ColorSpaceRasterized);
                Rasterize(group, content);
                return;
            }

            if (!group.IsIsolated && content.Blends)
            {
                if (group.BlendMode == BlendMode.Normal && !masked)
                {
                    DrawNonIsolatedLayer(group);
                    return;
                }

                Rasterize(group, content);
                return;
            }

            DrawIsolatedLayer(group);
        }

        // The paints: each one under a soft mask is drawn into a layer that the mask's values mask (§11.6.4.3, §11.6.5.1).
        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            switch (BeginMasked())
            {
                case Mask.Hidden:
                    return;
                case Mask.None:
                    FillPathCore(path, rule, color);
                    return;
            }

            FillPathCore(path, rule, color);
            EndMasked();
        }

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
            switch (BeginMasked())
            {
                case Mask.Hidden:
                    return;
                case Mask.None:
                    StrokePathCore(path, style, color);
                    return;
            }

            StrokePathCore(path, style, color);
            EndMasked();
        }

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion)
        {
            switch (BeginMasked())
            {
                case Mask.Hidden:
                    return;
                case Mask.None:
                    DrawImageCore(image, defaultColorSpaces, conversion);
                    return;
            }

            DrawImageCore(image, defaultColorSpaces, conversion);
            EndMasked();
        }

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
        {
            switch (BeginMasked())
            {
                case Mask.Hidden:
                    return;
                case Mask.None:
                    FillImageMaskCore(mask, color);
                    return;
            }

            FillImageMaskCore(mask, color);
            EndMasked();
        }

        public void DrawGlyphs(in GlyphRun run)
        {
            switch (run.Fills || run.Strokes ? BeginMasked() : Mask.None)
            {
                case Mask.Hidden:
                    return;
                case Mask.None:
                    DrawGlyphsCore(run);
                    return;
            }

            DrawGlyphsCore(run);
            EndMasked();
        }

        public void DrawShading(in DisplayShading shading)
        {
            switch (BeginMasked())
            {
                case Mask.Hidden:
                    return;
                case Mask.None:
                    DrawShadingCore(shading);
                    return;
            }

            DrawShadingCore(shading);
            EndMasked();
        }

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
        {
            switch (BeginMasked())
            {
                case Mask.Hidden:
                    return;
                case Mask.None:
                    FillPathWithShadingCore(path, rule, shading);
                    return;
            }

            FillPathWithShadingCore(path, rule, shading);
            EndMasked();
        }

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
            switch (BeginMasked())
            {
                case Mask.Hidden:
                    return;
                case Mask.None:
                    StrokePathWithShadingCore(path, style, shading);
                    return;
            }

            StrokePathWithShadingCore(path, style, shading);
            EndMasked();
        }

        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
        {
            switch (BeginMasked())
            {
                case Mask.Hidden:
                    return;
                case Mask.None:
                    FillPathWithTilingPatternCore(path, rule, pattern);
                    return;
            }

            FillPathWithTilingPatternCore(path, rule, pattern);
            EndMasked();
        }

        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
        {
            switch (BeginMasked())
            {
                case Mask.Hidden:
                    return;
                case Mask.None:
                    StrokePathWithTilingPatternCore(path, style, pattern);
                    return;
            }

            StrokePathWithTilingPatternCore(path, style, pattern);
            EndMasked();
        }

        private void BeginTransparency()
        {
            _softMask = null;
            _masking = false;
            _knockout = false;
        }

        private void DisposeTransparency()
        {
            _groupPaint.Dispose();
            _maskLayer.Dispose();
            _dstIn.Dispose();
            _lerp.Dispose();
            _rasterizer?.Dispose();
            if (_lerps is not null)
            {
                foreach (SKBlender? blender in _lerps)
                {
                    blender?.Dispose();
                }
            }

            if (_masks is not null)
            {
                foreach (MaskImage? mask in _masks)
                {
                    mask?.Image?.Dispose();
                }

                _masks = null;
            }
        }

        /// <summary>Opens the layer of a paint under the state's soft mask, or says there is no mask or that the mask hides everything.</summary>
        private Mask BeginMasked()
        {
            if (_softMask is null || _masking)
            {
                return Mask.None;
            }

            if (_softMask.Image is null)
            {
                return Mask.Hidden;
            }

            OpenMaskLayer(_compositing.BlendMode);
            return Mask.Open;
        }

        /// <summary>Opens a layer over the mask's area that <see cref="EndMasked"/> masks and composites with <paramref name="blend"/>.</summary>
        private void OpenMaskLayer(SKBlendMode blend)
        {
            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            _maskLayer.BlendMode = blend;
            _maskSave = canvas.SaveLayer(_softMask!.Bounds, _maskLayer);
            _masking = true;
        }

        /// <summary>Masks the paint's layer with the values (<see cref="SKBlendMode.DstIn"/>) and composites it.</summary>
        private void EndMasked()
        {
            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            canvas.DrawImage(_softMask!.Image, _softMask.Bounds.Left, _softMask.Bounds.Top, Pixels, _dstIn);
            canvas.RestoreToCount(_maskSave);
            _masking = false;
        }

        /// <summary>
        /// An isolated group (§11.4.5), or a non-isolated one whose content does not blend (the same result): a transparent layer the
        /// content draws into, masked by the group's soft mask (§11.6.4.3 NOTE 2) and composited with its opacity and blend mode.
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
            }

            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            _groupPaint.Color = SKColors.Black.WithAlpha(Alpha(group.Opacity));
            _groupPaint.BlendMode = NativeBlendMode(group.BlendMode);
            int count = TryGetBounds(group.HasBoundingBox, group.BoundingBox, group.Matrix * _device, out SKRect bounds)
                ? canvas.SaveLayer(bounds, _groupPaint)
                : canvas.SaveLayer(_groupPaint);
            if (mask is not null)
            {
                mask.Pins++;
            }

            ReplayInLayer(group, knockout: false);
            if (mask is not null)
            {
                mask.Pins--;
                canvas.SetMatrix(in _base);
                canvas.DrawImage(mask.Image, mask.Bounds.Left, mask.Bounds.Top, Pixels, _dstIn);
            }

            canvas.RestoreToCount(count);
        }

        /// <summary>
        /// A non-isolated group of blending content at opacity α in the Normal blend mode (§11.4.8): the content composites over a copy
        /// of the backdrop, and the layer's result r interpolates the backdrop b towards it, α r + (1 − α) b, which is the group
        /// compositing formula with the backdrop's contribution removed, for any backdrop alpha.
        /// </summary>
        private void DrawNonIsolatedLayer(in DisplayGroup group)
        {
            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            byte alpha = Alpha(group.Opacity);
            _lerps ??= new SKBlender?[256];
            _lerp.Blender = _lerps[alpha] ??= SKBlender.CreateArithmetic(0, alpha / 255f, 1 - (alpha / 255f), 0, true);
            var record = new SKCanvasSaveLayerRec
            {
                Paint = _lerp,
                Flags = SKCanvasSaveLayerRecFlags.InitializeWithPrevious,
                Bounds = TryGetBounds(group.HasBoundingBox, group.BoundingBox, group.Matrix * _device, out SKRect bounds) ? bounds : null,
            };
            int count = canvas.SaveLayer(record);
            ReplayInLayer(group, knockout: false);
            canvas.RestoreToCount(count);
        }

        /// <summary>
        /// The implicit knockout group of a fill and stroke (§11.7.4.4) whose paints share one blend mode: a transparent layer the
        /// paints replace into by their coverage (Skia's Src), composited onto the backdrop with that blend mode. For the Normal mode
        /// this is the knockout result exactly; for the others it differs only where the stroke's edge partly covers the fill.
        /// </summary>
        private void DrawKnockoutLayer(in DisplayGroup group, SKBlendMode blend)
        {
            bool carriesMask = _softMask is not null && !_masking;
            if (carriesMask && _softMask!.Image is null)
            {
                return;
            }

            if (carriesMask)
            {
                OpenMaskLayer(blend);
            }

            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            _groupPaint.Color = SKColors.Black;
            _groupPaint.BlendMode = carriesMask ? SKBlendMode.SrcOver : blend;
            int count = TryGetBounds(group.HasBoundingBox, group.BoundingBox, group.Matrix * _device, out SKRect bounds)
                ? canvas.SaveLayer(bounds, _groupPaint)
                : canvas.SaveLayer(_groupPaint);
            bool masking = _masking;
            bool knockout = _knockout;
            _knockout = true;
            try
            {
                group.Replay(this);
            }
            finally
            {
                _knockout = knockout;
                _masking = masking;
            }

            canvas.RestoreToCount(count);
            if (carriesMask)
            {
                EndMasked();
            }
        }

        /// <summary>Replays a group's content into its layer, where the paints carry no knockout of an outer layer and no open mask layer.</summary>
        private void ReplayInLayer(in DisplayGroup group, bool knockout)
        {
            bool masking = _masking;
            bool outer = _knockout;
            _masking = false;
            _knockout = knockout;
            try
            {
                group.Replay(this);
            }
            finally
            {
                _masking = masking;
                _knockout = outer;
            }
        }

        /// <summary>
        /// Draws a group with the software rasterizer (§11.4) into an image over the group's area within the clip, from the transform
        /// and compositing state the content starts with, and composites the image here: with the group's blend mode, or, for a
        /// non-isolated group in the Normal mode whose paints share one blend mode, with that mode (its paints blend with the backdrop).
        /// A non-isolated group whose content blends otherwise is reported: it was drawn against a transparent backdrop.
        /// </summary>
        private void Rasterize(in DisplayGroup group, in GroupContent content)
        {
            SKBlendMode composite = NativeBlendMode(group.BlendMode);
            if (!group.IsIsolated && content.Blends)
            {
                if (group.BlendMode == BlendMode.Normal && content.Uniform && !content.HasGroups)
                {
                    composite = content.Blend;
                }
                else
                {
                    Report(2048, NonIsolatedApproximated);
                }
            }

            SKRectI area = GetArea(group.HasBoundingBox, group.BoundingBox, group.Matrix * _device);
            if (area.IsEmpty)
            {
                return;
            }

            if ((long)area.Width * area.Height > MaxRasterPixels)
            {
                Report(4096, GroupApproximated);
                group.Replay(this);
                return;
            }

            bool carriesMask = group.Kind == DisplayGroupKind.FillAndStroke && _softMask is not null && !_masking;
            if (carriesMask && _softMask!.Image is null)
            {
                return;
            }

            Replayer root = Root;
            SoftwareRasterizer rasterizer = root._rasterizer ??= new SoftwareRasterizer();
            using var bitmap = new Bitmap(area.Width, area.Height);
            rasterizer.Render(
                group,
                _document!,
                bitmap,
                _device * Matrix.CreateTranslation(-area.Left, -area.Top),
                _ctm,
                _compositing.StrokeAlpha / 255.0,
                _compositing.FillAlpha / 255.0,
                _compositing.Mode);
            using SKImage image = SKImage.FromPixelCopy(new SKImageInfo(area.Width, area.Height, SKColorType.Bgra8888, SKAlphaType.Premul), bitmap.Pixels, bitmap.Stride);
            if (carriesMask)
            {
                OpenMaskLayer(composite);
            }

            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            _groupPaint.Color = SKColors.Black;
            _groupPaint.BlendMode = carriesMask ? SKBlendMode.SrcOver : composite;
            canvas.DrawImage(image, area.Left, area.Top, Pixels, _groupPaint);
            if (carriesMask)
            {
                EndMasked();
            }
        }

        /// <summary>
        /// Returns the image of a soft mask's values over the clip (§11.6.5.1), from the cache when the same mask was rendered at the
        /// same placement and clip; its <see cref="MaskImage.Image"/> is null when the mask hides everything there. Returns null when the
        /// area is too large to rasterize (the paints are then unmasked, reported).
        /// </summary>
        private MaskImage? GetMask(in DisplaySoftMask mask)
        {
            Report(64, SoftMaskRasterized);
            Replayer root = Root;
            Matrix toDevice = mask.Matrix * _device;
            SKRectI clip = GetArea(false, default, toDevice);
            MaskImage?[] masks = root._masks ??= new MaskImage?[MaskCacheSize];
            foreach (MaskImage? entry in masks)
            {
                if (entry is not null && ReferenceEquals(entry.Content, mask.Content) && entry.ToDevice == toDevice && entry.Clip == clip)
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

            SKRectI area = clip;
            if (transfer[luminosity ? Luminosity(backdrop) : 0] == 0 && TryGetBounds(mask.HasBoundingBox, mask.BoundingBox, toDevice, out SKRect bounds))
            {
                // Outside the group's bounding box the value is the constant 0 (§11.6.5.1): only the box can show a masked paint.
                area = SKRectI.Intersect(area, SKRectI.Ceiling(bounds));
            }

            if ((long)area.Width * area.Height > MaxRasterPixels)
            {
                Report(4096, GroupApproximated);
                return null;
            }

            SKImage? image = area.IsEmpty ? null : RenderMask(mask, toDevice, area, backdrop, luminosity, transfer);
            var created = new MaskImage(mask.Content, toDevice, clip, area, image) { Stamp = ++root._maskClock };
            root.AddMask(created, this);
            return created;
        }

        /// <summary>Renders the mask group with the software rasterizer over its backdrop and turns each pixel into its mask value.</summary>
        private SKImage RenderMask(in DisplaySoftMask mask, in Matrix toDevice, SKRectI area, uint backdrop, bool luminosity, byte[] transfer)
        {
            int width = area.Width;
            int height = area.Height;
            using var bitmap = new Bitmap(width, height);
            if (backdrop != 0)
            {
                MemoryMarshal.Cast<byte, uint>(bitmap.Pixels).Fill(backdrop);
            }

            SoftwareRasterizer rasterizer = Root._rasterizer ??= new SoftwareRasterizer();
            rasterizer.Render(mask.Content, bitmap, toDevice * Matrix.CreateTranslation(-area.Left, -area.Top));
            byte[] values = ArrayPool<byte>.Shared.Rent(width * height);
            try
            {
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

                return SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul), values.AsSpan(0, width * height), width);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(values);
            }
        }

        /// <summary>Keeps a mask image, evicting the least recently used one that no state, saved state or open group goes through.</summary>
        private void AddMask(MaskImage created, Replayer requester)
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

        /// <summary>The whole device pixels of the clip, intersected with a bounding box mapped to the device when there is one.</summary>
        private SKRectI GetArea(bool hasBox, in PdfRectangle box, in Matrix toDevice)
        {
            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            SKRectI area = SKRectI.Ceiling(canvas.LocalClipBounds, outwards: true);
            return TryGetBounds(hasBox, box, toDevice, out SKRect bounds) ? SKRectI.Intersect(area, SKRectI.Ceiling(bounds, outwards: true)) : area;
        }

        /// <summary>The device bounds of a bounding box, one pixel wider on each side for anti-aliased edges; false when there is no usable box.</summary>
        private static bool TryGetBounds(bool hasBox, in PdfRectangle box, in Matrix toDevice, out SKRect bounds)
        {
            bounds = default;
            if (!hasBox)
            {
                return false;
            }

            PathPoint a = toDevice.Transform(new PathPoint(box.Left, box.Bottom));
            PathPoint b = toDevice.Transform(new PathPoint(box.Right, box.Bottom));
            PathPoint c = toDevice.Transform(new PathPoint(box.Right, box.Top));
            PathPoint d = toDevice.Transform(new PathPoint(box.Left, box.Top));
            double left = Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X))) - 1;
            double right = Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X))) + 1;
            double top = Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y))) - 1;
            double bottom = Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))) + 1;
            if (!double.IsFinite(left + right + top + bottom))
            {
                return false;
            }

            bounds = new SKRect(Clamp(left), Clamp(top), Clamp(right), Clamp(bottom));
            return true;
        }

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

        /// <summary>What a masked paint does: draw unmasked, draw nothing (the mask hides the clip), or draw into the open mask layer.</summary>
        private enum Mask
        {
            None,
            Hidden,
            Open,
        }

        /// <summary>A soft mask's values over an area of the device (alpha only), keyed by the mask group, its placement and the clip.</summary>
        private sealed class MaskImage(DisplayList content, Matrix toDevice, SKRectI clip, SKRectI bounds, SKImage? image)
        {
            public DisplayList Content { get; } = content;

            public Matrix ToDevice { get; } = toDevice;

            public SKRectI Clip { get; } = clip;

            public SKRect Bounds { get; } = bounds;

            /// <summary>Gets the values, or null when the mask hides the whole clip.</summary>
            public SKImage? Image { get; } = image;

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
        private readonly record struct GroupContent(int Paints, bool Blends, bool Uniform, SKBlendMode Blend, bool PathsOnly, bool HasGroups, bool ChangesMask);

        /// <summary>A backend that paints nothing and notes what a group's content paints and with which blend modes.</summary>
        private sealed class GroupInspector : IRenderingBackend
        {
            private SKBlendMode[] _saved = new SKBlendMode[16];
            private int _depth;
            private SKBlendMode _blend;
            private int _paints;
            private bool _blends;
            private bool _uniform;
            private SKBlendMode _first;
            private bool _pathsOnly;
            private bool _hasGroups;
            private bool _changesMask;

            public GroupContent Inspect(in DisplayGroup group, SKBlendMode held)
            {
                (_depth, _blend, _paints, _blends, _uniform, _first, _pathsOnly, _hasGroups, _changesMask) = (0, held, 0, false, true, SKBlendMode.SrcOver, true, false, false);
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

            public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode) => _blend = NativeBlendMode(blendMode);

            public void SetSoftMask(in DisplaySoftMask mask) => _changesMask = true;

            public void DrawGroup(in DisplayGroup group)
            {
                _hasGroups = true;
                _uniform = false;
                Paint(path: false, 2);
                _blends |= group.BlendMode != BlendMode.Normal;
                SKBlendMode blend = _blend;
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
                _blends |= _blend != SKBlendMode.SrcOver;
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
}
