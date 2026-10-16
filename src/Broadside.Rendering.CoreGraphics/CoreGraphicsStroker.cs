using System.Runtime.InteropServices;
using Broadside.Graphics;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>
/// Prepares a stroked path for CoreGraphics: flattens it, applies stroke adjustment and the dash pattern the way PDF defines them,
/// and hands CoreGraphics the resulting polylines to outline with the line width, caps, joins and miter limit.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.5.3.2 with the line parameters of §8.4.3. CoreGraphics' own dasher differs from PDF's where the software rasterizer
/// (the reference) follows the specification: it ends a closed subpath's last dash with a cap instead of joining it to the first dash
/// that starts there (§8.4.3.4), and drops a zero-length dash that falls on the end of an open subpath (§8.5.3.2: its caps are
/// painted). So dashes are cut here, by user space length (§8.4.3.6), restarted per subpath, at most 2^20 per stroke (then the rest
/// is solid), and each dash reaches CoreGraphics as an open polyline, or a closed one when one dash covers a whole closed subpath.
/// A zero-length dash becomes a tiny segment along the path, so a projecting square cap is oriented along it.
/// </para>
/// <para>
/// Each subpath is flattened in user space as finely as its device image needs (§10.7.2: a tenth of a pixel). The pen is CoreGraphics'
/// line width in user space under the CTM, so a non-uniform CTM gives an elliptical pen (§8.4.3.2). A width of 0, or one under half a
/// pixel with stroke adjustment on, strokes one device unit wide in the context's space instead (§8.4.3.2, §10.7.5). Stroke
/// adjustment (§10.7.5) rounds the device width to whole pixels and moves the vertices of horizontal and vertical device segments
/// (not inside a curve) so that the line's edges fall on pixel boundaries, as the rasterizer does. A degenerate subpath paints a dot
/// only with round caps; a lone moveto paints nothing.
/// </para>
/// <para>Hot path (CLAUDE.md): working arrays grow by doubling and are kept, so a warm stroker allocates nothing per subpath, segment or dash.</para>
/// </remarks>
internal sealed class CoreGraphicsStroker
{
    /// <summary>The largest flattening error, in device pixels.</summary>
    private const double Tolerance = 0.1;

    /// <summary>The most line segments one curve is flattened into.</summary>
    private const int MaxSegments = 1000;

    /// <summary>The most dash boundaries one stroke dispenses before drawing the rest solid.</summary>
    private const int MaxDashes = 1 << 20;

    /// <summary>The length, in device pixels, of the segment that stands for a zero-length dash.</summary>
    private const double ZeroDashLength = 1e-4;

    // The subpath being collected: user space points (snapped when adjusting), whether each lies inside a flattened curve, and
    // the same points in the space CoreGraphics receives them in (user space, or the context's space for the thinnest line).
    private PathPoint[] _user = new PathPoint[64];
    private bool[] _smooth = new bool[64];
    private PathPoint[] _pen = new PathPoint[64];
    private byte[] _snap = new byte[64];
    private int _count;

    // The dash being collected and, for a closed subpath that starts on a dash, that first dash, kept to join the last one.
    private PathPoint[] _piece = new PathPoint[64];
    private int _pieceCount;
    private PathPoint[] _head = new PathPoint[64];
    private int _headCount;

    private double[] _dash = new double[8];
    private int _dashLength;
    private double _dashPhase;
    private int _dashBudget;

    private CGContext? _context;
    private Matrix _userToDevice;
    private Matrix _deviceToUser;
    private bool _thinnest;
    private bool _snapping;
    private double _snapOffset;
    private double _zeroDash;
    private LineCap _cap;

    /// <summary>
    /// Strokes <paramref name="path"/> onto <paramref name="context"/> with <paramref name="style"/>; the context's stroke colour is
    /// already set, and its graphics state is as it was when the call returns.
    /// </summary>
    /// <param name="context">The context, its user space the space <paramref name="userToDevice"/> maps to.</param>
    /// <param name="path">The path in user space.</param>
    /// <param name="style">The line parameters, in user space.</param>
    /// <param name="userToDevice">The CTM followed by the device transform: user space to the context's user space.</param>
    public void Stroke(CGContext context, PathView path, in StrokeStyle style, in Matrix userToDevice) => Run(context, path, style, userToDevice, outline: false);

    /// <summary>
    /// Leaves the outline of the area the stroke covers as the context's current path (in the context's space, after the stroke's
    /// transform is restored), for a clip; false when the stroke paints nothing.
    /// </summary>
    /// <param name="context">The context, its user space the space <paramref name="userToDevice"/> maps to.</param>
    /// <param name="path">The path in user space.</param>
    /// <param name="style">The line parameters, in user space.</param>
    /// <param name="userToDevice">The CTM followed by the device transform: user space to the context's user space.</param>
    /// <returns>Whether an outline was left.</returns>
    public bool Outline(CGContext context, PathView path, in StrokeStyle style, in Matrix userToDevice) =>
        Run(context, path, style, userToDevice, outline: true) && !context.IsPathEmpty();

    private bool Run(CGContext context, PathView path, in StrokeStyle style, in Matrix userToDevice, bool outline)
    {
        if (!Configure(style, userToDevice))
        {
            return false;
        }

        _context = context;
        context.SaveState();
        try
        {
            context.SetLineCap(style.LineCap switch
            {
                LineCap.Round => CGLineCap.Round,
                LineCap.ProjectingSquare => CGLineCap.Square,
                _ => CGLineCap.Butt,
            });
            context.SetLineJoin(style.LineJoin switch
            {
                LineJoin.Round => CGLineJoin.Round,
                LineJoin.Bevel => CGLineJoin.Bevel,
                _ => CGLineJoin.Miter,
            });
            context.SetMiterLimit((nfloat)style.MiterLimit);
            context.SetLineDash(0, null);
            if (_thinnest)
            {
                // §8.4.3.2, §10.7.5: the thinnest line, one device pixel wide; points are mapped here.
                context.SetLineWidth(1);
            }
            else
            {
                Matrix m = userToDevice;
                context.ConcatCTM(new CGAffineTransform((nfloat)m.A, (nfloat)m.B, (nfloat)m.C, (nfloat)m.D, (nfloat)m.E, (nfloat)m.F));
                double scale = LargestStretch(m);
                double width = style.LineWidth;
                if (style.StrokeAdjustment)
                {
                    // §10.7.5: a whole number of pixels, no more than half a pixel from the requested width.
                    double pixels = Math.Max(1, Math.Round(width * scale, MidpointRounding.AwayFromZero));
                    width = pixels / scale;
                    _snapOffset = pixels % 2 == 1 ? 0.5 : 0;
                }

                context.SetLineWidth((nfloat)width);
            }

            context.BeginPath();
            Walk(path);
            if (outline)
            {
                // The current path is not part of the graphics state and is kept in device space, so it survives the restore.
                context.ReplacePathWithStrokedPath();
            }
            else
            {
                context.StrokePath();
            }
        }
        finally
        {
            context.RestoreState();
            _context = null;
        }

        return true;
    }

    /// <summary>The largest factor by which the linear part of <paramref name="m"/> stretches a vector: its largest singular value.</summary>
    internal static double LargestStretch(in Matrix m)
    {
        double sum = (m.A * m.A) + (m.B * m.B) + (m.C * m.C) + (m.D * m.D);
        double determinant = m.Determinant;
        double root = Math.Sqrt(Math.Max(0, (sum * sum) - (4 * determinant * determinant)));
        return Math.Sqrt((sum + root) / 2);
    }

    private static void Grow<T>(ref T[] array, int needed)
    {
        if (needed > array.Length)
        {
            Array.Resize(ref array, Math.Max(needed, array.Length * 2));
        }
    }

    private static double Length(double x, double y) => Math.Sqrt((x * x) + (y * y));

    private static PathPoint Apply(in Matrix m, PathPoint v) => new((m.A * v.X) + (m.C * v.Y) + m.E, (m.B * v.X) + (m.D * v.Y) + m.F);

    private static int Segments(double squared) =>
        double.IsFinite(squared) ? (int)Math.Clamp(Math.Ceiling(Math.Sqrt(squared)), 1, MaxSegments) : MaxSegments;

    /// <summary>Reads the line parameters; false when the stroke paints nothing (a transform that collapses the plane).</summary>
    private bool Configure(in StrokeStyle style, in Matrix userToDevice)
    {
        double determinant = userToDevice.Determinant;
        if (determinant == 0 || !double.IsFinite(determinant) || !userToDevice.TryInvert(out _deviceToUser))
        {
            return false;
        }

        _userToDevice = userToDevice;
        _cap = style.LineCap;
        double scale = LargestStretch(userToDevice);
        double deviceWidth = style.LineWidth * scale;
        _thinnest = !(style.LineWidth > 0) || (style.StrokeAdjustment && deviceWidth < 0.5);
        _snapping = _thinnest || style.StrokeAdjustment;
        _snapOffset = 0.5;
        _zeroDash = _thinnest ? ZeroDashLength : ZeroDashLength / scale;

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

        return true;
    }

    /// <summary>Collects each subpath of <paramref name="path"/> as a polyline and finishes it (§8.5.3.2).</summary>
    private void Walk(PathView path)
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
                    Add(points[p++], smooth: false);
                    (open, hasSegments, degenerate) = (true, false, true);
                    break;
                case PathVerb.LineTo or PathVerb.QuadTo or PathVerb.CubicTo when !open:
                    // Every subpath starts with a moveto (PathView); a segment without one has nothing to start from.
                    p += verb == PathVerb.LineTo ? 1 : verb == PathVerb.QuadTo ? 2 : 3;
                    break;
                case PathVerb.LineTo:
                    degenerate &= points[p] == first;
                    hasSegments = true;
                    Add(points[p++], smooth: false);
                    break;
                case PathVerb.QuadTo:
                    degenerate &= points[p] == first && points[p + 1] == first;
                    hasSegments = true;
                    Quad(points[p], points[p + 1]);
                    p += 2;
                    break;
                case PathVerb.CubicTo:
                    degenerate &= points[p] == first && points[p + 1] == first && points[p + 2] == first;
                    hasSegments = true;
                    Cubic(points[p], points[p + 1], points[p + 2]);
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

    private void Add(PathPoint point, bool smooth)
    {
        if (_count == _user.Length)
        {
            Grow(ref _user, _count + 1);
            Grow(ref _smooth, _count + 1);
        }

        _user[_count] = point;
        _smooth[_count] = smooth;
        _count++;
    }

    /// <summary>Flattens a quadratic curve from the current point in user space, as finely as its device image needs.</summary>
    private void Quad(PathPoint control, PathPoint to)
    {
        PathPoint from = _user[_count - 1];
        PathPoint d0 = Apply(_userToDevice, from);
        PathPoint d1 = Apply(_userToDevice, control);
        PathPoint d2 = Apply(_userToDevice, to);
        int n = Segments(Length(d0.X - (2 * d1.X) + d2.X, d0.Y - (2 * d1.Y) + d2.Y) / (4 * Tolerance));
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n;
            double u = 1 - t;
            Add(i == n ? to : new PathPoint(
                (u * u * from.X) + (2 * u * t * control.X) + (t * t * to.X),
                (u * u * from.Y) + (2 * u * t * control.Y) + (t * t * to.Y)), smooth: i < n);
        }
    }

    /// <summary>Flattens a cubic curve from the current point in user space, as finely as its device image needs.</summary>
    private void Cubic(PathPoint control1, PathPoint control2, PathPoint to)
    {
        PathPoint from = _user[_count - 1];
        PathPoint d0 = Apply(_userToDevice, from);
        PathPoint d1 = Apply(_userToDevice, control1);
        PathPoint d2 = Apply(_userToDevice, control2);
        PathPoint d3 = Apply(_userToDevice, to);

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
            Add(i == n ? to : new PathPoint(
                (a * from.X) + (b * control1.X) + (c * control2.X) + (e * to.X),
                (a * from.Y) + (b * control1.Y) + (c * control2.Y) + (e * to.Y)), smooth: i < n);
        }
    }

    /// <summary>Hands the collected subpath to CoreGraphics (§8.5.3.2).</summary>
    private void Finish(bool closed, bool hasSegments, bool degenerate)
    {
        if (!hasSegments)
        {
            // A lone moveto paints nothing.
            return;
        }

        if (degenerate)
        {
            // A degenerate subpath paints a dot only with round caps; CoreGraphics draws a zero-length segment's round cap.
            if (_cap == LineCap.Round)
            {
                PathPoint dot = Pen(_user[0]);
                _context!.MoveTo((nfloat)dot.X, (nfloat)dot.Y);
                _context.AddLineToPoint((nfloat)dot.X, (nfloat)dot.Y);
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
            Emit(_pen, _count, closed, default);
            return;
        }

        Dash(closed);
    }

    private PathPoint Pen(PathPoint user) => _thinnest ? CoreGraphicsBackend.Map(_userToDevice, user) : user;

    /// <summary>
    /// Moves the ends of horizontal and vertical device segments (not inside a curve) so that a line of the adjusted width has its
    /// edges on pixel boundaries: centres at half pixels for an odd number of pixels, on pixel boundaries for an even one (§10.7.5).
    /// </summary>
    private void Snap(bool closed)
    {
        Grow(ref _pen, _count);
        Grow(ref _snap, _count);
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

            PathPoint device = _pen[i];
            double x = (flags[i] & 1) != 0 ? SnapCoordinate(device.X) : device.X;
            double y = (flags[i] & 2) != 0 ? SnapCoordinate(device.Y) : device.Y;
            _user[i] = Apply(_deviceToUser, new PathPoint(x, y));
        }
    }

    private double SnapCoordinate(double value) => _snapOffset > 0 ? Math.Floor(value) + _snapOffset : Math.Floor(value + 0.5);

    /// <summary>Cuts the subpath into dashes by user space length and hands each to CoreGraphics (§8.4.3.6).</summary>
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
            AddPiece(_pen[0]);
        }

        PathPoint direction = default;
        int segments = closed ? _count : _count - 1;
        for (int s = 0; s < segments; s++)
        {
            int a = s;
            int b = s + 1 == _count ? 0 : s + 1;
            PathPoint ua = _user[a];
            PathPoint ub = _user[b];
            PathPoint pa = _pen[a];
            PathPoint pb = _pen[b];
            double length = Length(ub.X - ua.X, ub.Y - ua.Y);
            if (length > 0)
            {
                direction = Unit(pb.X - pa.X, pb.Y - pa.Y);
            }

            double position = 0;
            while (remaining <= length - position)
            {
                position += remaining;
                double t = length > 0 ? position / length : 1;
                var point = new PathPoint(pa.X + ((pb.X - pa.X) * t), pa.Y + ((pb.Y - pa.Y) * t));
                if (on)
                {
                    AddPiece(point);
                    if (headPending && !toggled)
                    {
                        SaveHead();
                    }
                    else
                    {
                        Emit(_piece, _pieceCount, closed: false, direction);
                    }
                }
                else
                {
                    _pieceCount = 0;
                    AddPiece(point);
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
                        AddPiece(point);
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
                AddPiece(pb);
            }
        }

        if (on)
        {
            if (headPending && !toggled)
            {
                Emit(_pen, _count, closed: true, default);
            }
            else if (headPending)
            {
                // §8.4.3.4: the last dash runs into the first; they are one dash, joined at the start.
                for (int i = 1; i < _headCount; i++)
                {
                    AddPiece(_head[i]);
                }

                Emit(_piece, _pieceCount, closed: false, direction);
            }
            else
            {
                Emit(_piece, _pieceCount, closed: false, direction);
            }
        }
        else if (headPending)
        {
            Emit(_head, _headCount, closed: false, direction);
        }
    }

    private void AddPiece(PathPoint point)
    {
        if (_pieceCount == _piece.Length)
        {
            Grow(ref _piece, _pieceCount + 1);
        }

        _piece[_pieceCount++] = point;
    }

    private void SaveHead()
    {
        Grow(ref _head, _pieceCount);
        _piece.AsSpan(0, _pieceCount).CopyTo(_head);
        _headCount = _pieceCount;
    }

    private static PathPoint Unit(double x, double y)
    {
        double length = Length(x, y);
        return length > 0 && double.IsFinite(length) ? new PathPoint(x / length, y / length) : default;
    }

    /// <summary>
    /// Adds a polyline to the context's path, skipping repeated points. One of zero length (a zero-length dash) paints only its caps
    /// (§8.5.3.2): nothing with butt caps, otherwise a tiny segment along <paramref name="direction"/>, the path's direction there.
    /// </summary>
    private void Emit(PathPoint[] points, int count, bool closed, PathPoint direction)
    {
        if (count == 0)
        {
            return;
        }

        CGContext context = _context!;
        PathPoint start = points[0];
        int distinct = 1;
        PathPoint last = start;
        for (int i = 1; i < count; i++)
        {
            if (points[i] != last)
            {
                distinct++;
                last = points[i];
            }
        }

        if (distinct == 1)
        {
            if (_cap == LineCap.Butt)
            {
                return;
            }

            context.MoveTo((nfloat)start.X, (nfloat)start.Y);
            context.AddLineToPoint((nfloat)(start.X + (direction.X * _zeroDash)), (nfloat)(start.Y + (direction.Y * _zeroDash)));
            return;
        }

        context.MoveTo((nfloat)start.X, (nfloat)start.Y);
        last = start;
        for (int i = 1; i < count; i++)
        {
            if (points[i] != last)
            {
                last = points[i];
                context.AddLineToPoint((nfloat)last.X, (nfloat)last.Y);
            }
        }

        if (closed)
        {
            context.ClosePath();
        }
    }
}
