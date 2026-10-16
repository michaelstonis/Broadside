using Broadside.Graphics;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;

namespace Broadside.Rendering.Direct2D;

/// <summary>Strokes (ISO 32000-2 §8.5.3.2 with the line parameters of §8.4.3 and stroke adjustment, §10.7.5).</summary>
/// <remarks>
/// <para>
/// Direct2D widens the lines and draws their caps and joins; this backend decides what it widens, the way the software
/// rasterizer's stroker does, so both agree where Direct2D's own conventions differ from PDF's. Each subpath is flattened in user
/// space (as finely as its device image needs, §10.7.2), cut into dashes by user space lengths (§8.4.3.6), and written as Direct2D
/// figures in "pen space": user space with a pen of the line width, drawn under the current transform so that a non-uniform CTM
/// gives the elliptical pen of §8.4.3.2. A miter join beyond the miter limit becomes a bevel (§8.4.3.5). The last and first dash of
/// a closed subpath are one figure when both are on (§8.4.3.4).
/// </para>
/// <para>
/// A width of 0, or a width under half a pixel with stroke adjustment on, strokes with a one-pixel pen in device space (§8.4.3.2,
/// §10.7.5). Stroke adjustment rounds the device width to whole pixels and, like the thinnest line, moves the vertices of horizontal
/// and vertical device segments so that the line's edges fall on pixel boundaries.
/// </para>
/// <para>
/// Zero-length pieces are filled here rather than left to Direct2D: a degenerate subpath is a dot only with round caps, and a
/// zero-length dash paints its cap, a disc or a square turned along the path (§8.5.3.2). A stroke dispenses at most 2^20 dashes;
/// past that the rest of it is solid.
/// </para>
/// </remarks>
internal sealed unsafe partial class Direct2DBackend
{
    /// <summary>The largest flattening error, in device pixels.</summary>
    private const double Tolerance = 0.1;

    /// <summary>The most line segments one curve is flattened into.</summary>
    private const int MaxSegments = 1000;

    /// <summary>Device widths are kept within 2^40 pixels.</summary>
    private const double DeviceLimit = 1099511627776.0;

    /// <summary>User coordinates are kept within ±10^100 so that differences of two stay finite.</summary>
    private const double UserLimit = 1e100;

    /// <summary>The most dash boundaries one stroke dispenses before drawing the rest solid.</summary>
    private const int MaxDashes = 1 << 20;

    /// <summary>The control point distance of a quarter circle drawn as one cubic Bézier curve, as a fraction of the radius.</summary>
    private const double Kappa = 0.5522847498307936;

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
    private double _dashPhase;
    private int _dashBudget;

    private Matrix _userToDevice;
    private Matrix _deviceToUser;
    private Matrix _userToPen;
    private Matrix _penToDevice;
    private double _radius;
    private LineCap _cap;
    private bool _snapping;
    private double _snapOffset;

    // The figure points handed to Direct2D, and the zero-length pieces filled after the stroke, in pen space.
    private D2D_POINT_2F[] _figure = new D2D_POINT_2F[64];
    private Dot[] _dots = new Dot[8];
    private int _dotCount;
    private ID2D1GeometrySink* _sink;
    private bool _hasStrokeFigures;

    // The stroke style of the last stroke, kept for the render while caps, join and miter limit stay the same.
    private ID2D1StrokeStyle1* _strokeStyle;
    private LineCap _styleCap;
    private LineJoin _styleJoin;
    private float _styleMiterLimit;

    /// <summary>Strokes the path with the colour (ISO 32000-2 §8.5.3.2).</summary>
    public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
    {
        PdfColorConverter converter = Converter(color);
        if (converter.PaintsNothing || path.IsEmpty || _compositing.StrokeAlpha == 0 || !ConfigurePen(style))
        {
            return;
        }

        SetColor(converter, color.Components, _compositing.StrokeAlpha);
        StrokeWith(path, style);
    }

    /// <summary>Strokes the path with the current paint (<see cref="_paint"/>), the pen configured (<see cref="ConfigurePen"/>).</summary>
    private void StrokeWith(PathView path, in StrokeStyle style)
    {
        _dotCount = 0;
        _hasStrokeFigures = false;
        ID2D1PathGeometry* geometry = OpenGeometry(out ID2D1GeometrySink* sink);
        try
        {
            try
            {
                _sink = sink;
                WriteStroke(path);
                sink->Close();
            }
            finally
            {
                _sink = null;
                sink->Release();
            }

            if (_hasStrokeFigures)
            {
                ID2D1StrokeStyle1* strokeStyle = StrokeStyleFor(style.LineCap, style.LineJoin, style.MiterLimit);
                SetWorldTransform(_penToDevice);
                _context->DrawGeometry((ID2D1Geometry*)geometry, Paint(_penToDevice), (float)(2 * _radius), (ID2D1StrokeStyle*)strokeStyle);
            }
        }
        finally
        {
            geometry->Release();
        }

        FillDots();
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

    private static int Segments(double squared) =>
        double.IsFinite(squared) ? (int)Math.Clamp(Math.Ceiling(Math.Sqrt(squared)), 1, MaxSegments) : MaxSegments;

    private static double Length(double x, double y) => Math.Sqrt((x * x) + (y * y));

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

    private static D2D1_CAP_STYLE CapStyle(LineCap cap) => cap switch
    {
        LineCap.Round => D2D1_CAP_STYLE.D2D1_CAP_STYLE_ROUND,
        LineCap.ProjectingSquare => D2D1_CAP_STYLE.D2D1_CAP_STYLE_SQUARE,
        _ => D2D1_CAP_STYLE.D2D1_CAP_STYLE_FLAT,
    };

    private static D2D1_LINE_JOIN JoinStyle(LineJoin join) => join switch
    {
        LineJoin.Round => D2D1_LINE_JOIN.D2D1_LINE_JOIN_ROUND,
        LineJoin.Bevel => D2D1_LINE_JOIN.D2D1_LINE_JOIN_BEVEL,

        // §8.4.3.5: past the miter limit the join is beveled, not clipped.
        _ => D2D1_LINE_JOIN.D2D1_LINE_JOIN_MITER_OR_BEVEL,
    };

    /// <summary>Releases the render's stroke style; stroke styles belong to the factory of the surface they were made for.</summary>
    private void EndStrokes()
    {
        if (_strokeStyle is not null)
        {
            _strokeStyle->Release();
            _strokeStyle = null;
        }
    }

    private ID2D1PathGeometry* OpenGeometry(out ID2D1GeometrySink* sink)
    {
        ID2D1PathGeometry* geometry;
        ((ID2D1Factory*)_factory)->CreatePathGeometry(&geometry);
        try
        {
            ID2D1GeometrySink* opened;
            geometry->Open(&opened);
            opened->SetFillMode(D2D1_FILL_MODE.D2D1_FILL_MODE_WINDING);
            sink = opened;
        }
        catch
        {
            geometry->Release();
            throw;
        }

        return geometry;
    }

    private ID2D1StrokeStyle1* StrokeStyleFor(LineCap cap, LineJoin join, double miterLimit)
    {
        float limit = miterLimit >= 1 ? (float)Math.Min(miterLimit, float.MaxValue) : 1;
        if (_strokeStyle is not null && cap == _styleCap && join == _styleJoin && limit == _styleMiterLimit)
        {
            return _strokeStyle;
        }

        EndStrokes();
        var properties = new D2D1_STROKE_STYLE_PROPERTIES1
        {
            startCap = CapStyle(cap),
            endCap = CapStyle(cap),
            dashCap = CapStyle(cap),
            lineJoin = JoinStyle(join),
            miterLimit = limit,

            // Dashes are cut here, by user space length, so Direct2D strokes every figure solid.
            dashStyle = D2D1_DASH_STYLE.D2D1_DASH_STYLE_SOLID,
            dashOffset = 0,
            transformType = D2D1_STROKE_TRANSFORM_TYPE.D2D1_STROKE_TRANSFORM_TYPE_NORMAL,
        };
        ID2D1StrokeStyle1* strokeStyle;
        _factory->CreateStrokeStyle(&properties, null, 0, &strokeStyle);
        _strokeStyle = strokeStyle;
        (_styleCap, _styleJoin, _styleMiterLimit) = (cap, join, limit);
        return strokeStyle;
    }

    /// <summary>Reads the line parameters and chooses the pen: user space with half the width, or device space with a one-pixel pen.</summary>
    /// <returns>Whether the pen paints anything.</returns>
    private bool ConfigurePen(in StrokeStyle style)
    {
        _userToDevice = _toDevice;
        _cap = style.LineCap;

        double scale = MaxScale(_userToDevice);
        double width = style.LineWidth;
        double deviceWidth = width * scale;
        bool thinnest = !(width > 0) || (style.StrokeAdjustment && deviceWidth < 0.5);
        _snapping = (thinnest || style.StrokeAdjustment) && _userToDevice.TryInvert(out _deviceToUser);
        if (thinnest)
        {
            // §8.4.3.2, §10.7.5: the thinnest line, one device pixel wide.
            _userToPen = _userToDevice;
            _penToDevice = Matrix.Identity;
            _radius = 0.5;
            _snapOffset = 0.5;
        }
        else
        {
            _userToPen = Matrix.Identity;
            _penToDevice = _userToDevice;
            _radius = width / 2;
            if (style.StrokeAdjustment && double.IsFinite(deviceWidth))
            {
                // §10.7.5: a whole number of pixels, no more than half a pixel from the requested width.
                double pixels = Math.Max(1, Math.Round(deviceWidth, MidpointRounding.AwayFromZero));
                _radius = pixels / (2 * scale);
                _snapOffset = pixels % 2 == 1 ? 0.5 : 0;
            }

            if (!(_radius * scale <= DeviceLimit))
            {
                _radius = double.IsFinite(scale) && scale > 0 ? DeviceLimit / scale : 0;
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
            double period = dash.Length % 2 == 1 ? 2 * sum : sum;
            double phase = style.DashPhase;
            _dashPhase = double.IsFinite(phase) && phase > 0 ? phase % period : 0;
            _dashBudget = MaxDashes;
        }

        return _radius > 0 && double.IsFinite(_radius);
    }

    /// <summary>Writes the figures of every subpath of <paramref name="path"/> into <see cref="_sink"/>.</summary>
    private void WriteStroke(PathView path)
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

        // n ≥ sqrt(3d / (4 · tolerance)) keeps the chord error within the tolerance.
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
                AddDot(Apply(_userToPen, _user[0]), default, hasDirection: false);
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
            Vec pen = Apply(_userToPen, _user[i]);
            _pen[i] = new Vec(Math.Clamp(pen.X, -UserLimit, UserLimit), Math.Clamp(pen.Y, -UserLimit, UserLimit));
        }

        if (_dashLength == 0)
        {
            if (closed)
            {
                EmitClosed(_pen, _smooth, _count);
            }
            else
            {
                EmitOpen(_pen, _smooth, _count, default, hasDirection: false);
            }

            return;
        }

        Dash(closed);
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
        Math.Abs(value) >= DeviceLimit ? value : _snapOffset > 0 ? Math.Floor(value) + _snapOffset : Math.Floor(value + 0.5);

    /// <summary>Cuts the subpath into dashes by user space length and writes each (§8.4.3.6).</summary>
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
                        EmitOpen(_piece, _pieceSmooth, _pieceCount, direction, hasDirection: direction != default);
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
                EmitClosed(_pen, _smooth, _count);
            }
            else if (headPending)
            {
                // §8.4.3.4: the last dash runs into the first; they are one dash, joined at the start.
                for (int i = 1; i < _headCount; i++)
                {
                    AddPiece(_head[i], _headSmooth[i]);
                }

                EmitOpen(_piece, _pieceSmooth, _pieceCount, default, hasDirection: false);
            }
            else
            {
                EmitOpen(_piece, _pieceSmooth, _pieceCount, default, hasDirection: false);
            }
        }
        else if (headPending)
        {
            EmitOpen(_head, _headSmooth, _headCount, default, hasDirection: false);
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

    /// <summary>Writes an open polyline in pen space as an open figure; one of zero length is a dot (a zero-length dash).</summary>
    /// <param name="points">The points; consecutive duplicates are removed in place.</param>
    /// <param name="smooth">Whether each point lies inside a flattened curve.</param>
    /// <param name="count">The number of points.</param>
    /// <param name="direction">For a polyline of zero length, the direction of the path there.</param>
    /// <param name="hasDirection">Whether <paramref name="direction"/> is known.</param>
    private void EmitOpen(Vec[] points, bool[] smooth, int count, Vec direction, bool hasDirection)
    {
        count = Compact(points, smooth, count);
        if (count == 0)
        {
            return;
        }

        if (count == 1)
        {
            AddDot(points[0], direction, hasDirection);
            return;
        }

        WriteFigure(points, count, closed: false);
    }

    /// <summary>Writes a closed polyline in pen space as a closed figure, joined where it closes.</summary>
    private void EmitClosed(Vec[] points, bool[] smooth, int count)
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
                AddDot(points[0], default, hasDirection: false);
            }

            return;
        }

        WriteFigure(points, count, closed: true);
    }

    private void WriteFigure(Vec[] points, int count, bool closed)
    {
        Grow(ref _figure, count - 1);
        for (int i = 1; i < count; i++)
        {
            _figure[i - 1] = Interop.Point(points[i].X, points[i].Y);
        }

        _sink->BeginFigure(Interop.Point(points[0].X, points[0].Y), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_HOLLOW);
        fixed (D2D_POINT_2F* figure = _figure)
        {
            _sink->AddLines(figure, (uint)(count - 1));
        }

        _sink->EndFigure(closed ? D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED : D2D1_FIGURE_END.D2D1_FIGURE_END_OPEN);
        _hasStrokeFigures = true;
    }

    /// <summary>Records a zero-length piece: a disc with round caps, a square along <paramref name="direction"/> with projecting square caps.</summary>
    private void AddDot(Vec center, Vec direction, bool hasDirection)
    {
        if (_cap != LineCap.Round && !(_cap == LineCap.ProjectingSquare && hasDirection))
        {
            return;
        }

        if (_dotCount == _dots.Length)
        {
            Array.Resize(ref _dots, _dotCount * 2);
        }

        _dots[_dotCount++] = new Dot(center, direction);
    }

    /// <summary>Fills the zero-length pieces of the stroke just drawn, in pen space.</summary>
    private void FillDots()
    {
        if (_dotCount == 0)
        {
            return;
        }

        ID2D1PathGeometry* geometry = OpenGeometry(out ID2D1GeometrySink* sink);
        try
        {
            try
            {
                double r = _radius;
                for (int i = 0; i < _dotCount; i++)
                {
                    Dot dot = _dots[i];
                    Vec c = dot.Center;
                    if (_cap == LineCap.Round)
                    {
                        sink->BeginFigure(Interop.Point(c.X + r, c.Y), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
                        double k = Kappa * r;
                        Quarter(sink, c.X + r, c.Y + k, c.X + k, c.Y + r, c.X, c.Y + r);
                        Quarter(sink, c.X - k, c.Y + r, c.X - r, c.Y + k, c.X - r, c.Y);
                        Quarter(sink, c.X - r, c.Y - k, c.X - k, c.Y - r, c.X, c.Y - r);
                        Quarter(sink, c.X + k, c.Y - r, c.X + r, c.Y - k, c.X + r, c.Y);
                    }
                    else
                    {
                        Vec along = dot.Direction * r;
                        Vec normal = Left(dot.Direction) * r;
                        Vec start = c - along + normal;
                        sink->BeginFigure(Interop.Point(start.X, start.Y), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
                        Vec b = c + along + normal;
                        Vec e = c + along - normal;
                        Vec d = c - along - normal;
                        sink->AddLine(Interop.Point(b.X, b.Y));
                        sink->AddLine(Interop.Point(e.X, e.Y));
                        sink->AddLine(Interop.Point(d.X, d.Y));
                    }

                    sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
                }

                sink->Close();
            }
            finally
            {
                sink->Release();
            }

            SetWorldTransform(_penToDevice);
            _context->FillGeometry((ID2D1Geometry*)geometry, Paint(_penToDevice), null);
        }
        finally
        {
            geometry->Release();
            _dotCount = 0;
        }
    }

    private static void Quarter(ID2D1GeometrySink* sink, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        var segment = new D2D1_BEZIER_SEGMENT { point1 = Interop.Point(x1, y1), point2 = Interop.Point(x2, y2), point3 = Interop.Point(x3, y3) };
        sink->AddBezier(&segment);
    }

    /// <summary>A zero-length piece of a stroke in pen space, and the direction of the path there (zero when unknown).</summary>
    private readonly record struct Dot(Vec Center, Vec Direction);

    /// <summary>A point or vector in user, pen or device space.</summary>
    private readonly record struct Vec(double X, double Y)
    {
        public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);

        public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);

        public static Vec operator *(Vec a, double factor) => new(a.X * factor, a.Y * factor);
    }
}
