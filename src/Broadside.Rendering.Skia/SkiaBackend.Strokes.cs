using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    private sealed partial class Replayer
    {
        /// <summary>User coordinates are kept within ±10^18 so that Skia's float arithmetic stays finite.</summary>
        private const double UserLimit = 1e18;

        /// <summary>The most dash boundaries one stroke dispenses before drawing the rest solid, as the software rasterizer does.</summary>
        private const int MaxDashes = 1 << 20;

        /// <summary>The largest flattening error of a curve that is dashed or stroke-adjusted, in device pixels, as the software rasterizer flattens.</summary>
        private const double Tolerance = 0.1;

        /// <summary>The most line segments one curve is flattened into.</summary>
        private const int MaxCurveSegments = 1000;

        /// <summary>
        /// The width of a one-pixel line: a 1/1024 pixel wider than one pixel, because Skia draws strokes at most one device pixel wide
        /// as hairlines, whose coverage differs from that of the stroke's outline (the software rasterizer's model).
        /// </summary>
        private const double OnePixel = 1 + (1.0 / 1024);

        private readonly SKPaint _stroke;

        // The collected subpath flattened for dashing and stroke adjustment, in user space, and whether each point lies inside a curve.
        private PathPoint[] _flat = new PathPoint[64];
        private bool[] _smooth = new bool[64];
        private int _flatCount;

        // The subpath being collected, in user space: its verbs and points, and the stroke-adjustment flags of its flattened points.
        private PathVerb[] _verbs = new PathVerb[32];
        private PathPoint[] _points = new PathPoint[64];
        private byte[] _snap = new byte[64];
        private int _verbCount;
        private int _pointCount;

        private double[] _dash = new double[8];
        private int _dashLength;
        private double _dashPhase;
        private int _dashBudget;

        private LineCap _cap;
        private bool _thinnest;
        private bool _snapping;
        private double _snapOffset;
        private Matrix _deviceToUser;

        /// <summary>
        /// Strokes the path (ISO 32000-2 §8.5.3.2) with the line parameters of §8.4.3: the subpaths, dashed in user space and with
        /// stroke adjustment applied, are collected in user space and stroked by Skia under the current transform, so a non-uniform
        /// transform gives an elliptical pen. The thinnest line (width 0, or under half a pixel with stroke adjustment, §10.7.5) is
        /// stroked one device pixel wide in device space instead.
        /// </summary>
        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
            if (path.IsEmpty || !TryGetColor(color, out SKColor paint))
            {
                return;
            }

            float width = Configure(style);
            _path.Clear();
            _path.FillType = SKPathFillType.Winding;
            CollectSubpaths(path);
            if (_path.IsEmpty)
            {
                return;
            }

            _stroke.Color = paint;
            _stroke.StrokeCap = style.LineCap switch
            {
                LineCap.Round => SKStrokeCap.Round,
                LineCap.ProjectingSquare => SKStrokeCap.Square,
                _ => SKStrokeCap.Butt,
            };
            _stroke.StrokeJoin = style.LineJoin switch
            {
                LineJoin.Round => SKStrokeJoin.Round,
                LineJoin.Bevel => SKStrokeJoin.Bevel,
                _ => SKStrokeJoin.Miter,
            };

            // §8.4.3.5: the limit is the ratio of the miter length to the line width, as Skia defines its miter limit.
            _stroke.StrokeMiter = (float)Math.Min(style.MiterLimit, float.MaxValue);
            _stroke.StrokeWidth = width;
            _canvas!.SetMatrix(in _base);
            if (_thinnest)
            {
                _path.Transform(_toDevice.A, _toDevice.B, _toDevice.C, _toDevice.D, _toDevice.E, _toDevice.F, DeviceLimit);
            }
            else
            {
                SKMatrix toDevice = ToSkia(_toDevice);
                _canvas.Concat(in toDevice);
            }

            _canvas.DrawPath(_paths.Get(_path), _stroke);
        }

        private void DisposeStrokes()
        {
            _stroke.Dispose();
        }

        private static SKMatrix ToSkia(in Matrix m) => new(
            (float)m.A, (float)m.C, (float)m.E,
            (float)m.B, (float)m.D, (float)m.F,
            0, 0, 1);

        /// <summary>Returns the largest factor by which the linear part of <paramref name="m"/> stretches a vector.</summary>
        private static double MaxScale(in Matrix m)
        {
            double sum = (m.A * m.A) + (m.B * m.B) + (m.C * m.C) + (m.D * m.D);
            double determinant = (m.A * m.D) - (m.B * m.C);
            double root = Math.Sqrt(Math.Max(0, (sum * sum) - (4 * determinant * determinant)));
            return Math.Sqrt((sum + root) / 2);
        }

        private static PathPoint User(PathPoint point) =>
            new(Math.Clamp(point.X, -UserLimit, UserLimit), Math.Clamp(point.Y, -UserLimit, UserLimit));

        private static SKPoint ToSkia(PathPoint point) => new((float)point.X, (float)point.Y);

        private static void Grow<T>(ref T[] array, int needed)
        {
            if (needed > array.Length)
            {
                Array.Resize(ref array, Math.Max(needed, array.Length * 2));
            }
        }

        /// <summary>Reads the line parameters (§8.4.3, §10.7.5) and returns the width Skia strokes with, in the space it strokes in.</summary>
        private float Configure(in StrokeStyle style)
        {
            _cap = style.LineCap;
            double scale = MaxScale(_toDevice);
            double width = style.LineWidth;
            double deviceWidth = width * scale;
            _thinnest = !(width > 0) || (style.StrokeAdjustment && deviceWidth < 0.5);
            _snapping = (_thinnest || style.StrokeAdjustment) && _toDevice.TryInvert(out _deviceToUser);
            double strokeWidth = width;
            _snapOffset = 0.5;
            if (_thinnest)
            {
                // §8.4.3.2, §10.7.5: the thinnest line, one device pixel wide.
                strokeWidth = OnePixel;
            }
            else if (style.StrokeAdjustment && double.IsFinite(deviceWidth))
            {
                // §10.7.5: a whole number of pixels, no more than half a pixel from the requested width.
                double pixels = Math.Max(1, Math.Round(deviceWidth, MidpointRounding.AwayFromZero));
                strokeWidth = (pixels == 1 ? OnePixel : pixels) / scale;
                _snapOffset = pixels % 2 == 1 ? 0.5 : 0;
            }

            _dashLength = 0;
            ReadOnlySpan<double> dash = style.DashArray;
            double sum = 0;
            foreach (double element in dash)
            {
                sum += element;
            }

            if (dash.Length > 0 && sum > 0 && double.IsFinite(sum))
            {
                Grow(ref _dash, dash.Length);
                dash.CopyTo(_dash);
                _dashLength = dash.Length;
                double period = dash.Length % 2 == 1 ? 2 * sum : sum;
                double phase = style.DashPhase;
                _dashPhase = double.IsFinite(phase) && phase > 0 ? phase % period : 0;
                _dashBudget = MaxDashes;
            }

            return (float)Math.Min(strokeWidth, float.MaxValue);
        }

        /// <summary>Collects each subpath of <paramref name="path"/> and adds its stroke geometry to <see cref="_path"/>.</summary>
        private void CollectSubpaths(PathView path)
        {
            ReadOnlySpan<PathVerb> verbs = path.Verbs;
            ReadOnlySpan<PathPoint> points = path.Points;
            bool open = false;
            bool hasSegments = false;
            bool degenerate = true;
            PathPoint first = default;
            int p = 0;
            foreach (PathVerb verb in verbs)
            {
                switch (verb)
                {
                    case PathVerb.MoveTo:
                        if (open)
                        {
                            Finish(closed: false, hasSegments, degenerate);
                        }

                        first = points[p];
                        _verbCount = 0;
                        _pointCount = 0;
                        Add(PathVerb.MoveTo, points.Slice(p++, 1));
                        (open, hasSegments, degenerate) = (true, false, true);
                        break;
                    case PathVerb.LineTo or PathVerb.QuadTo or PathVerb.CubicTo when !open:
                        p += verb == PathVerb.LineTo ? 1 : verb == PathVerb.QuadTo ? 2 : 3;
                        break;
                    case PathVerb.LineTo:
                        degenerate &= points[p] == first;
                        hasSegments = true;
                        Add(verb, points.Slice(p++, 1));
                        break;
                    case PathVerb.QuadTo:
                        degenerate &= points[p] == first && points[p + 1] == first;
                        hasSegments = true;
                        Add(verb, points.Slice(p, 2));
                        p += 2;
                        break;
                    case PathVerb.CubicTo:
                        degenerate &= points[p] == first && points[p + 1] == first && points[p + 2] == first;
                        hasSegments = true;
                        Add(verb, points.Slice(p, 3));
                        p += 3;
                        break;
                    case PathVerb.Close:
                        if (open)
                        {
                            Finish(closed: true, hasSegments: true, degenerate);
                            open = false;
                        }

                        break;
                }
            }

            if (open)
            {
                Finish(closed: false, hasSegments, degenerate);
            }
        }

        private void Add(PathVerb verb, ReadOnlySpan<PathPoint> points)
        {
            Grow(ref _verbs, _verbCount + 1);
            Grow(ref _points, _pointCount + points.Length);
            _verbs[_verbCount++] = verb;
            foreach (PathPoint point in points)
            {
                _points[_pointCount++] = User(point);
            }
        }

        /// <summary>Adds the stroke geometry of the collected subpath (§8.5.3.2).</summary>
        private void Finish(bool closed, bool hasSegments, bool degenerate)
        {
            if (!hasSegments)
            {
                // A lone moveto paints nothing.
                return;
            }

            if (degenerate)
            {
                // A degenerate subpath paints a dot only with round caps: Skia gives a zero-length segment its caps.
                if (_cap == LineCap.Round)
                {
                    SKPoint point = ToSkia(_points[0]);
                    _path.MoveTo(point);
                    _path.LineTo(point);
                }

                return;
            }

            if (!_snapping && _dashLength == 0)
            {
                // Skia strokes the curves themselves.
                AddSubpath(_path, closed);
                return;
            }

            // Stroke adjustment and dashes work on the subpath flattened as the software rasterizer flattens it, so that both move the
            // same vertices and cut at the same lengths.
            Flatten();
            if (_snapping)
            {
                Snap(closed);
            }

            if (_dashLength == 0)
            {
                AddPolyline(closed);
                return;
            }

            if (closed)
            {
                AddFlat(_flat[0], smooth: false);
            }

            Dash(closed);
        }

        private void AddPolyline(bool closed)
        {
            _path.MoveTo(ToSkia(_flat[0]));
            for (int i = 1; i < _flatCount; i++)
            {
                _path.LineTo(ToSkia(_flat[i]));
            }

            if (closed)
            {
                _path.Close();
            }
        }

        private void AddSubpath(PathRecorder target, bool closed)
        {
            int p = 0;
            for (int v = 0; v < _verbCount; v++)
            {
                switch (_verbs[v])
                {
                    case PathVerb.MoveTo:
                        target.MoveTo(ToSkia(_points[p++]));
                        break;
                    case PathVerb.LineTo:
                        target.LineTo(ToSkia(_points[p++]));
                        break;
                    case PathVerb.QuadTo:
                        target.QuadTo(ToSkia(_points[p]), ToSkia(_points[p + 1]));
                        p += 2;
                        break;
                    case PathVerb.CubicTo:
                        target.CubicTo(ToSkia(_points[p]), ToSkia(_points[p + 1]), ToSkia(_points[p + 2]));
                        p += 3;
                        break;
                }
            }

            if (closed)
            {
                target.Close();
            }
        }

        /// <summary>
        /// Moves the vertices of flattened segments that map to horizontal or vertical device lines, except those inside a curve, so
        /// that a line of the adjusted width has its edges on pixel boundaries: centres at half pixels for an odd number of pixels, on
        /// pixel boundaries for an even one (§10.7.5: no more than half a pixel off). The software rasterizer's rule, point for point.
        /// </summary>
        private void Snap(bool closed)
        {
            Grow(ref _snap, _flatCount);
            Span<byte> flags = _snap.AsSpan(0, _flatCount);
            flags.Clear();
            int segments = closed ? _flatCount : _flatCount - 1;
            for (int s = 0; s < segments; s++)
            {
                int a = s;
                int b = s + 1 == _flatCount ? 0 : s + 1;
                if (_smooth[a] || _smooth[b])
                {
                    continue;
                }

                PathPoint from = _toDevice.Transform(_flat[a]);
                PathPoint to = _toDevice.Transform(_flat[b]);
                if (Math.Abs(from.Y - to.Y) <= 1e-6)
                {
                    flags[a] |= 2;
                    flags[b] |= 2;
                }

                if (Math.Abs(from.X - to.X) <= 1e-6)
                {
                    flags[a] |= 1;
                    flags[b] |= 1;
                }
            }

            for (int i = 0; i < _flatCount; i++)
            {
                if (flags[i] == 0)
                {
                    continue;
                }

                PathPoint device = _toDevice.Transform(_flat[i]);
                double x = (flags[i] & 1) != 0 ? SnapCoordinate(device.X) : device.X;
                double y = (flags[i] & 2) != 0 ? SnapCoordinate(device.Y) : device.Y;
                _flat[i] = User(_deviceToUser.Transform(x, y));
            }
        }

        private double SnapCoordinate(double value) =>
            Math.Abs(value) >= DeviceLimit ? value : _snapOffset > 0 ? Math.Floor(value) + _snapOffset : Math.Floor(value + 0.5);

        /// <summary>
        /// Flattens the collected subpath into <see cref="_flat"/> in user space, as finely as its device image needs (the software
        /// rasterizer's tolerance of a tenth of a pixel, §10.7.2), marking the points inside curves as smooth.
        /// </summary>
        private void Flatten()
        {
            _flatCount = 0;
            int p = 0;
            for (int v = 0; v < _verbCount; v++)
            {
                switch (_verbs[v])
                {
                    case PathVerb.MoveTo or PathVerb.LineTo:
                        AddFlat(_points[p++], smooth: false);
                        break;
                    case PathVerb.QuadTo:
                        FlattenQuad(_points[p], _points[p + 1]);
                        p += 2;
                        break;
                    case PathVerb.CubicTo:
                        FlattenCubic(_points[p], _points[p + 1], _points[p + 2]);
                        p += 3;
                        break;
                }
            }
        }

        private void AddFlat(PathPoint point, bool smooth)
        {
            Grow(ref _flat, _flatCount + 1);
            Grow(ref _smooth, _flatCount + 1);
            _smooth[_flatCount] = smooth;
            _flat[_flatCount++] = point;
        }

        private void FlattenQuad(PathPoint control, PathPoint to)
        {
            PathPoint from = _flat[_flatCount - 1];
            PathPoint d0 = _toDevice.Transform(from);
            PathPoint d1 = _toDevice.Transform(control);
            PathPoint d2 = _toDevice.Transform(to);
            int n = Segments(Length(d0.X - (2 * d1.X) + d2.X, d0.Y - (2 * d1.Y) + d2.Y) / (4 * Tolerance));
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n;
                double u = 1 - t;
                AddFlat(
                    i == n ? to : new PathPoint(
                        (u * u * from.X) + (2 * u * t * control.X) + (t * t * to.X),
                        (u * u * from.Y) + (2 * u * t * control.Y) + (t * t * to.Y)),
                    smooth: i < n);
            }
        }

        private void FlattenCubic(PathPoint control1, PathPoint control2, PathPoint to)
        {
            PathPoint from = _flat[_flatCount - 1];
            PathPoint d0 = _toDevice.Transform(from);
            PathPoint d1 = _toDevice.Transform(control1);
            PathPoint d2 = _toDevice.Transform(control2);
            PathPoint d3 = _toDevice.Transform(to);

            // The bound the software rasterizer uses: n ≥ sqrt(3d / (4 · tolerance)) keeps the chord error within the tolerance.
            double d = Math.Max(
                Length(d0.X - (2 * d1.X) + d2.X, d0.Y - (2 * d1.Y) + d2.Y),
                Length(d1.X - (2 * d2.X) + d3.X, d1.Y - (2 * d2.Y) + d3.Y));
            int n = Segments(3 * d / (4 * Tolerance));
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n;
                double u = 1 - t;
                double a = u * u * u;
                double b = 3 * u * u * t;
                double c = 3 * u * t * t;
                double e = t * t * t;
                AddFlat(
                    i == n ? to : new PathPoint(
                        (a * from.X) + (b * control1.X) + (c * control2.X) + (e * to.X),
                        (a * from.Y) + (b * control1.Y) + (c * control2.Y) + (e * to.Y)),
                    smooth: i < n);
            }
        }

        private static int Segments(double squared) =>
            double.IsFinite(squared) ? (int)Math.Clamp(Math.Ceiling(Math.Sqrt(squared)), 1, MaxCurveSegments) : MaxCurveSegments;

        private static double Length(double x, double y) => Math.Sqrt((x * x) + (y * y));

        /// <summary>
        /// Cuts the flattened subpath into dashes by user space length and adds them to <see cref="_path"/> (§8.4.3.6): the pattern
        /// restarts at each subpath, a zero-length dash keeps its caps turned along the path, and on a closed subpath the last dash
        /// runs on into the first across its start when both are on (§8.4.3.4). Skia's dash effect is not used: it allocates an object
        /// per pattern, and it treats closed subpaths and zero-length dashes differently from the software rasterizer.
        /// </summary>
        private void Dash(bool closed)
        {
            int index = 0;
            double remaining = _dash[0];
            double phase = _dashPhase;
            while (phase > 0)
            {
                if (phase >= remaining)
                {
                    phase -= remaining;
                    index++;
                    remaining = _dash[index % _dashLength];
                }
                else
                {
                    remaining -= phase;
                    phase = 0;
                }
            }

            bool on = index % 2 == 0;

            // A closed subpath that starts on a dash holds that dash back (head) to draw it after the last one, joined to it.
            double head = closed && on && remaining > 0 ? remaining : -1;
            bool holding = head >= 0;
            bool emitting = false;
            bool started = false;
            bool solid = false;
            double travelled = 0;
            for (int i = 1; i < _flatCount; i++)
            {
                PathPoint a = _flat[i - 1];
                PathPoint b = _flat[i];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double length = Length(dx, dy);
                if (!(length > 0))
                {
                    continue;
                }

                double ux = dx / length;
                double uy = dy / length;
                if (!started)
                {
                    started = true;
                    if (on && !holding)
                    {
                        emitting = BeginDash(a, ux, uy, remaining);
                    }
                }

                double t = 0;
                while (!solid && remaining <= length - t)
                {
                    // A dash boundary on this segment.
                    t += remaining;
                    var boundary = new PathPoint(a.X + (ux * t), a.Y + (uy * t));
                    if (on && emitting)
                    {
                        _path.LineTo(ToSkia(boundary));
                    }

                    if (on)
                    {
                        holding = false;
                    }

                    emitting = false;
                    index++;
                    on = index % 2 == 0;
                    remaining = _dash[index % _dashLength];
                    if (--_dashBudget < 0)
                    {
                        // Past the budget the rest of the stroke is solid.
                        solid = true;
                        on = true;
                        remaining = double.PositiveInfinity;
                    }

                    if (on)
                    {
                        emitting = BeginDash(boundary, ux, uy, remaining);
                    }
                }

                remaining -= length - t;
                if (on && emitting)
                {
                    _path.LineTo(ToSkia(b));
                }

                travelled += length;
            }

            if (head < 0)
            {
                return;
            }

            if (holding)
            {
                // The first dash never ended: it covers the whole closed subpath, which is stroked closed.
                _path.MoveTo(ToSkia(_flat[0]));
                for (int i = 1; i < _flatCount; i++)
                {
                    _path.LineTo(ToSkia(_flat[i]));
                }

                _path.Close();
                return;
            }

            if (!(on && emitting))
            {
                _path.MoveTo(ToSkia(_flat[0]));
            }

            AddHead(Math.Min(head, travelled));
        }

        /// <summary>
        /// Starts a dash at <paramref name="point"/>: a moveto, or for a zero-length dash a segment too short to see along the path, so
        /// that Skia turns its caps with the path (§8.5.3.2). Returns whether the dash continues along the path.
        /// </summary>
        private bool BeginDash(PathPoint point, double ux, double uy, double length)
        {
            if (length > 0)
            {
                _path.MoveTo(ToSkia(point));
                return true;
            }

            if (_cap != LineCap.Butt)
            {
                double epsilon = Math.Max(1e-3, (Math.Abs(point.X) + Math.Abs(point.Y)) * 1e-6);
                _path.MoveTo(ToSkia(point));
                _path.LineTo(ToSkia(new PathPoint(point.X + (ux * epsilon), point.Y + (uy * epsilon))));
            }

            return false;
        }

        /// <summary>Continues <see cref="_path"/> along the flattened subpath from its start to <paramref name="stop"/>.</summary>
        private void AddHead(double stop)
        {
            double travelled = 0;
            for (int i = 1; i < _flatCount; i++)
            {
                PathPoint a = _flat[i - 1];
                PathPoint b = _flat[i];
                double length = Length(b.X - a.X, b.Y - a.Y);
                if (travelled + length >= stop)
                {
                    double t = length > 0 ? (stop - travelled) / length : 0;
                    _path.LineTo(ToSkia(new PathPoint(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t))));
                    return;
                }

                _path.LineTo(ToSkia(b));
                travelled += length;
            }
        }
    }
}
