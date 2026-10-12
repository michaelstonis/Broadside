using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Turns a stroked path into outline polygons and feeds them to a <see cref="ScanlineRasterizer"/>, which fills them with the
/// non-zero winding rule: the software rasterizer's stroker.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.5.3.2 with the line parameters of §8.4.3. Each subpath is flattened in user space (as finely as its device image
/// needs, §10.7.2), cut into dashes by user space lengths (§8.4.3.6) and outlined in "pen space": user space, where the pen is a
/// circle of half the line width, so the transform maps it to the ellipse a non-uniform CTM implies (§8.4.3.2). The outline of an
/// open polyline is its left offset forward, the end cap, its right offset backward and the start cap; a closed one is its two
/// offsets as two loops. Outer corners get the line join (§8.4.3.4; a miter beyond the limit becomes a bevel, §8.4.3.5), corners
/// between the flattened pieces of one curve a round join; inner corners meet at the offsets' intersection or turn through the
/// vertex. Every outline piece has the same orientation, so where parts of one stroke overlap the winding number only grows and the
/// non-zero rule paints the union once.
/// </para>
/// <para>
/// A width of 0, or a width under half a pixel with stroke adjustment on (§10.7.5), strokes with a one-pixel round pen in device
/// space instead. Stroke adjustment rounds the device width to whole pixels and, like the thinnest line, moves the vertices of
/// horizontal and vertical device segments so that the line's edges fall on pixel boundaries (§10.7.5: no more than half a pixel off).
/// </para>
/// <para>
/// Degenerate subpaths paint a dot only with round caps; a lone moveto paints nothing; zero-length dashes paint their caps oriented
/// along the path (§8.5.3.2). Dashes of a closed subpath that start and end "on" are joined across its start (§8.4.3.4). A stroke
/// dispenses at most 2^20 dashes; past that the rest of it is solid.
/// </para>
/// <para>
/// Hot path (CLAUDE.md): working arrays grow by doubling and are kept, so a warm stroker allocates nothing per subpath, segment, dash
/// or join.
/// </para>
/// </remarks>
internal sealed class Stroker
{
    /// <summary>The largest flattening error, in device pixels.</summary>
    private const double Tolerance = 0.1;

    /// <summary>The most line segments one curve or arc is flattened into.</summary>
    private const int MaxSegments = 1000;

    /// <summary>Device coordinates are kept within ±2^40 pixels, as the fill path keeps them.</summary>
    private const double CoordinateLimit = 1099511627776.0;

    /// <summary>User coordinates are kept within ±10^100 so that differences of two stay finite.</summary>
    private const double UserLimit = 1e100;

    /// <summary>The most dash boundaries one stroke dispenses before drawing the rest solid.</summary>
    private const int MaxDashes = 1 << 20;

    private readonly ScanlineRasterizer _scan;

    // The subpath being collected: its points in user space and in pen space, and whether each lies inside a flattened curve.
    private Vec[] _user = new Vec[64];
    private Vec[] _pen = new Vec[64];
    private bool[] _smooth = new bool[64];
    private byte[] _snap = new byte[64];
    private int _count;

    // The dash being collected and, for a closed subpath that starts on a dash, that first dash, kept to join the last one.
    private Vec[] _piece = new Vec[64];
    private bool[] _pieceSmooth = new bool[64];
    private int _pieceCount;
    private Vec[] _head = new Vec[64];
    private bool[] _headSmooth = new bool[64];
    private int _headCount;

    private double[] _dash = new double[8];
    private int _dashLength;
    private double _dashPeriod;
    private double _dashPhase;
    private int _dashBudget;

    private Matrix _userToDevice;
    private Matrix _deviceToUser;
    private Matrix _userToPen;
    private Matrix _penToDevice;
    private double _arcScale;
    private double _radius;
    private LineCap _cap;
    private LineJoin _join;
    private double _miterLimitSquared;
    private bool _snapping;
    private double _snapOffset;

    // The outline polygon being emitted, in device pixels.
    private bool _hasPoint;
    private Vec _first;
    private Vec _last;

    public Stroker(ScanlineRasterizer scan) => _scan = scan;

    /// <summary>Adds the outline of <paramref name="path"/> stroked with <paramref name="style"/> to the scan converter.</summary>
    /// <param name="path">The path in user space.</param>
    /// <param name="style">The line parameters, in user space.</param>
    /// <param name="userToDevice">The transform from user space to device pixels.</param>
    public void Stroke(PathView path, in StrokeStyle style, in Matrix userToDevice)
    {
        Configure(style, userToDevice);
        ReadOnlySpan<PathVerb> verbs = path.Verbs;
        ReadOnlySpan<PathPoint> points = path.Points;
        _hasPoint = false;
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
                    _count = 0;
                    Add(User(points[p++]), smooth: false);
                    (open, hasSegments, degenerate) = (true, false, true);
                    break;
                case PathVerb.LineTo or PathVerb.QuadTo or PathVerb.CubicTo when !open:
                    // Every subpath starts with a moveto (PathView); a segment without one has nothing to start from.
                    p += verb == PathVerb.LineTo ? 1 : verb == PathVerb.QuadTo ? 2 : 3;
                    break;
                case PathVerb.LineTo:
                    degenerate &= points[p] == first;
                    hasSegments = true;
                    Add(User(points[p++]), smooth: false);
                    break;
                case PathVerb.QuadTo:
                    degenerate &= points[p] == first && points[p + 1] == first;
                    hasSegments = true;
                    Quad(User(points[p]), User(points[p + 1]));
                    p += 2;
                    break;
                case PathVerb.CubicTo:
                    degenerate &= points[p] == first && points[p + 1] == first && points[p + 2] == first;
                    hasSegments = true;
                    Cubic(User(points[p]), User(points[p + 1]), User(points[p + 2]));
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

    private static Vec User(PathPoint point) => new(Math.Clamp(point.X, -UserLimit, UserLimit), Math.Clamp(point.Y, -UserLimit, UserLimit));

    private static Vec Apply(in Matrix m, Vec v) => new((m.A * v.X) + (m.C * v.Y) + m.E, (m.B * v.X) + (m.D * v.Y) + m.F);

    /// <summary>Returns the largest factor by which the linear part of <paramref name="m"/> stretches a vector.</summary>
    private static double MaxScale(in Matrix m)
    {
        double sum = (m.A * m.A) + (m.B * m.B) + (m.C * m.C) + (m.D * m.D);
        double determinant = (m.A * m.D) - (m.B * m.C);
        double root = Math.Sqrt(Math.Max(0, (sum * sum) - (4 * determinant * determinant)));
        return Math.Sqrt((sum + root) / 2);
    }

    private static Vec Left(Vec d) => new(-d.Y, d.X);

    private static void Grow<T>(ref T[] array, int needed)
    {
        if (needed > array.Length)
        {
            Array.Resize(ref array, Math.Max(needed, array.Length * 2));
        }
    }

    /// <summary>Reads the line parameters and chooses the pen: user space with half the width, or device space with a one-pixel pen.</summary>
    private void Configure(in StrokeStyle style, in Matrix userToDevice)
    {
        _userToDevice = userToDevice;
        _cap = style.LineCap;
        _join = style.LineJoin;
        _miterLimitSquared = style.MiterLimit * style.MiterLimit;

        double scale = MaxScale(userToDevice);
        double width = style.LineWidth;
        double deviceWidth = width * scale;
        bool thinnest = !(width > 0) || (style.StrokeAdjustment && deviceWidth < 0.5);
        _snapping = (thinnest || style.StrokeAdjustment) && userToDevice.TryInvert(out _deviceToUser);
        if (thinnest)
        {
            // §8.4.3.2, §10.7.5: the thinnest line, one device pixel wide.
            _userToPen = userToDevice;
            _penToDevice = Matrix.Identity;
            _arcScale = 1;
            _radius = 0.5;
            _snapOffset = 0.5;
        }
        else
        {
            _userToPen = Matrix.Identity;
            _penToDevice = userToDevice;
            _arcScale = scale;
            _radius = width / 2;
            if (style.StrokeAdjustment && double.IsFinite(deviceWidth))
            {
                // §10.7.5: a whole number of pixels, no more than half a pixel from the requested width.
                double pixels = Math.Max(1, Math.Round(deviceWidth, MidpointRounding.AwayFromZero));
                _radius = pixels / (2 * scale);
                _snapOffset = pixels % 2 == 1 ? 0.5 : 0;
            }

            if (!(_radius * _arcScale <= CoordinateLimit))
            {
                _radius = double.IsFinite(_arcScale) && _arcScale > 0 ? CoordinateLimit / _arcScale : 0;
            }
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
            _dashPeriod = dash.Length % 2 == 1 ? 2 * sum : sum;
            double phase = style.DashPhase;
            _dashPhase = double.IsFinite(phase) && phase > 0 ? phase % _dashPeriod : 0;
            _dashBudget = MaxDashes;
        }
    }

    private void Add(Vec point, bool smooth)
    {
        if (_count == _user.Length)
        {
            Grow(ref _user, _count + 1);
            Grow(ref _pen, _count + 1);
            Grow(ref _smooth, _count + 1);
            Grow(ref _snap, _count + 1);
        }

        _user[_count] = point;
        _smooth[_count] = smooth;
        _count++;
    }

    /// <summary>Flattens a quadratic curve from the current point in user space, as finely as its device image needs.</summary>
    private void Quad(Vec control, Vec to)
    {
        Vec from = _user[_count - 1];
        Vec d0 = Apply(_userToDevice, from);
        Vec d1 = Apply(_userToDevice, control);
        Vec d2 = Apply(_userToDevice, to);
        int n = Segments(Length(d0.X - (2 * d1.X) + d2.X, d0.Y - (2 * d1.Y) + d2.Y) / (4 * Tolerance));
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n;
            double u = 1 - t;
            Add(i == n ? to : new Vec(
                (u * u * from.X) + (2 * u * t * control.X) + (t * t * to.X),
                (u * u * from.Y) + (2 * u * t * control.Y) + (t * t * to.Y)), smooth: i < n);
        }
    }

    /// <summary>Flattens a cubic curve from the current point in user space, as finely as its device image needs.</summary>
    private void Cubic(Vec control1, Vec control2, Vec to)
    {
        Vec from = _user[_count - 1];
        Vec d0 = Apply(_userToDevice, from);
        Vec d1 = Apply(_userToDevice, control1);
        Vec d2 = Apply(_userToDevice, control2);
        Vec d3 = Apply(_userToDevice, to);

        // The bound the fill path uses: n ≥ sqrt(3d / (4 · tolerance)) keeps the chord error within the tolerance.
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
            Add(i == n ? to : new Vec(
                (a * from.X) + (b * control1.X) + (c * control2.X) + (e * to.X),
                (a * from.Y) + (b * control1.Y) + (c * control2.Y) + (e * to.Y)), smooth: i < n);
        }
    }

    private static int Segments(double squared) =>
        double.IsFinite(squared) ? (int)Math.Clamp(Math.Ceiling(Math.Sqrt(squared)), 1, MaxSegments) : MaxSegments;

    private static double Length(double x, double y) => Math.Sqrt((x * x) + (y * y));

    /// <summary>Strokes the collected subpath (§8.5.3.2).</summary>
    private void Finish(bool closed, bool hasSegments, bool degenerate)
    {
        if (!hasSegments)
        {
            // A lone moveto paints nothing.
            return;
        }

        if (degenerate)
        {
            // A degenerate subpath paints a dot only with round caps.
            if (_cap == LineCap.Round)
            {
                Dot(Apply(_userToPen, _user[0]), default, hasDirection: false);
            }

            return;
        }

        if (_snapping)
        {
            Snap(closed);
        }

        Grow(ref _pen, _count);
        for (int i = 0; i < _count; i++)
        {
            _pen[i] = Pen(_user[i]);
        }

        if (_dashLength == 0)
        {
            if (closed)
            {
                StrokeClosed(_pen, _smooth, _count);
            }
            else
            {
                StrokeOpen(_pen, _smooth, _count, default, hasDirection: false);
            }

            return;
        }

        Dash(closed);
    }

    private Vec Pen(Vec user)
    {
        Vec pen = Apply(_userToPen, user);
        return new Vec(Math.Clamp(pen.X, -UserLimit, UserLimit), Math.Clamp(pen.Y, -UserLimit, UserLimit));
    }

    /// <summary>
    /// Moves the ends of horizontal and vertical device segments (not inside a curve) so that a line of the adjusted width has its
    /// edges on pixel boundaries: centres at half pixels for an odd number of pixels, on pixel boundaries for an even one (§10.7.5).
    /// </summary>
    private void Snap(bool closed)
    {
        Grow(ref _pen, _count);
        Span<byte> flags = _snap.AsSpan(0, _count);
        flags.Clear();
        for (int i = 0; i < _count; i++)
        {
            _pen[i] = Apply(_userToDevice, _user[i]);
        }

        int segments = closed ? _count : _count - 1;
        for (int s = 0; s < segments; s++)
        {
            int a = s;
            int b = s + 1 == _count ? 0 : s + 1;
            if (_smooth[a] || _smooth[b])
            {
                continue;
            }

            if (Math.Abs(_pen[a].Y - _pen[b].Y) <= 1e-6)
            {
                flags[a] |= 2;
                flags[b] |= 2;
            }

            if (Math.Abs(_pen[a].X - _pen[b].X) <= 1e-6)
            {
                flags[a] |= 1;
                flags[b] |= 1;
            }
        }

        for (int i = 0; i < _count; i++)
        {
            if (flags[i] == 0)
            {
                continue;
            }

            Vec device = _pen[i];
            double x = (flags[i] & 1) != 0 ? SnapCoordinate(device.X) : device.X;
            double y = (flags[i] & 2) != 0 ? SnapCoordinate(device.Y) : device.Y;
            _user[i] = Apply(_deviceToUser, new Vec(x, y));
        }
    }

    private double SnapCoordinate(double value) =>
        Math.Abs(value) >= CoordinateLimit ? value : _snapOffset > 0 ? Math.Floor(value) + _snapOffset : Math.Floor(value + 0.5);

    /// <summary>Cuts the subpath into dashes by user space length and strokes each (§8.4.3.6).</summary>
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
        bool headPending = closed && on;
        bool toggled = false;
        _pieceCount = 0;
        _headCount = 0;
        if (on)
        {
            AddPiece(_pen[0], smooth: false);
        }

        int segments = closed ? _count : _count - 1;
        for (int s = 0; s < segments; s++)
        {
            int a = s;
            int b = s + 1 == _count ? 0 : s + 1;
            Vec ua = _user[a];
            Vec ub = _user[b];
            Vec pa = _pen[a];
            Vec pb = _pen[b];
            double length = Length(ub.X - ua.X, ub.Y - ua.Y);
            Vec direction = Unit(pb.X - pa.X, pb.Y - pa.Y);
            double position = 0;
            while (remaining <= length - position)
            {
                position += remaining;
                double t = length > 0 ? position / length : 1;
                var point = new Vec(pa.X + ((pb.X - pa.X) * t), pa.Y + ((pb.Y - pa.Y) * t));
                if (on)
                {
                    AddPiece(point, smooth: false);
                    if (headPending && !toggled)
                    {
                        SaveHead();
                    }
                    else
                    {
                        StrokeOpen(_piece, _pieceSmooth, _pieceCount, direction, hasDirection: direction != default);
                    }
                }
                else
                {
                    _pieceCount = 0;
                    AddPiece(point, smooth: false);
                }

                toggled = true;
                on = !on;
                index++;
                if (--_dashBudget <= 0)
                {
                    // Too many dashes: the rest of the stroke is solid.
                    if (!on)
                    {
                        _pieceCount = 0;
                        AddPiece(point, smooth: false);
                        on = true;
                    }

                    remaining = double.PositiveInfinity;
                    break;
                }

                remaining = _dash[index % _dashLength];
            }

            remaining -= length - position;
            if (on)
            {
                AddPiece(pb, smooth: b != 0 && _smooth[b]);
            }
        }

        if (on)
        {
            if (headPending && !toggled)
            {
                StrokeClosed(_pen, _smooth, _count);
            }
            else if (headPending)
            {
                // §8.4.3.4: the last dash runs into the first; they are one dash, joined at the start.
                for (int i = 1; i < _headCount; i++)
                {
                    AddPiece(_head[i], _headSmooth[i]);
                }

                StrokeOpen(_piece, _pieceSmooth, _pieceCount, default, hasDirection: false);
            }
            else
            {
                StrokeOpen(_piece, _pieceSmooth, _pieceCount, default, hasDirection: false);
            }
        }
        else if (headPending)
        {
            StrokeOpen(_head, _headSmooth, _headCount, default, hasDirection: false);
        }
    }

    private void AddPiece(Vec point, bool smooth)
    {
        if (_pieceCount == _piece.Length)
        {
            Grow(ref _piece, _pieceCount + 1);
            Grow(ref _pieceSmooth, _pieceCount + 1);
        }

        _piece[_pieceCount] = point;
        _pieceSmooth[_pieceCount] = smooth;
        _pieceCount++;
    }

    private void SaveHead()
    {
        Grow(ref _head, _pieceCount);
        Grow(ref _headSmooth, _pieceCount);
        _piece.AsSpan(0, _pieceCount).CopyTo(_head);
        _pieceSmooth.AsSpan(0, _pieceCount).CopyTo(_headSmooth);
        _headCount = _pieceCount;
    }

    private static Vec Unit(double x, double y)
    {
        double length = Length(x, y);
        return length > 0 && double.IsFinite(length) ? new Vec(x / length, y / length) : default;
    }

    /// <summary>Removes consecutive equal points, keeping a point a corner if either copy was one.</summary>
    private static int Compact(Vec[] points, bool[] smooth, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        int kept = 1;
        for (int i = 1; i < count; i++)
        {
            if (points[i] == points[kept - 1])
            {
                smooth[kept - 1] &= smooth[i];
                continue;
            }

            points[kept] = points[i];
            smooth[kept] = smooth[i];
            kept++;
        }

        return kept;
    }

    /// <summary>Outlines an open polyline in pen space: left offset forward, end cap, right offset backward, start cap.</summary>
    /// <param name="points">The points; consecutive duplicates are removed in place.</param>
    /// <param name="smooth">Whether each point lies inside a flattened curve.</param>
    /// <param name="count">The number of points.</param>
    /// <param name="direction">For a polyline of zero length (a zero-length dash), the direction of the path there.</param>
    /// <param name="hasDirection">Whether <paramref name="direction"/> is known.</param>
    private void StrokeOpen(Vec[] points, bool[] smooth, int count, Vec direction, bool hasDirection)
    {
        count = Compact(points, smooth, count);
        if (count == 0)
        {
            return;
        }

        if (count == 1)
        {
            Dot(points[0], direction, hasDirection);
            return;
        }

        Vec first = Unit(points[1].X - points[0].X, points[1].Y - points[0].Y);
        LineTo(points[0] + (Left(first) * _radius));
        Vec incoming = first;
        double incomingLength = Distance(points[0], points[1]);
        for (int i = 1; i < count - 1; i++)
        {
            Vec outgoing = Unit(points[i + 1].X - points[i].X, points[i + 1].Y - points[i].Y);
            double outgoingLength = Distance(points[i], points[i + 1]);
            Join(points[i], incoming, outgoing, smooth[i], incomingLength, outgoingLength);
            (incoming, incomingLength) = (outgoing, outgoingLength);
        }

        Vec end = points[count - 1];
        LineTo(end + (Left(incoming) * _radius));
        Cap(end, incoming);

        Vec back = -incoming;
        double backLength = incomingLength;
        for (int i = count - 2; i >= 1; i--)
        {
            Vec next = -Unit(points[i].X - points[i - 1].X, points[i].Y - points[i - 1].Y);
            double nextLength = Distance(points[i - 1], points[i]);
            Join(points[i], back, next, smooth[i], backLength, nextLength);
            (back, backLength) = (next, nextLength);
        }

        LineTo(points[0] + (Left(-first) * _radius));
        Cap(points[0], -first);
        ClosePolygon();
    }

    /// <summary>Outlines a closed polyline in pen space as two loops, its left and its right offset, joined at every vertex.</summary>
    private void StrokeClosed(Vec[] points, bool[] smooth, int count)
    {
        count = Compact(points, smooth, count);
        while (count > 1 && points[count - 1] == points[0])
        {
            count--;
        }

        if (count < 2)
        {
            if (count == 1 && _cap == LineCap.Round)
            {
                Dot(points[0], default, hasDirection: false);
            }

            return;
        }

        for (int side = 0; side < 2; side++)
        {
            bool forward = side == 0;
            for (int k = 0; k < count; k++)
            {
                int i = forward ? k : count - 1 - k;
                int previous = i == 0 ? count - 1 : i - 1;
                int next = i == count - 1 ? 0 : i + 1;
                Vec into = Unit(points[i].X - points[previous].X, points[i].Y - points[previous].Y);
                Vec outOf = Unit(points[next].X - points[i].X, points[next].Y - points[i].Y);
                double intoLength = Distance(points[previous], points[i]);
                double outOfLength = Distance(points[i], points[next]);
                bool isSmooth = i != 0 && smooth[i];
                if (forward)
                {
                    Join(points[i], into, outOf, isSmooth, intoLength, outOfLength);
                }
                else
                {
                    Join(points[i], -outOf, -into, isSmooth, outOfLength, intoLength);
                }
            }

            ClosePolygon();
        }
    }

    private static double Distance(Vec a, Vec b) => Length(b.X - a.X, b.Y - a.Y);

    /// <summary>
    /// Emits the left offset around vertex <paramref name="vertex"/>, from the end of the incoming segment's offset to the start of
    /// the outgoing one's: the join on an outer corner, the meeting of the offsets on an inner one.
    /// </summary>
    private void Join(Vec vertex, Vec incoming, Vec outgoing, bool smooth, double incomingLength, double outgoingLength)
    {
        double cross = (incoming.X * outgoing.Y) - (incoming.Y * outgoing.X);
        double dot = (incoming.X * outgoing.X) + (incoming.Y * outgoing.Y);
        Vec a = Left(incoming) * _radius;
        Vec b = Left(outgoing) * _radius;
        if (cross > 0)
        {
            // Inner corner: the offsets cross at the miter point while it lies within both segments; else go through the vertex.
            double denominator = 1 + dot;
            if (denominator > 1e-9 && 2 * _radius * cross / denominator <= Math.Min(incomingLength, outgoingLength))
            {
                LineTo(vertex + ((a + b) / denominator));
                return;
            }

            LineTo(vertex + a);
            LineTo(vertex);
            LineTo(vertex + b);
            return;
        }

        LineTo(vertex + a);
        if (cross == 0 && dot > 0)
        {
            return;
        }

        if (smooth || _join == LineJoin.Round)
        {
            Arc(vertex, a, Math.Atan2(Math.Abs(cross), dot));
        }
        else if (_join == LineJoin.Miter)
        {
            // §8.4.3.5: miter length / line width = 1 / sin(φ/2), whose square is 2 / (1 + cos θ) for the turn θ.
            double denominator = 1 + dot;
            if (denominator > 0 && 2 <= _miterLimitSquared * denominator)
            {
                LineTo(vertex + ((a + b) / denominator));
            }
        }

        LineTo(vertex + b);
    }

    /// <summary>Emits the cap at <paramref name="end"/>, from its left offset point to its right one, for the direction arriving there.</summary>
    private void Cap(Vec end, Vec direction)
    {
        Vec normal = Left(direction) * _radius;
        switch (_cap)
        {
            case LineCap.Round:
                Arc(end, normal, Math.PI);
                break;
            case LineCap.ProjectingSquare:
                Vec ahead = direction * _radius;
                LineTo(end + normal + ahead);
                LineTo(end - normal + ahead);
                break;
        }

        LineTo(end - normal);
    }

    /// <summary>A zero-length piece: a disc with round caps, a square along <paramref name="direction"/> with projecting square caps.</summary>
    private void Dot(Vec center, Vec direction, bool hasDirection)
    {
        if (_cap == LineCap.Round)
        {
            var start = new Vec(_radius, 0);
            LineTo(center + start);
            Arc(center, start, 2 * Math.PI);
            ClosePolygon();
        }
        else if (_cap == LineCap.ProjectingSquare && hasDirection)
        {
            Vec along = direction * _radius;
            Vec normal = Left(direction) * _radius;
            LineTo(center - along + normal);
            LineTo(center + along + normal);
            LineTo(center + along - normal);
            LineTo(center - along - normal);
            ClosePolygon();
        }
    }

    /// <summary>
    /// Emits the points of a circular arc around <paramref name="center"/> starting at offset <paramref name="from"/> and turning by
    /// <paramref name="sweep"/> radians the way the outlines run (clockwise for a counterclockwise-positive frame), except its two ends.
    /// </summary>
    private void Arc(Vec center, Vec from, double sweep)
    {
        double deviceRadius = _radius * _arcScale;
        int n = 1;
        if (deviceRadius > Tolerance && double.IsFinite(deviceRadius))
        {
            double step = 2 * Math.Acos(1 - (Tolerance / deviceRadius));
            n = step > 0 ? (int)Math.Clamp(Math.Ceiling(sweep / step), 1, MaxSegments) : MaxSegments;
        }

        double angle = -sweep / n;
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        Vec offset = from;
        for (int i = 1; i < n; i++)
        {
            offset = new Vec((offset.X * cos) - (offset.Y * sin), (offset.X * sin) + (offset.Y * cos));
            LineTo(center + offset);
        }
    }

    /// <summary>Adds the edge from the previous outline point to <paramref name="pen"/>, mapped to device pixels.</summary>
    private void LineTo(Vec pen)
    {
        Vec device = Apply(_penToDevice, pen);
        device = new Vec(Math.Clamp(device.X, -CoordinateLimit, CoordinateLimit), Math.Clamp(device.Y, -CoordinateLimit, CoordinateLimit));
        if (_hasPoint)
        {
            _scan.AddLine(_last.X, _last.Y, device.X, device.Y);
        }
        else
        {
            _first = device;
            _hasPoint = true;
        }

        _last = device;
    }

    private void ClosePolygon()
    {
        if (_hasPoint)
        {
            _scan.AddLine(_last.X, _last.Y, _first.X, _first.Y);
            _hasPoint = false;
        }
    }

    /// <summary>A point or vector of the stroker.</summary>
    private readonly record struct Vec(double X, double Y)
    {
        public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);

        public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);

        public static Vec operator -(Vec a) => new(-a.X, -a.Y);

        public static Vec operator *(Vec a, double factor) => new(a.X * factor, a.Y * factor);

        public static Vec operator /(Vec a, double divisor) => new(a.X / divisor, a.Y / divisor);
    }
}
