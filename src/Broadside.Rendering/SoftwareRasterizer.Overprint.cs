using System.Runtime.InteropServices;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>
/// Overprint preview (ISO 32000-2 §8.6.7, §11.7.4; <see cref="RenderOptions.OverprintPreview"/>): the page content painted into
/// device colourant planes with the overprint parameters deciding which colourants each paint replaces, converted for display at
/// the end.
/// </summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's overprint half.</summary>
    private sealed partial class Backend
    {
        private static readonly CosName[] ProcessNames = [new("Cyan"), new("Magenta"), new("Yellow"), new("Black")];
        private static readonly CosName NoneName = new("None");

        private readonly ColorantPlanes _planes = new();
        private readonly Ink _fillInk = new();
        private readonly Ink _strokeInk = new();
        private readonly Dictionary<PdfColorSpace, int[]> _deviceNPlanes = new(ReferenceEqualityComparer.Instance);

        // The overprint parameters of the paints that follow (§8.6.7), saved and restored with the compositors.
        private DisplayDeviceParameters _deviceParameters;

        private bool _previewing;

        private Backend? _groupPreviewBackend;
        private Bitmap? _groupPreviewBitmap;
        private byte[] _groupPreviewPixels = [];

        /// <summary>The scratch layers' pixel models (#114's <see cref="PixelModel"/>): 1 − C, M, Y as RGB, and 1 − K in all three channels.</summary>
        private static readonly PixelModel CmyPass = new(DeviceColorModel.Cmyk, 0);

        private static readonly PixelModel BlackPass = new(DeviceColorModel.Cmyk, 1);
        private PdfOutputIntent? _previewOutputIntent;

        // The converter of the last colour to DeviceCMYK, for the process colourants of a paint.
        private PdfColorSpace? _inkSpace;
        private PdfDefaultColorSpaces? _inkDefaults;
        private ColorConversion _inkConversion;
        private PdfColorConverter? _inkConverter;

        // Images and shadings with varying colour are painted twice into page-sized layers, as 1 − C, 1 − M, 1 − Y and as 1 − K,
        // through the ordinary painters, and the layers merged into the planes.
        private Bitmap? _cmyLayer;
        private Bitmap? _blackLayer;
        private ImageSource? _cmyImage;
        private ImageSource? _blackImage;
        private ShadingSource? _cmyShading;
        private ShadingSource? _blackShading;
        private PdfColorSpace? _previewImageSpace;
        private PdfDefaultColorSpaces? _previewImageDefaults;
        private ColorConversion _previewImageConversion;
        private PdfColorConverter? _previewImageConverter;
        private PdfColorConverter? _previewImageBaseConverter;

        /// <summary>Takes the overprint parameters and halftone of the paints that follow (§8.6.7, §10.6).</summary>
        /// <remarks>
        /// The rasterizer is a composite, continuous-tone RGB device: by default it does not overprint, and §8.6.7 says such a device
        /// ignores the overprint parameter, as §10.6.1 says it needs no halftoning (owner decision 5 of the Phase 3 plan). With
        /// <see cref="RenderOptions.OverprintPreview"/> the overprint parameters decide which colourants each paint replaces.
        /// Transfer functions are applied either way: they come with each colour's conversion (§10.5).
        /// </remarks>
        public void SetDeviceParameters(in DisplayDeviceParameters parameters) => _deviceParameters = parameters;

        /// <summary>Converts the colourant planes for display into the target and paints in RGB from then on.</summary>
        /// <remarks>Called after the content, before the first annotation (annotations and form fields are drawn over the preview).</remarks>
        public void FinishPreview()
        {
            if (!_previewing)
            {
                return;
            }

            _previewing = false;
            PdfColorConverter converter = _document!.GetColorConverter(
                PdfDeviceCmykColorSpace.Instance, new ColorConversion { OutputIntent = _previewOutputIntent }, defaults: null);
            _planes.Finish(_target!, converter);
            _previewOutputIntent = null;
        }

        private void BeginPreview(bool preview)
        {
            _previewing = preview;
            _previewOutputIntent = null;
            if (preview)
            {
                _planes.Begin(_width, _height);
            }
        }

        private void EndPreview()
        {
            _previewing = false;
            _previewOutputIntent = null;
            _planes.Forget();
            _deviceNPlanes.Clear();
            _inkSpace = null;
            _inkDefaults = null;
            _inkConverter = null;
            _previewImageSpace = null;
            _previewImageDefaults = null;
            _previewImageConverter = null;
            _previewImageBaseConverter = null;
            _cmyImage?.Forget();
            _blackImage?.Forget();
            _cmyShading?.Forget();
            _blackShading?.Forget();
        }

        private void DisposePreview()
        {
            _planes.Dispose();
            _cmyLayer?.Dispose();
            _blackLayer?.Dispose();
            _cmyImage?.Dispose();
            _blackImage?.Dispose();
            _groupPreviewBackend?.Dispose();
            _groupPreviewBitmap?.Dispose();
            Pooled.Return(ref _groupPreviewPixels);
        }

        /// <summary>
        /// Routes a transparency group (#114) that needs its own layer into the preview: the group is rendered in RGB, with its
        /// isolation, knockout, opacity and blend mode, by a child backend over a clear surface, and the result is converted to process
        /// colourants and merged as one process paint through the clip, as a tiling cell's is.
        /// </summary>
        /// <remarks>
        /// Approximations of §11.7.4.5 under the preview: the group's content is composited without the planes as its backdrop, so a
        /// non-isolated group's blend modes and a group's own blend mode see a clear backdrop; spot colours inside the group reach the
        /// planes as their process equivalents; the group's result knocks out (no overprint).
        /// </remarks>
        private void PreviewGroup(in DisplayGroup group, int left, int top, int right, int bottom)
        {
            var box = new PixelBox(left, top, right, bottom);
            int length = box.Width * box.Height * 4;
            Pooled.Ensure(ref _groupPreviewPixels, length);
            Memory<byte> pixels = _groupPreviewPixels.AsMemory(0, length);
            pixels.Span.Clear();
            if (_groupPreviewBitmap is null)
            {
                _groupPreviewBitmap = new Bitmap(pixels, box.Width, box.Height, box.Width * 4);
            }
            else
            {
                _groupPreviewBitmap.Attach(pixels, box.Width, box.Height, box.Width * 4);
            }

            Backend child = _groupPreviewBackend ??= new Backend();
            child.Begin(_document!, _groupPreviewBitmap, _device * Matrix.CreateTranslation(-left, -top), box.Width, box.Height, preview: false, _antiAliasing);
            try
            {
                child.SetTransform(_ctm);
                child.DrawGroup(group);
            }
            finally
            {
                child.End();
            }

            _previewOutputIntent ??= group.Conversion.OutputIntent;
            _scan.Reset(_width, _height);
            AddDeviceLine(new PathPoint(left, top), new PathPoint(right, top));
            AddDeviceLine(new PathPoint(right, top), new PathPoint(right, bottom));
            AddDeviceLine(new PathPoint(right, bottom), new PathPoint(left, bottom));
            AddDeviceLine(new PathPoint(left, bottom), new PathPoint(left, top));

            // The group's own mask applied in the child; the mask the paints outside the group go through does not apply to it.
            _clip.SetSoftMask(null, false);
            PreviewTiling(MemoryMarshal.Cast<byte, uint>(pixels.Span), box, FillRule.NonZero, new Compositor(1, BlendMode.Normal), overprint: false);
            _clip.SetSoftMask(_softMask, _softMaskShape);
        }

        /// <summary>Paints the scan converter's shape in one colour: into the planes with <paramref name="ink"/> when previewing, else into the target.</summary>
        private void PaintSolid(FillRule rule, uint pixel, Compositor compositor, Ink ink)
        {
            if (!_previewing)
            {
                _scan.Fill(_target!, rule, pixel, compositor);
                return;
            }

            var sink = new InkSink(_planes, ink, compositor);
            _scan.Paint(rule, ref sink);
        }

        /// <summary>
        /// Works out which colourants a paint in <paramref name="color"/> marks and the tint it gives each (§8.6.7, §11.7.4.5 Table 146).
        /// </summary>
        /// <param name="color">The paint's colour.</param>
        /// <param name="overprint">Whether the paint overprints (<c>OP</c> for strokes, <c>op</c> for the rest).</param>
        /// <param name="ink">Receives the colourants.</param>
        /// <remarks>
        /// A Separation colour marks its colourant (<c>All</c>: every colourant), a DeviceN colour each colourant it names except
        /// <c>None</c>; a process colourant name (Cyan, Magenta, Yellow, Black) is the process plane, any other a spot plane. Any other
        /// space converts to DeviceCMYK and marks the four process colourants, except that with overprint mode 1 a DeviceCMYK
        /// component of 0, as written in the file, marks nothing (§8.6.7). Without overprint every other colourant is erased.
        /// </remarks>
        private void PrepareInk(in DisplayColor color, bool overprint, Ink ink)
        {
            _previewOutputIntent ??= color.Conversion.OutputIntent;
            PdfColorSpace space = color.ColorSpace;
            ReadOnlySpan<float> components = color.Components;
            switch (space)
            {
                case PdfSeparationColorSpace { IsAll: true }:
                    ink.Reset(_planes.Count, mark: true, Tint(components, 0));
                    return;
                case PdfSeparationColorSpace separation:
                    {
                        int plane = PlaneOf(separation.ColorantName, color, 0, 1);
                        ink.Reset(_planes.Count, mark: !overprint);
                        ink.Set(plane, Tint(components, 0));
                        return;
                    }

                case PdfDeviceNColorSpace deviceN:
                    {
                        int[] planes = DeviceNPlanes(deviceN, color);
                        ink.Reset(_planes.Count, mark: !overprint);
                        for (int index = 0; index < planes.Length; index++)
                        {
                            if (planes[index] >= 0)
                            {
                                ink.Set(planes[index], Tint(components, index));
                            }
                        }

                        return;
                    }
            }

            Span<float> cmyk = stackalloc float[ColorantPlanes.ProcessCount];
            InkConverter(color).Convert(components, cmyk);
            bool nonZero = overprint && _deviceParameters.OverprintMode == 1 && space is PdfDeviceCmykColorSpace;
            ink.Reset(_planes.Count, mark: !overprint);
            for (int c = 0; c < ColorantPlanes.ProcessCount; c++)
            {
                if (!nonZero || Tint(components, c) != 0)
                {
                    ink.Set(c, cmyk[c]);
                }
            }
        }

        private static float Tint(ReadOnlySpan<float> components, int index) => index < components.Length ? components[index] : 0;

        /// <summary>Returns the plane of a colourant named by a Separation or DeviceN space, adding a spot plane the first time.</summary>
        private int PlaneOf(CosName name, in DisplayColor color, int component, int components)
        {
            for (int c = 0; c < ProcessNames.Length; c++)
            {
                if (name.Equals(ProcessNames[c]))
                {
                    return c;
                }
            }

            if (_planes.TryGetSpot(name, out int plane))
            {
                return plane;
            }

            // The spot's process equivalent: its colour space with this component at 1 and the others at 0, converted to DeviceCMYK.
            Span<float> unit = stackalloc float[Math.Max(components, 1)];
            unit.Clear();
            unit[component] = 1;
            Span<float> cmyk = stackalloc float[ColorantPlanes.ProcessCount];
            PdfColorConverter converter = _document!.GetColorConverter(
                color.ColorSpace, color.Conversion with { Target = DeviceColorModel.Cmyk, TransferFunction = null }, color.DefaultColorSpaces);
            converter.Convert(unit, cmyk);
            return _planes.AddSpot(name, cmyk);
        }

        /// <summary>Returns the plane of each component of a DeviceN space (−1 for <c>None</c>), worked out once per space and render.</summary>
        private int[] DeviceNPlanes(PdfDeviceNColorSpace space, in DisplayColor color)
        {
            if (_deviceNPlanes.TryGetValue(space, out int[]? planes))
            {
                return planes;
            }

            IReadOnlyList<CosName> names = space.ColorantNames;
            planes = new int[names.Count];
            for (int index = 0; index < planes.Length; index++)
            {
                planes[index] = names[index].Equals(NoneName) ? -1 : PlaneOf(names[index], color, index, planes.Length);
            }

            _deviceNPlanes.Add(space, planes);
            return planes;
        }

        private PdfColorConverter InkConverter(in DisplayColor color)
        {
            if (_inkConverter is null || !ReferenceEquals(_inkSpace, color.ColorSpace) || !ReferenceEquals(_inkDefaults, color.DefaultColorSpaces) || _inkConversion != color.Conversion)
            {
                _inkSpace = color.ColorSpace;
                _inkDefaults = color.DefaultColorSpaces;
                _inkConversion = color.Conversion;
                _inkConverter = _document!.GetColorConverter(color.ColorSpace, color.Conversion with { Target = DeviceColorModel.Cmyk }, color.DefaultColorSpaces);
            }

            return _inkConverter;
        }

        /// <summary>
        /// The pixel rectangle the scan converter's shape can paint, within the clip, cleared in the layers; false when it paints nothing.
        /// </summary>
        private bool TryBeginLayers(bool black, out PixelBounds bounds)
        {
            bounds = default;
            if (!_scan.TryGetBounds(out int left, out int top, out int right, out int bottom))
            {
                return false;
            }

            _clip.GetBounds(out int clipLeft, out int clipTop, out int clipRight, out int clipBottom);
            bounds = new PixelBounds(
                Math.Max(Math.Max(left, clipLeft), 0),
                Math.Max(Math.Max(top, clipTop), 0),
                Math.Min(Math.Min(right, clipRight), _width),
                Math.Min(Math.Min(bottom, clipBottom), _height));
            if (bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom)
            {
                _scan.Reset(_width, _height);
                return false;
            }

            Clear(_cmyLayer = Layer(_cmyLayer), bounds);
            if (black)
            {
                Clear(_blackLayer = Layer(_blackLayer), bounds);
            }

            return true;
        }

        private Bitmap Layer(Bitmap? layer)
        {
            if (layer is not null && layer.Width == _width && layer.Height == _height)
            {
                return layer;
            }

            layer?.Dispose();
            return new Bitmap(_width, _height);
        }

        private static void Clear(Bitmap layer, in PixelBounds bounds)
        {
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                layer.GetRow(y).Slice(bounds.Left * 4, (bounds.Right - bounds.Left) * 4).Clear();
            }
        }

        /// <summary>Composites one ink into the planes by the coverage the shape left in the CMY layer's alpha (stencil masks).</summary>
        private void MergeInk(in PixelBounds bounds, Ink ink, Compositor compositor)
        {
            int width = _planes.Width;
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                ReadOnlySpan<uint> layer = MemoryMarshal.Cast<byte, uint>(_cmyLayer!.GetRow(y));
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    int coverage = (int)(layer[x] >> 24);
                    if (coverage == 0)
                    {
                        continue;
                    }

                    int index = (y * width) + x;
                    for (int plane = 0; plane < ink.Count; plane++)
                    {
                        if (ink.Marked[plane])
                        {
                            Span<byte> tints = _planes.Plane(plane);
                            tints[index] = (byte)compositor.CompositeTint(tints[index], ink.Tints[plane], coverage, plane >= ColorantPlanes.ProcessCount);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Composites a process paint whose colour varies (an image, a shading) from the CMY and black layers into the planes: the
        /// four process colourants take the paint's, and the spot colourants are erased unless it overprints (Table 146, "any process
        /// colour space"; overprint mode 1 does not apply to images and shadings, §8.6.7).
        /// </summary>
        private void MergeProcess(in PixelBounds bounds, Compositor compositor, bool overprint)
        {
            int width = _planes.Width;
            Span<byte> source = stackalloc byte[ColorantPlanes.ProcessCount];
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                ReadOnlySpan<uint> cmy = MemoryMarshal.Cast<byte, uint>(_cmyLayer!.GetRow(y));
                ReadOnlySpan<uint> black = MemoryMarshal.Cast<byte, uint>(_blackLayer!.GetRow(y));
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    uint pixel = cmy[x];
                    int coverage = (int)(pixel >> 24);
                    if (coverage == 0)
                    {
                        continue;
                    }

                    // The layers hold 1 − tint premultiplied by the coverage; both have the same coverage.
                    source[0] = Ink.Unpremultiplied((pixel >> 16) & 0xFF, coverage);
                    source[1] = Ink.Unpremultiplied((pixel >> 8) & 0xFF, coverage);
                    source[2] = Ink.Unpremultiplied(pixel & 0xFF, coverage);
                    source[3] = Ink.Unpremultiplied((black[x] >> 16) & 0xFF, coverage);
                    int index = (y * width) + x;
                    for (int plane = 0; plane < ColorantPlanes.ProcessCount; plane++)
                    {
                        Span<byte> tints = _planes.Plane(plane);
                        tints[index] = (byte)compositor.CompositeTint(tints[index], source[plane], coverage, spot: false);
                    }

                    if (!overprint)
                    {
                        for (int plane = ColorantPlanes.ProcessCount; plane < _planes.Count; plane++)
                        {
                            Span<byte> tints = _planes.Plane(plane);
                            tints[index] = (byte)compositor.CompositeTint(tints[index], 0, coverage, spot: true);
                        }
                    }
                }
            }
        }

        /// <summary>Paints the image (§8.9) as a process paint into the planes; see <see cref="DrawImage"/> for the geometry.</summary>
        private void PreviewImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion, in Matrix inverse)
        {
            _previewOutputIntent ??= conversion.OutputIntent;
            if (_previewImageConverter is null || !ReferenceEquals(_previewImageSpace, image.ColorSpace) || !ReferenceEquals(_previewImageDefaults, defaultColorSpaces) || _previewImageConversion != conversion)
            {
                _previewImageSpace = image.ColorSpace;
                _previewImageDefaults = defaultColorSpaces;
                _previewImageConversion = conversion;
                _previewImageConverter = _document!.GetColorConverter(image.ColorSpace, conversion with { Target = DeviceColorModel.Cmyk }, defaultColorSpaces);
                _previewImageBaseConverter = null;
            }

            PdfColorConverter converter = _previewImageConverter;
            PdfColorConverter? baseConverter = image.ColorSpace is PdfIndexedColorSpace indexed && !image.Matte.IsEmpty
                ? _previewImageBaseConverter ??= _document!.GetColorConverter(indexed.Base, conversion with { Target = DeviceColorModel.Cmyk }, defaultColorSpaces)
                : null;
            // A print preview draws every image at its own resolution (#362: reductions are for displaying only).
            if (image.GetSamplesAt(0) is not { } samples || (long)samples.Width * samples.Height > Array.MaxLength)
            {
                _scan.Reset(_width, _height);
                return;
            }

            ImageSource cmy = _cmyImage ??= new ImageSource();
            ImageSource black = _blackImage ??= new ImageSource();
            cmy.Prepare(image, samples, converter, baseConverter, CmyPass);
            black.Prepare(image, samples, converter, baseConverter, BlackPass);
            ImageMask mask = PrepareImageMask(image, samples);
            (byte[]? opacity, int maskWidth, int maskHeight, bool maskInterpolate) = (mask.Opacity, mask.Width, mask.Height, mask.Interpolate);
            if (!TryBeginLayers(black: true, out PixelBounds bounds))
            {
                return;
            }

            // The image coverage rule and sampling, as PaintImage applies them outside the preview (#104, §10.7.4).
            _scan.Rule = _imageRule;
            try
            {
                var cmyPainter = new ImagePainter(
                    _cmyLayer!.Pixels, _cmyLayer.Stride, cmy.Buffer, samples.Width, samples.Height, inverse, image.Interpolate, opacity, maskWidth, maskHeight, maskInterpolate, average: Averages);
                _scan.Paint(FillRule.NonZero, ref cmyPainter, keep: true);
                var blackPainter = new ImagePainter(
                    _blackLayer!.Pixels, _blackLayer.Stride, black.Buffer, samples.Width, samples.Height, inverse, image.Interpolate, opacity, maskWidth, maskHeight, maskInterpolate, average: Averages);
                _scan.Paint(FillRule.NonZero, ref blackPainter);
            }
            finally
            {
                _scan.Rule = _pathRule;
            }

            MergeProcess(bounds, _fillCompositor, _deviceParameters.FillOverprint);
        }

        /// <summary>Paints the colour through the stencil mask into the planes; see <see cref="FillImageMask"/>.</summary>
        private void PreviewImageMask(DisplayImageMask mask, in DisplayColor color, in Matrix inverse)
        {
            PrepareInk(color, _deviceParameters.FillOverprint, _fillInk);
            _maskSource.Prepare(mask);
            if (!TryBeginLayers(black: false, out PixelBounds bounds))
            {
                return;
            }

            var painter = new ImagePainter(_cmyLayer!.Pixels, _cmyLayer.Stride, uint.MaxValue, _maskSource.Buffer, mask.Width, mask.Height, inverse, mask.Interpolate, average: Averages);
            PaintImage(ref painter);
            MergeInk(bounds, _fillInk, _fillCompositor);
        }

        /// <summary>Paints a shading (§8.7.4) as a process paint into the planes; see <see cref="PaintShading"/>.</summary>
        private void PreviewShading(in DisplayShading shading, in Matrix inverse, in ShadingGeometry geometry, bool background, Compositor compositor, FillRule rule, bool overprint)
        {
            _previewOutputIntent ??= shading.Conversion.OutputIntent;
            bool hasBackground = background && shading.PaintsBackground && _cmyShading!.HasBackground;
            if ((geometry.IsEmpty && !hasBackground) || !TryBeginLayers(black: true, out PixelBounds bounds))
            {
                _scan.Reset(_width, _height);
                return;
            }

            var cmy = new ShadingPainter(
                _cmyLayer!.Pixels, _cmyLayer.Stride, inverse, geometry, _cmyShading!.Table, hasBackground, _cmyShading.Background, shading.Shading.AntiAlias);
            _scan.Paint(rule, ref cmy, keep: true);
            var black = new ShadingPainter(
                _blackLayer!.Pixels, _blackLayer.Stride, inverse, geometry, _blackShading!.Table, hasBackground, _blackShading.Background, shading.Shading.AntiAlias);
            _scan.Paint(rule, ref black);
            MergeProcess(bounds, compositor, overprint);
        }

        /// <summary>
        /// Paints a tiling pattern's tiled cells (§8.7.3) into the planes as a process paint. The cell is rendered in RGB, so its
        /// colours come back to DeviceCMYK through the formulas of §10.4.2.3 (a deviation: the cell's own colourants and overprinting
        /// are not kept).
        /// </summary>
        private void PreviewTiling(ReadOnlySpan<uint> tiles, PixelBox area, FillRule rule, Compositor compositor, bool overprint)
        {
            if (!TryBeginLayers(black: true, out PixelBounds bounds))
            {
                return;
            }

            var painter = new TilingPainter(_cmyLayer!.Pixels, _cmyLayer.Stride, tiles, area.Left, area.Top, area.Width, area.Height, default);
            _scan.Paint(rule, ref painter);
            MergeRgbLayer(bounds, compositor, overprint);
        }

        /// <summary>
        /// Paints the prepared mesh layer (#110: function-based and triangle-mesh shadings) into the preview: its RGB lands in the scratch
        /// layer, is converted to process colourants and merges as a process paint, as a tiling cell's does.
        /// </summary>
        private void PreviewMesh(bool hasBackground, FillRule rule, Compositor compositor, bool overprint)
        {
            if (!TryBeginLayers(black: true, out PixelBounds bounds))
            {
                _scan.Reset(_width, _height);
                return;
            }

            var painter = new MeshPainter(_cmyLayer!.Pixels, _cmyLayer.Stride, _meshLayer, hasBackground, _shadingSource.Background, default);
            _scan.Paint(rule, ref painter);
            MergeRgbLayer(bounds, compositor, overprint);
        }

        /// <summary>Converts the premultiplied RGB painted into the scratch layer to the layers' process form and merges it.</summary>
        private void MergeRgbLayer(PixelBounds bounds, Compositor compositor, bool overprint)
        {
            PdfColorConverter converter = _document!.GetColorConverter(
                PdfDeviceRgbColorSpace.Instance, new ColorConversion { Target = DeviceColorModel.Cmyk, OutputIntent = _previewOutputIntent }, defaults: null);
            Span<byte> rgb = stackalloc byte[3];
            Span<byte> cmyk = stackalloc byte[ColorantPlanes.ProcessCount];
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                Span<uint> cmy = MemoryMarshal.Cast<byte, uint>(_cmyLayer!.GetRow(y));
                Span<uint> black = MemoryMarshal.Cast<byte, uint>(_blackLayer!.GetRow(y));
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    uint pixel = cmy[x];
                    int coverage = (int)(pixel >> 24);
                    if (coverage == 0)
                    {
                        continue;
                    }

                    // Unpremultiplied RGB to CMYK, then the layers' form: 1 − tint premultiplied by the coverage.
                    rgb[0] = (byte)(255 - Ink.Unpremultiplied((pixel >> 16) & 0xFF, coverage));
                    rgb[1] = (byte)(255 - Ink.Unpremultiplied((pixel >> 8) & 0xFF, coverage));
                    rgb[2] = (byte)(255 - Ink.Unpremultiplied(pixel & 0xFF, coverage));
                    converter.Convert(rgb, cmyk, 1);
                    cmy[x] = Premultiplied(cmyk[0], cmyk[1], cmyk[2], coverage);
                    black[x] = Premultiplied(cmyk[3], cmyk[3], cmyk[3], coverage);
                }
            }

            MergeProcess(bounds, compositor, overprint);
        }

        private static uint Premultiplied(int c, int m, int y, int coverage) =>
            (uint)Compositor.Divide255((255 - y) * coverage)
            | ((uint)Compositor.Divide255((255 - m) * coverage) << 8)
            | ((uint)Compositor.Divide255((255 - c) * coverage) << 16)
            | ((uint)coverage << 24);

        /// <summary>Builds the CMY and black colour tables of a shading for the preview; false when its colours paint nothing.</summary>
        private bool PreparePreviewShading(in DisplayShading shading)
        {
            ShadingSource cmy = _cmyShading ??= new ShadingSource();
            ShadingSource black = _blackShading ??= new ShadingSource();
            return cmy.Prepare(_document!, shading, CmyPass) & black.Prepare(_document!, shading, BlackPass);
        }

        /// <summary>A rectangle of device pixels, right and bottom exclusive.</summary>
        private readonly record struct PixelBounds(int Left, int Top, int Right, int Bottom);

        /// <summary>The colourants one paint marks and the tint it gives each, kept for the next paint.</summary>
        private sealed class Ink
        {
            public byte[] Tints { get; private set; } = new byte[8];

            public bool[] Marked { get; private set; } = new bool[8];

            /// <summary>Gets the number of planes the ink was prepared for.</summary>
            public int Count { get; private set; }

            /// <summary>Returns the tint (0 to 255) of an additive value premultiplied by <paramref name="coverage"/>.</summary>
            public static byte Unpremultiplied(uint value, int coverage) =>
                (byte)(255 - Math.Min(255, ((value * 255) + ((uint)coverage / 2)) / (uint)coverage));

            /// <summary>Starts over for <paramref name="planes"/> planes, each marked or not with <paramref name="tint"/>.</summary>
            public void Reset(int planes, bool mark, float tint = 0)
            {
                if (Tints.Length < planes)
                {
                    Tints = new byte[Math.Max(planes, Tints.Length * 2)];
                    Marked = new bool[Tints.Length];
                }

                Count = planes;
                Tints.AsSpan(0, planes).Fill(Byte(tint));
                Marked.AsSpan(0, planes).Fill(mark);
            }

            /// <summary>Marks plane <paramref name="plane"/> with <paramref name="tint"/> (0 to 1).</summary>
            public void Set(int plane, float tint)
            {
                Tints[plane] = Byte(tint);
                Marked[plane] = true;
            }

            private static byte Byte(float tint) => (byte)((Math.Clamp(tint, 0f, 1f) * 255f) + 0.5f);
        }

        /// <summary>Composites an ink into the planes by the coverage of each run.</summary>
        private readonly ref struct InkSink(ColorantPlanes planes, Ink ink, Compositor compositor) : ICoverageSink
        {
            public void Span(int row, int x, int count, int coverage)
            {
                int start = (row * planes.Width) + x;
                for (int plane = 0; plane < ink.Count; plane++)
                {
                    if (!ink.Marked[plane])
                    {
                        continue;
                    }

                    Span<byte> tints = planes.Plane(plane).Slice(start, count);
                    int tint = ink.Tints[plane];
                    bool spot = plane >= ColorantPlanes.ProcessCount;
                    if (coverage >= 255 && compositor.IsNormal && compositor.Alpha == 255)
                    {
                        tints.Fill((byte)tint);
                        continue;
                    }

                    foreach (ref byte value in tints)
                    {
                        value = (byte)compositor.CompositeTint(value, tint, coverage, spot);
                    }
                }
            }
        }
    }
}
