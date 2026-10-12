using System.Runtime.InteropServices;
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
/// Reuse one instance for many renders: its working buffers are pooled and kept, so a warm render allocates nothing per path, cell or
/// scanline. An instance renders one page at a time; use one per thread to render in parallel (a document is safe for concurrent
/// renders while nobody changes it).
/// </para>
/// </remarks>
public sealed partial class SoftwareRasterizer : IDisposable
{
    private static readonly RenderOptions DefaultOptions = new();

    private readonly Backend _backend = new();
    private bool _disposed;

    /// <summary>Renders <paramref name="page"/> at 72 DPI into a new pooled bitmap, on white.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The bitmap; dispose it to return its buffer to the pool.</returns>
    /// <exception cref="ArgumentException">The page would need more pixels than a bitmap can hold.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>).</remarks>
    public Bitmap Render(PdfPage page) => Render(page, DefaultOptions);

    /// <summary>Renders <paramref name="page"/> into a new pooled bitmap of <see cref="RenderOptions.GetPixelSize"/> pixels, on white.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The scale and rotation.</param>
    /// <returns>The bitmap; dispose it to return its buffer to the pool.</returns>
    /// <exception cref="ArgumentException">The page would need more pixels than a bitmap can hold.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>).</remarks>
    public Bitmap Render(PdfPage page, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        PageGeometry geometry = options.Measure(page);
        DisplayList list = page.CreateDisplayList();
        var bitmap = new Bitmap(geometry.PixelWidth, geometry.PixelHeight);
        try
        {
            Draw(list, bitmap, geometry.Transform, geometry.PixelWidth, geometry.PixelHeight, white: true);
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
    /// <param name="options">The scale and rotation.</param>
    /// <exception cref="ArgumentException"><paramref name="target"/> is smaller than the page's pixel size.</exception>
    public void Render(PdfPage page, Bitmap target, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        PageGeometry geometry = options.Measure(page);
        if (target.Width < geometry.PixelWidth || target.Height < geometry.PixelHeight)
        {
            throw new ArgumentException($"The page needs {geometry.PixelWidth} x {geometry.PixelHeight} pixels; the bitmap has {target.Width} x {target.Height}.", nameof(target));
        }

        Draw(page.CreateDisplayList(), target, geometry.Transform, geometry.PixelWidth, geometry.PixelHeight, white: true);
    }

    /// <summary>Draws <paramref name="list"/> over the current pixels of <paramref name="target"/>, without a background.</summary>
    /// <param name="list">The display list.</param>
    /// <param name="target">The bitmap; drawing is clipped to it.</param>
    /// <param name="deviceTransform">
    /// The transform from the list's default user space to the bitmap's pixels (origin top-left, y down), such as
    /// <see cref="RenderOptions.GetDeviceTransform"/>; a tile renderer adds its own offset.
    /// </param>
    /// <remarks>ADR 0006; ISO 32000-2 §8.3.2.2 (device space).</remarks>
    public void Render(DisplayList list, Bitmap target, in Matrix deviceTransform)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        Draw(list, target, deviceTransform, target.Width, target.Height, white: false);
    }

    /// <summary>Returns the rasterizer's pooled working buffers; it renders no more after this.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _backend.Dispose();
        }
    }

    private void Draw(DisplayList list, Bitmap target, in Matrix device, int width, int height, bool white)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (white)
        {
            for (int y = 0; y < height; y++)
            {
                MemoryMarshal.Cast<byte, uint>(target.Pixels.Slice(y * target.Stride, width * 4)).Fill(uint.MaxValue);
            }
        }

        _backend.Begin(list.Document, target, device, width, height);
        try
        {
            list.Replay(_backend);
        }
        finally
        {
            _backend.End();
        }
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
        }

        public void End()
        {
            _document = null;
            _target = null;
            _space = null;
            _defaults = null;
            _converter = null;
            EndImages();
        }

        public void SaveState()
        {
            if (_depth == _stack.Length)
            {
                Array.Resize(ref _stack, _stack.Length * 2);
            }

            _stack[_depth++] = _ctm;
        }

        public void RestoreState()
        {
            if (_depth > 0)
            {
                SetTransform(in _stack[--_depth]);
            }
        }

        public void SetTransform(in Matrix transform)
        {
            _ctm = transform;
            _toDevice = transform * _device;
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            PdfColorConverter converter = Converter(color);
            if (converter.PaintsNothing || path.IsEmpty)
            {
                return;
            }

            Span<float> rgb = stackalloc float[4];
            converter.Convert(color.Components, rgb);
            uint pixel = Pack(rgb);

            _scan.Reset(_width, _height);
            AddPath(path);
            _scan.Fill(_target!, rule, pixel);
        }

        public void Dispose()
        {
            _scan.Dispose();
            DisposeImages();
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
        private void AddPath(PathView path)
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
                        Finish(ref subpath);
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

            Finish(ref subpath);
        }

        /// <summary>Ends a subpath: closes it if open, and gives a degenerate one the pixel under its point (§8.5.3.3.1).</summary>
        private void Finish(ref Subpath subpath)
        {
            if (!subpath.IsOpen)
            {
                return;
            }

            Close(ref subpath);
            if (subpath.HasSegments && subpath.IsDegenerate)
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
