using System.Runtime.InteropServices;
using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The software rasterizer: the fully managed backend that renders a page or a <see cref="DisplayList"/> into a <see cref="Bitmap"/>
/// of premultiplied BGRA pixels with analytic anti-aliasing. It is the reference every other backend is compared with.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0006: it consumes display lists only, replaying them through its own <see cref="IRenderingBackend"/>. Paths are mapped to
/// device space, curves flattened to within a tenth of a pixel (ISO 32000-2 §10.7.2 lets a processor choose its flatness), open
/// subpaths closed, a trailing lone moveto ignored and a degenerate subpath given the pixel under it (§8.5.3.3.1), then scan
/// converted with the non-zero winding or even-odd rule (§8.5.3.3.2, §8.5.3.3.3, §10.7.4; see the remarks on coverage there). Colours
/// convert to RGB through <see cref="PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces?)"/>, so
/// the engine's colour management (<see cref="IColorManagement"/>) decides them (§8.6, §10).
/// </para>
/// <para>
/// Clipping (§8.5.4): the clip starts as the rendered page area and is intersected with each clipping path the list hands over,
/// saved and restored with the graphics state; a form is clipped to its bounding box (§8.10.1). Clips are anti-aliased: a rectangle
/// that stays aligned with the pixel axes only narrows a clip rectangle (no mask), any other path becomes a pooled coverage mask.
/// Every paint's coverage is multiplied by the clip's.
/// </para>
/// <para>
/// Tiles and regions (spec #33 story 14): <see cref="RenderOptions.Region"/>, or a cached <see cref="DisplayList"/> rendered with
/// <see cref="Render(DisplayList, Bitmap, in Matrix)"/> under the page's device transform moved by a tile's offset, draws only the
/// paints whose bounds reach the bitmap. A tile has the pixels of the whole render at the same place: exactly where the arithmetic
/// is exact, and otherwise within one level at a pixel whose coverage sits on a rounding boundary.
/// </para>
/// <para>
/// Reuse one instance for many renders: its working buffers are pooled and kept, so a warm render allocates nothing per path, cell or
/// scanline. An instance renders one page at a time; use one per thread to render in parallel (a document is safe for concurrent
/// renders while nobody changes it).
/// </para>
/// <para>
/// Viewer capability 2 (issue #97): a page render takes a cancellation token and a progress receiver from its
/// <see cref="RenderOptions"/>, and <see cref="BeginRender(PdfPage, Bitmap, RenderOptions)"/> draws in time slices that resume
/// where they stopped (<see cref="ProgressiveRender"/>), for hosts that must not block.
/// </para>
/// </remarks>
public sealed partial class SoftwareRasterizer : IDisposable
{
    private static readonly RenderOptions DefaultOptions = new();
    private static readonly Content.ContentOptions DefaultContentOptions = new();

    /// <summary>
    /// How many device pixels beyond a paint's bounds it may still touch: a degenerate subpath's pixel, the one-pixel pen of the
    /// thinnest line and stroke adjustment's rounding and snapping (ISO 32000-2 §8.5.3.3.1, §10.7.5). Region culling widens the
    /// bitmap by this much.
    /// </summary>
    private const double RegionMargin = 2;

    private readonly Backend _backend = new();
    private DisplayListReplay? _progressive;
    private bool _disposed;

    /// <summary>Renders <paramref name="page"/> at 72 DPI into a new pooled bitmap, on white.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The bitmap; dispose it to return its buffer to the pool.</returns>
    /// <exception cref="ArgumentException">The page would need more pixels than a bitmap can hold.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>); §12.5.5 and §12.5.3 for the annotation and form-field layers drawn over the content (see <see cref="PdfPage.CreateDisplayList(DisplayLayers, DisplayIntent, Content.ContentOptions)"/>).</remarks>
    public Bitmap Render(PdfPage page) => Render(page, DefaultOptions);

    /// <summary>Renders <paramref name="page"/> into a new pooled bitmap of <see cref="RenderOptions.GetPixelSize"/> pixels, on white.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The scale, rotation, layers and intent.</param>
    /// <returns>The bitmap; dispose it to return its buffer to the pool.</returns>
    /// <exception cref="ArgumentException">The page would need more pixels than a bitmap can hold.</exception>
    /// <exception cref="OperationCanceledException"><see cref="RenderOptions.CancellationToken"/> was cancelled; the bitmap is returned to the pool.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>); §12.5.5 and §12.5.3 for the annotation and form-field layers drawn over the content (see <see cref="PdfPage.CreateDisplayList(DisplayLayers, DisplayIntent, Content.ContentOptions)"/>). Cancellation and progress come from <paramref name="options"/> (viewer capability 2).</remarks>
    public Bitmap Render(PdfPage page, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfBusy();
        PageGeometry geometry = options.Measure(page);
        DisplayList list = Build(page, options);
        var bitmap = new Bitmap(geometry.PixelWidth, geometry.PixelHeight);
        try
        {
            Draw(list, bitmap, geometry.Transform, geometry.PixelWidth, geometry.PixelHeight, white: true, cull: options.Region is not null, options.CancellationToken, DrawingProgress(options));
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    /// <summary>
    /// Renders <paramref name="page"/> into the top-left <see cref="RenderOptions.GetPixelSize"/> pixels of <paramref name="target"/>,
    /// on white; pixels outside that area are left as they are.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="target">The bitmap, for example one over caller memory; at least the page's pixel size.</param>
    /// <param name="options">The scale, rotation, layers and intent.</param>
    /// <exception cref="ArgumentException"><paramref name="target"/> is smaller than the page's pixel size.</exception>
    /// <exception cref="OperationCanceledException">
    /// <see cref="RenderOptions.CancellationToken"/> was cancelled; the bitmap keeps what was drawn (nothing, when the build was cancelled).
    /// </exception>
    public void Render(PdfPage page, Bitmap target, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfBusy();
        PageGeometry geometry = CheckTarget(page, target, options);
        Draw(Build(page, options), target, geometry.Transform, geometry.PixelWidth, geometry.PixelHeight, white: true, cull: options.Region is not null, options.CancellationToken, DrawingProgress(options));
    }
    /// <summary>
    /// Builds <paramref name="page"/>'s display list and starts drawing it into the top-left <see cref="RenderOptions.GetPixelSize"/>
    /// pixels of <paramref name="target"/>, on white, in time slices: each <see cref="ProgressiveRender.Continue"/> draws until its
    /// budget is used.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="target">The bitmap, at least the page's pixel size; pixels outside the page's area are left as they are.</param>
    /// <param name="options">The scale and rotation, the cancellation token and the progress receiver.</param>
    /// <returns>The render: the page area is white and nothing is drawn yet. Continue it to the end, or dispose it.</returns>
    /// <exception cref="ArgumentException"><paramref name="target"/> is smaller than the page's pixel size.</exception>
    /// <exception cref="OperationCanceledException"><see cref="RenderOptions.CancellationToken"/> was cancelled while the list was built.</exception>
    /// <exception cref="InvalidOperationException">Another progressive render of this rasterizer has not ended.</exception>
    /// <remarks>
    /// Viewer capability 2 (issue #97). The display list is built here, at once (cancellable, reporting progress from 0 to 0.5); the
    /// slices draw it, reporting from 0.5 to 1. To keep a long build off a thread that must not block, build the list elsewhere
    /// (<see cref="PdfPage.CreateDisplayList(ContentOptions)"/>) and use
    /// <see cref="BeginRender(DisplayList, Bitmap, in Matrix, CancellationToken, IProgress{double})"/>.
    /// </remarks>
    public ProgressiveRender BeginRender(PdfPage page, Bitmap target, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfBusy();
        PageGeometry geometry = CheckTarget(page, target, options);
        DisplayList list = Build(page, options);
        FillWhite(target, geometry.PixelWidth, geometry.PixelHeight);
        return Begin(list, target, geometry.Transform, geometry.PixelWidth, geometry.PixelHeight, cull: options.Region is not null, options.CancellationToken, DrawingProgress(options));
    }

    /// <summary>
    /// Starts drawing <paramref name="list"/> over the current pixels of <paramref name="target"/>, without a background, in time
    /// slices: each <see cref="ProgressiveRender.Continue"/> draws until its budget is used.
    /// </summary>
    /// <param name="list">The display list.</param>
    /// <param name="target">The bitmap; drawing is clipped to it.</param>
    /// <param name="deviceTransform">The transform from the list's default user space to the bitmap's pixels, as for <see cref="Render(DisplayList, Bitmap, in Matrix)"/>.</param>
    /// <param name="cancellationToken">Cancels the render before its next operation.</param>
    /// <param name="progress">Receives the share of the list's operations drawn so far, from 0 to 1 (see <see cref="DisplayList.BeginReplay(IRenderingBackend, CancellationToken, IProgress{double})"/>), or <see langword="null"/>.</param>
    /// <returns>The render, with nothing drawn yet. Continue it to the end, or dispose it.</returns>
    /// <exception cref="InvalidOperationException">Another progressive render of this rasterizer has not ended.</exception>
    /// <remarks>ADR 0006; viewer capability 2 (issue #97). Continued to the end, it draws exactly what <see cref="Render(DisplayList, Bitmap, in Matrix)"/> draws.</remarks>
    public ProgressiveRender BeginRender(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfBusy();
        return Begin(list, target, deviceTransform, target.Width, target.Height, cull: true, cancellationToken, progress);
    }

    /// <summary>Draws <paramref name="list"/> over the current pixels of <paramref name="target"/>, without a background.</summary>
    /// <param name="list">The display list.</param>
    /// <param name="target">The bitmap; drawing is clipped to it.</param>
    /// <param name="deviceTransform">
    /// The transform from the list's default user space to the bitmap's pixels (origin top-left, y down), such as
    /// <see cref="RenderOptions.GetDeviceTransform"/>; a tile renderer adds its own offset (the tile's top-left pixel moved to the
    /// origin) and gets the same pixels as the whole page's render.
    /// </param>
    /// <remarks>
    /// ADR 0006; ISO 32000-2 §8.3.2.2 (device space). Spec #33 story 14 (viewer capability 1): only the paints whose bounds reach the
    /// bitmap are drawn (<see cref="DisplayList.Replay(IRenderingBackend, PdfRectangle)"/>), so one cached list renders tiles at any
    /// scale, each costing what its own content costs. The list computes the bounds on its first such render.
    /// </remarks>
    public void Render(DisplayList list, Bitmap target, in Matrix deviceTransform)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfBusy();
        Draw(list, target, deviceTransform, target.Width, target.Height, white: false, cull: true, CancellationToken.None, progress: null);
    }

    /// <summary>Returns the rasterizer's pooled working buffers; it renders no more after this.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_progressive is not null)
            {
                _progressive = null;
                _backend.End();
            }

            _disposed = true;
            _backend.Dispose();
        }
    }

    /// <summary>Returns whether <paramref name="replay"/> is the progressive render this rasterizer is drawing.</summary>
    internal bool IsDrawing(DisplayListReplay replay) => !_disposed && ReferenceEquals(_progressive, replay);

    /// <summary>Ends the progressive render of <paramref name="render"/>: releases the per-render state, so the rasterizer may render again.</summary>
    internal void EndProgressive(DisplayListReplay render)
    {
        if (ReferenceEquals(_progressive, render))
        {
            _progressive = null;
            _backend.End();
        }
    }

    /// <summary>Builds the page's display list with the options' token, reporting its progress as the first half of the render's.</summary>
    private static DisplayList Build(PdfPage page, RenderOptions options)
    {
        if (options.Progress is null && !options.CancellationToken.CanBeCanceled)
        {
            return page.CreateDisplayList(options.Layers, options.Intent, DefaultContentOptions);
        }

        return page.CreateDisplayList(options.Layers, options.Intent, new ContentOptions()
            .WithCancellation(options.CancellationToken)
            .WithProgress(options.Progress is { } progress ? new ScaledProgress(progress, 0, 0.5) : null));
    }

    /// <summary>The receiver of the drawing's progress: the second half of the render's.</summary>
    private static ScaledProgress? DrawingProgress(RenderOptions options) =>
        options.Progress is { } progress ? new ScaledProgress(progress, 0.5, 0.5) : null;

    private static PageGeometry CheckTarget(PdfPage page, Bitmap target, RenderOptions options)
    {
        PageGeometry geometry = options.Measure(page);
        if (target.Width < geometry.PixelWidth || target.Height < geometry.PixelHeight)
        {
            throw new ArgumentException($"The page needs {geometry.PixelWidth} x {geometry.PixelHeight} pixels; the bitmap has {target.Width} x {target.Height}.", nameof(target));
        }

        return geometry;
    }

    private static void FillWhite(Bitmap target, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            MemoryMarshal.Cast<byte, uint>(target.Pixels.Slice(y * target.Stride, width * 4)).Fill(uint.MaxValue);
        }
    }

    private void ThrowIfBusy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_progressive is not null)
        {
            throw new InvalidOperationException("The rasterizer is drawing a progressive render; continue it to the end or dispose it first.");
        }
    }

    private ProgressiveRender Begin(DisplayList list, Bitmap target, in Matrix device, int width, int height, bool cull, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        _backend.Begin(list.Document, target, device, width, height);
        DisplayListReplay replay = cull && TryGetRegion(device, width, height, out PdfRectangle region)
            ? list.BeginReplay(_backend, region, cancellationToken, progress)
            : list.BeginReplay(_backend, cancellationToken, progress);
        _progressive = replay;
        return new ProgressiveRender(this, replay, target);
    }

    private void Draw(DisplayList list, Bitmap target, in Matrix device, int width, int height, bool white, bool cull, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        if (white)
        {
            FillWhite(target, width, height);
        }

        _backend.Begin(list.Document, target, device, width, height);
        try
        {
            PdfRectangle region = default;
            bool culled = cull && TryGetRegion(device, width, height, out region);
            if (progress is null && !cancellationToken.CanBeCanceled)
            {
                if (culled)
                {
                    list.Replay(_backend, region);
                }
                else
                {
                    list.Replay(_backend);
                }
            }
            else if (culled)
            {
                // A cancellable or reported replay of a region: one slice without a deadline.
                ProgressiveReplay(list.BeginReplay(_backend, region, cancellationToken, progress));
            }
            else
            {
                list.Replay(_backend, cancellationToken, progress);
            }
        }
        finally
        {
            _backend.End();
        }
    }

    /// <summary>Runs a replay to its end in one slice.</summary>
    private static void ProgressiveReplay(DisplayListReplay replay) => replay.Continue(Timeout.InfiniteTimeSpan);

    /// <summary>Reports a part of a render's progress: <c>offset + value × scale</c>.</summary>
    private sealed class ScaledProgress(IProgress<double> target, double offset, double scale) : IProgress<double>
    {
        public void Report(double value) => target.Report(offset + (value * scale));
    }

    /// <summary>
    /// Returns the part of default user space that can change the pixels of a <paramref name="width"/> × <paramref name="height"/>
    /// bitmap under <paramref name="device"/>: the bitmap widened by <see cref="RegionMargin"/> pixels, mapped back. False when the
    /// transform has no inverse.
    /// </summary>
    private static bool TryGetRegion(in Matrix device, int width, int height, out PdfRectangle region)
    {
        region = default;
        if (!device.TryInvert(out Matrix inverse))
        {
            return false;
        }

        PathPoint a = inverse.Transform(-RegionMargin, -RegionMargin);
        PathPoint b = inverse.Transform(width + RegionMargin, -RegionMargin);
        PathPoint c = inverse.Transform(width + RegionMargin, height + RegionMargin);
        PathPoint d = inverse.Transform(-RegionMargin, height + RegionMargin);
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double bottom = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double top = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        if (!(double.IsFinite(left) && double.IsFinite(bottom) && double.IsFinite(right) && double.IsFinite(top)))
        {
            return false;
        }

        region = new PdfRectangle(left, bottom, right, top);
        return true;
    }

    /// <summary>The backend the rasterizer replays display lists through: graphics state, path mapping and colour.</summary>
    private sealed partial class Backend : IRenderingBackend, IDisposable
    {
        /// <summary>The largest flattening error, in device pixels.</summary>
        private const double Tolerance = 0.1;

        /// <summary>The most line segments one curve is flattened into.</summary>
        private const int MaxCurveSegments = 1000;

        /// <summary>Device coordinates are kept within ±2^40 pixels so that the clipping arithmetic stays finite.</summary>
        private const double CoordinateLimit = 1099511627776.0;

        private readonly ScanlineRasterizer _scan = new();
        private readonly Stroker _stroker;
        private readonly RasterClip _clip = new();
        private Matrix[] _stack = new Matrix[16];
        private int _depth;
        private Matrix _device;
        private Matrix _ctm;
        private Matrix _toDevice;
        private PdfDocument? _document;
        private Bitmap? _target;
        private int _width;
        private int _height;

        private PdfColorSpace? _space;
        private ColorConversion _conversion;
        private PdfDefaultColorSpaces? _defaults;
        private PdfColorConverter? _converter;

        public Backend() => _stroker = new Stroker(_scan);

        public void Begin(PdfDocument document, Bitmap target, in Matrix device, int width, int height)
        {
            _document = document;
            _target = target;
            _device = device;
            _ctm = Matrix.Identity;
            _toDevice = device;
            _depth = 0;
            _width = width;
            _height = height;
            _clip.Reset(width, height);
            _scan.Clip = _clip;
            ResetCompositing();
            BeginAnnotations();
        }

        public void End()
        {
            _clip.Release();
            _document = null;
            _target = null;
            _space = null;
            _defaults = null;
            _converter = null;
            EndImages();
            EndShadings();
        }

        public void SaveState()
        {
            if (_depth == _stack.Length)
            {
                Array.Resize(ref _stack, _stack.Length * 2);
            }

            _stack[_depth++] = _ctm;
            _clip.Save();
            SaveCompositing();
        }

        public void RestoreState()
        {
            if (_depth > 0)
            {
                SetTransform(in _stack[--_depth]);
            }

            _clip.Restore();
            RestoreCompositing();
        }

        public void SetTransform(in Matrix transform)
        {
            _ctm = transform;
            _toDevice = transform * _device;
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            PdfColorConverter converter = Converter(color);
            if (converter.PaintsNothing || path.IsEmpty || _clip.IsEmpty || _fillCompositor.PaintsNothing)
            {
                return;
            }

            Span<float> rgb = stackalloc float[4];
            converter.Convert(color.Components, rgb);
            uint pixel = Pack(rgb);

            _scan.Reset(_width, _height);
            AddPath(path);
            _scan.Fill(_target!, rule, pixel, _fillCompositor);
        }

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
            PdfColorConverter converter = Converter(color);
            if (converter.PaintsNothing || path.IsEmpty || _clip.IsEmpty || _strokeCompositor.PaintsNothing)
            {
                return;
            }

            Span<float> rgb = stackalloc float[4];
            converter.Convert(color.Components, rgb);
            uint pixel = Pack(rgb);

            _scan.Reset(_width, _height);
            _stroker.Stroke(path, style, _toDevice);
            _scan.Fill(_target!, FillRule.NonZero, pixel, _strokeCompositor);
        }

        /// <summary>
        /// Intersects the clip with the path (ISO 32000-2 §8.5.4): a rectangle that stays aligned with the pixel axes only narrows the
        /// clip rectangle; any other path is scan converted like a fill (§8.5.3.3) into the clip's coverage mask.
        /// </summary>
        public void IntersectClip(PathView path, FillRule rule)
        {
            if (_clip.IsEmpty)
            {
                return;
            }

            if (TryMapRectangle(path, out double left, out double top, out double right, out double bottom))
            {
                _clip.IntersectRectangle(left, top, right, bottom);
                return;
            }

            _scan.Reset(_width, _height);
            AddPath(path);
            _clip.IntersectPath(_scan, rule);
        }

        public void Dispose()
        {
            _scan.Dispose();
            _clip.Dispose();
            DisposeImages();
            DisposeAnnotations();
        }

        /// <summary>
        /// Whether <paramref name="path"/> is one rectangle (four points, as <c>re</c> makes it, optionally back to its start and
        /// closed) whose sides map to device lines along the pixel axes, and its device bounds if so. Its inside is the same under
        /// either fill rule. A rectangle of a single point is a degenerate subpath, which is left to the general case.
        /// </summary>
        private bool TryMapRectangle(PathView path, out double left, out double top, out double right, out double bottom)
        {
            left = top = right = bottom = 0;
            ReadOnlySpan<PathVerb> verbs = path.Verbs;
            ReadOnlySpan<PathPoint> points = path.Points;
            int segments = verbs.Length > 0 && verbs[^1] == PathVerb.Close ? verbs.Length - 1 : verbs.Length;
            if (segments is not (4 or 5) || verbs[0] != PathVerb.MoveTo || points.Length != segments)
            {
                return false;
            }

            for (int index = 1; index < segments; index++)
            {
                if (verbs[index] != PathVerb.LineTo)
                {
                    return false;
                }
            }

            if (segments == 5 && points[4] != points[0])
            {
                return false;
            }

            Device a = Map(points[0]);
            Device b = Map(points[1]);
            Device c = Map(points[2]);
            Device d = Map(points[3]);
            bool horizontalFirst = a.Y == b.Y && b.X == c.X && c.Y == d.Y && d.X == a.X;
            bool verticalFirst = a.X == b.X && b.Y == c.Y && c.X == d.X && d.Y == a.Y;
            if (!(horizontalFirst || verticalFirst) || !double.IsFinite(a.X + a.Y + c.X + c.Y) || (a == b && b == c))
            {
                return false;
            }

            left = Math.Min(a.X, c.X);
            right = Math.Max(a.X, c.X);
            top = Math.Min(a.Y, c.Y);
            bottom = Math.Max(a.Y, c.Y);
            return true;
        }

        private static uint Pack(ReadOnlySpan<float> rgb)
        {
            Span<byte> bgra = [Channel(rgb[2]), Channel(rgb[1]), Channel(rgb[0]), 0xFF];
            return MemoryMarshal.Read<uint>(bgra);
        }

        private static byte Channel(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);

        private PdfColorConverter Converter(in DisplayColor color)
        {
            if (_converter is null || !ReferenceEquals(_space, color.ColorSpace) || !ReferenceEquals(_defaults, color.DefaultColorSpaces) || _conversion != color.Conversion)
            {
                _space = color.ColorSpace;
                _defaults = color.DefaultColorSpaces;
                _conversion = color.Conversion;
                _converter = _document!.GetColorConverter(color.ColorSpace, color.Conversion with { Target = DeviceColorModel.Rgb }, color.DefaultColorSpaces);
            }

            return _converter;
        }

        /// <summary>Adds every subpath of <paramref name="path"/> to the scan converter, applying the filling rules of §8.5.3.3.1.</summary>
        /// <param name="path">The path, mapped to the device by the current transform.</param>
        /// <param name="degenerateDots">Whether a degenerate subpath paints the pixel under it (path objects do; glyph outlines do not).</param>
        private void AddPath(PathView path, bool degenerateDots = true)
        {
            ReadOnlySpan<PathVerb> verbs = path.Verbs;
            ReadOnlySpan<PathPoint> points = path.Points;
            var subpath = default(Subpath);
            int p = 0;
            foreach (PathVerb verb in verbs)
            {
                switch (verb)
                {
                    case PathVerb.MoveTo:
                        Finish(ref subpath, degenerateDots);
                        subpath = new Subpath(points[p], Map(points[p]));
                        p++;
                        break;
                    case PathVerb.LineTo:
                        Line(ref subpath, points[p]);
                        p++;
                        break;
                    case PathVerb.QuadTo:
                        Quad(ref subpath, points[p], points[p + 1]);
                        p += 2;
                        break;
                    case PathVerb.CubicTo:
                        Cubic(ref subpath, points[p], points[p + 1], points[p + 2]);
                        p += 3;
                        break;
                    case PathVerb.Close:
                        subpath.HasSegments |= subpath.IsOpen;
                        Close(ref subpath);
                        break;
                }
            }

            Finish(ref subpath, degenerateDots);
        }

        /// <summary>Ends a subpath: closes it if open, and gives a degenerate one the pixel under its point (§8.5.3.3.1).</summary>
        private void Finish(ref Subpath subpath, bool degenerateDots)
        {
            if (!subpath.IsOpen)
            {
                return;
            }

            Close(ref subpath);
            if (degenerateDots && subpath.HasSegments && subpath.IsDegenerate)
            {
                _scan.AddPixel(subpath.Start.X, subpath.Start.Y);
            }

            subpath.IsOpen = false;
        }

        private void Close(ref Subpath subpath)
        {
            if (subpath.Current != subpath.Start)
            {
                _scan.AddLine(subpath.Current.X, subpath.Current.Y, subpath.Start.X, subpath.Start.Y);
                subpath.Current = subpath.Start;
            }
        }

        private void Line(ref Subpath subpath, PathPoint to)
        {
            subpath.Touch(to);
            Device end = Map(to);
            _scan.AddLine(subpath.Current.X, subpath.Current.Y, end.X, end.Y);
            subpath.Current = end;
        }

        private void Quad(ref Subpath subpath, PathPoint control, PathPoint to)
        {
            subpath.Touch(control);
            subpath.Touch(to);
            Device p0 = subpath.Current;
            Device p1 = Map(control);
            Device p2 = Map(to);
            double d = Length(p0.X - (2 * p1.X) + p2.X, p0.Y - (2 * p1.Y) + p2.Y);
            int n = Outside(p0, p1, p2, p2) ? 1 : Segments(d / (4 * Tolerance));
            Device previous = p0;
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n;
                double u = 1 - t;
                var point = i == n ? p2 : new Device(
                    (u * u * p0.X) + (2 * u * t * p1.X) + (t * t * p2.X),
                    (u * u * p0.Y) + (2 * u * t * p1.Y) + (t * t * p2.Y));
                _scan.AddLine(previous.X, previous.Y, point.X, point.Y);
                previous = point;
            }

            subpath.Current = p2;
        }

        private void Cubic(ref Subpath subpath, PathPoint control1, PathPoint control2, PathPoint to)
        {
            subpath.Touch(control1);
            subpath.Touch(control2);
            subpath.Touch(to);
            Device p0 = subpath.Current;
            Device p1 = Map(control1);
            Device p2 = Map(control2);
            Device p3 = Map(to);

            // Uniform subdivision: the chord error over a step h is at most h²/8 · max|B''| ≤ h²/8 · 6d, so n steps keep it within
            // the tolerance when n ≥ sqrt(3d / (4 · tolerance)).
            double d = Math.Max(
                Length(p0.X - (2 * p1.X) + p2.X, p0.Y - (2 * p1.Y) + p2.Y),
                Length(p1.X - (2 * p2.X) + p3.X, p1.Y - (2 * p2.Y) + p3.Y));
            int n = Outside(p0, p1, p2, p3) ? 1 : Segments(3 * d / (4 * Tolerance));
            Device previous = p0;
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n;
                double u = 1 - t;
                double a = u * u * u;
                double b = 3 * u * u * t;
                double c = 3 * u * t * t;
                double e = t * t * t;
                var point = i == n ? p3 : new Device(
                    (a * p0.X) + (b * p1.X) + (c * p2.X) + (e * p3.X),
                    (a * p0.Y) + (b * p1.Y) + (c * p2.Y) + (e * p3.Y));
                _scan.AddLine(previous.X, previous.Y, point.X, point.Y);
                previous = point;
            }

            subpath.Current = p3;
        }

        private static int Segments(double squared) =>
            double.IsFinite(squared) ? (int)Math.Clamp(Math.Ceiling(Math.Sqrt(squared)), 1, MaxCurveSegments) : MaxCurveSegments;

        private static double Length(double x, double y) => Math.Sqrt((x * x) + (y * y));

        /// <summary>
        /// Whether a curve's control polygon lies wholly above, below, left or right of the bitmap. Such a curve changes no pixel's
        /// coverage differently from its chord: above and below show nothing, right affects nothing, and left adds the same cover to
        /// every row it spans as the chord does (only the endpoints' rows matter for the winding to their right).
        /// </summary>
        private bool Outside(Device p0, Device p1, Device p2, Device p3) =>
            (p0.X <= 0 && p1.X <= 0 && p2.X <= 0 && p3.X <= 0)
            || (p0.X >= _width && p1.X >= _width && p2.X >= _width && p3.X >= _width)
            || (p0.Y <= 0 && p1.Y <= 0 && p2.Y <= 0 && p3.Y <= 0)
            || (p0.Y >= _height && p1.Y >= _height && p2.Y >= _height && p3.Y >= _height);

        private Device Map(PathPoint point)
        {
            Matrix m = _toDevice;
            double x = (m.A * point.X) + (m.C * point.Y) + m.E;
            double y = (m.B * point.X) + (m.D * point.Y) + m.F;
            return new Device(Math.Clamp(x, -CoordinateLimit, CoordinateLimit), Math.Clamp(y, -CoordinateLimit, CoordinateLimit));
        }
    }

    /// <summary>A point in device pixels.</summary>
    private readonly record struct Device(double X, double Y);

    /// <summary>The subpath being added: where it started and is now, and whether every point so far is its first point.</summary>
    private struct Subpath
    {
        public Subpath(PathPoint first, Device start)
        {
            First = first;
            Start = start;
            Current = start;
            IsOpen = true;
            IsDegenerate = true;
        }

        public PathPoint First { get; }

        public Device Start { get; }

        public Device Current { get; set; }

        public bool IsOpen { get; set; }

        public bool HasSegments { get; set; }

        public bool IsDegenerate { get; private set; }

        /// <summary>Records a point of a segment: the subpath has a segment, and is degenerate only while every point is its first.</summary>
        public void Touch(PathPoint point)
        {
            HasSegments = true;
            IsDegenerate &= point == First;
        }
    }
}
