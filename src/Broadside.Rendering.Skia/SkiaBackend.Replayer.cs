using Broadside.Diagnostics;
using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>The backend the display list replays through: graphics state, path mapping, colour and the skipped operations.</summary>
    private sealed partial class Replayer : IRenderingBackend, IDisposable
    {
        /// <summary>Device coordinates are kept within ±2^30 pixels, so that Skia's float arithmetic stays finite.</summary>
        private const double DeviceLimit = 1073741824.0;

        // The geometry of the operation being drawn, and the Skia paths built from such geometry (see PathCache).
        private readonly PathRecorder _path = new();
        private readonly PathCache _paths = new();
        private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private SKCanvas? _canvas;
        private SKMatrix _base;
        private Matrix _device;
        private Matrix _ctm;
        private Matrix _toDevice;
        private SavedState[] _stack = new SavedState[16];
        private int _depth;
        private PdfDocument? _document;

        private PdfColorSpace? _space;
        private ColorConversion _conversion;
        private PdfDefaultColorSpaces? _defaults;
        private PdfColorConverter? _converter;

        // The caches every replayer of a render shares, held by the backend's own replayer (pattern cells replay through others).
        private readonly ImageCache? _imageCache;
        private readonly ShadingCache? _shadingCache;
        private readonly WeakReference<PdfDocument?> _budgetDocument = new(null);

        public Replayer()
        {
            _stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
            _imageCache = new ImageCache();
            _shadingCache = new ShadingCache();
        }

        /// <summary>Creates the replayer of the pattern cells at <paramref name="depth"/>, sharing <paramref name="owner"/>'s caches and diagnostics.</summary>
        private Replayer(Replayer owner, int depth)
        {
            _stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
            _owner = owner;
            _patternDepth = depth;
        }

        /// <summary>Gets how many Skia images the backend made from display-list images and masks.</summary>
        public int ImagesCreated => _imageCache?.Created ?? 0;

        /// <summary>Gets the bytes of converted pixels the backend keeps.</summary>
        public long CachedImageBytes => _imageCache?.Bytes ?? 0;

        public void Begin(PdfDocument document, SKCanvas canvas, in Matrix device)
        {
            if (_imageCache is not null && (!_budgetDocument.TryGetTarget(out PdfDocument? last) || !ReferenceEquals(last, document)))
            {
                // The document's image budget (spec D10) bounds the converted pixels kept; read once per document, as reading allocates.
                _imageCache.Budget = document.CacheStatistics.Images.Budget;
                _budgetDocument.SetTarget(document);
            }

            _document = document;
            _canvas = canvas;
            _base = canvas.TotalMatrix;
            _device = device;
            _ctm = Matrix.Identity;
            _toDevice = device;
            _depth = 0;
            Diagnostics.Clear();
            _reported = 0;
            BeginCompositing();
        }

        public void End()
        {
            _canvas = null;
            _lastProgram = null;
            _lastFace = null;
            _document = null;
            _space = null;
            _defaults = null;
            _converter = null;
        }

        public void SaveState()
        {
            if (_depth == _stack.Length)
            {
                Array.Resize(ref _stack, _stack.Length * 2);
            }

            _stack[_depth++] = new SavedState(_ctm, _compositing);
            _canvas!.Save();
        }

        public void RestoreState()
        {
            if (_depth > 0)
            {
                SavedState saved = _stack[--_depth];
                SetTransform(saved.Ctm);
                _compositing = saved.Compositing;
            }

            _canvas!.Restore();
        }

        public void SetTransform(in Matrix transform)
        {
            _ctm = transform;
            _toDevice = transform * _device;
        }

        /// <summary>Fills the path (ISO 32000-2 §8.5.3.3) mapped to the device, so Skia sees device coordinates in double precision.</summary>
        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            if (path.IsEmpty || _compositing.FillAlpha == 0 || !TryGetColor(color, out SKColor paint))
            {
                return;
            }

            AddDevicePath(path);
            _path.FillType = rule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            _fill.Color = paint.WithAlpha(_compositing.FillAlpha);
            _fill.BlendMode = _compositing.BlendMode;
            _canvas!.SetMatrix(in _base);
            _canvas.DrawPath(_paths.Get(_path), _fill);
        }

        /// <summary>Intersects the clip with the inside of the path (ISO 32000-2 §8.5.4), anti-aliased like a fill.</summary>
        public void IntersectClip(PathView path, FillRule rule)
        {
            AddDevicePath(path);
            _path.FillType = rule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
            _canvas!.SetMatrix(in _base);
            _canvas.ClipPath(_paths.Get(_path), SKClipOperation.Intersect, antialias: true);
        }

        public void Dispose()
        {
            _paths.Dispose();
            _fill.Dispose();
            DisposeStrokes();
            DisposeCompositing();
            DisposeImages();
            DisposeShadings();
            DisposeTiling();
            _imageCache?.Dispose();
            _shadingCache?.Dispose();
            ReleaseFaces();
        }

        private static float Clamp(double value) => (float)Math.Clamp(value, -DeviceLimit, DeviceLimit);

        private static byte Channel(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);

        /// <summary>Converts a colour to opaque 8-bit RGB as the software rasterizer does; false when it paints nothing (Separation <c>None</c>).</summary>
        private bool TryGetColor(in DisplayColor color, out SKColor paint)
        {
            if (_converter is null || !ReferenceEquals(_space, color.ColorSpace) || !ReferenceEquals(_defaults, color.DefaultColorSpaces) || _conversion != color.Conversion)
            {
                _space = color.ColorSpace;
                _defaults = color.DefaultColorSpaces;
                _conversion = color.Conversion;
                _converter = _document!.GetColorConverter(color.ColorSpace, color.Conversion with { Target = DeviceColorModel.Rgb }, color.DefaultColorSpaces);
            }

            if (_converter.PaintsNothing)
            {
                paint = default;
                return false;
            }

            Span<float> rgb = stackalloc float[4];
            _converter.Convert(color.Components, rgb);
            paint = new SKColor(Channel(rgb[0]), Channel(rgb[1]), Channel(rgb[2]));
            return true;
        }

        /// <summary>
        /// Records in <see cref="_path"/> <paramref name="path"/> mapped to the device. Skia closes open subpaths for filling and
        /// ignores a lone moveto; a degenerate subpath (one point, at least one segment) is given the device pixel under its point,
        /// as ISO 32000-2 §8.5.3.3.1 asks and the software rasterizer does.
        /// </summary>
        private void AddDevicePath(PathView path)
        {
            _path.Clear();
            ReadOnlySpan<PathVerb> verbs = path.Verbs;
            ReadOnlySpan<PathPoint> points = path.Points;
            PathPoint start = default;
            bool open = false;
            bool hasSegments = false;
            bool degenerate = true;
            int p = 0;
            foreach (PathVerb verb in verbs)
            {
                switch (verb)
                {
                    case PathVerb.MoveTo:
                        FinishSubpath(open, hasSegments, degenerate, start);
                        start = points[p];
                        (open, hasSegments, degenerate) = (true, false, true);
                        MoveTo(points[p++]);
                        break;
                    case PathVerb.LineTo:
                        degenerate &= points[p] == start;
                        hasSegments = true;
                        LineTo(points[p++]);
                        break;
                    case PathVerb.QuadTo:
                        degenerate &= points[p] == start && points[p + 1] == start;
                        hasSegments = true;
                        SKPoint control = Map(points[p]);
                        SKPoint end = Map(points[p + 1]);
                        _path.QuadTo(control, end);
                        p += 2;
                        break;
                    case PathVerb.CubicTo:
                        degenerate &= points[p] == start && points[p + 1] == start && points[p + 2] == start;
                        hasSegments = true;
                        SKPoint first = Map(points[p]);
                        SKPoint second = Map(points[p + 1]);
                        SKPoint to = Map(points[p + 2]);
                        _path.CubicTo(first, second, to);
                        p += 3;
                        break;
                    case PathVerb.Close:
                        hasSegments |= open;
                        _path.Close();
                        FinishSubpath(open, hasSegments, degenerate, start);
                        open = false;
                        break;
                }
            }

            FinishSubpath(open, hasSegments, degenerate, start);
        }

        private void FinishSubpath(bool open, bool hasSegments, bool degenerate, PathPoint start)
        {
            if (!open || !hasSegments || !degenerate)
            {
                return;
            }

            SKPoint point = Map(start);
            if (!(float.IsFinite(point.X) && float.IsFinite(point.Y)))
            {
                return;
            }

            float left = MathF.Floor(point.X);
            float top = MathF.Floor(point.Y);
            _path.MoveTo(left, top);
            _path.LineTo(left, top + 1);
            _path.LineTo(left + 1, top + 1);
            _path.LineTo(left + 1, top);
            _path.Close();
        }

        private void MoveTo(PathPoint point) => _path.MoveTo(Map(point));

        private void LineTo(PathPoint point) => _path.LineTo(Map(point));

        private SKPoint Map(PathPoint point)
        {
            PathPoint device = _toDevice.Transform(point);
            return new SKPoint(Clamp(device.X), Clamp(device.Y));
        }
    }
}
