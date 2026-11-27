namespace Broadside.Graphics.Svg;

/// <summary>The kind of an <see cref="SvgPath"/> segment.</summary>
internal enum SvgSegmentKind : byte
{
    /// <summary>Begins a subpath at the end point.</summary>
    Move,

    /// <summary>A straight line to the end point.</summary>
    Line,

    /// <summary>A cubic Bézier curve through two control points to the end point.</summary>
    Cubic,

    /// <summary>Closes the subpath.</summary>
    Close,
}

/// <summary>One segment: its kind, two control points (cubic only) and its end point, in absolute user coordinates.</summary>
internal readonly record struct SvgSegment(SvgSegmentKind Kind, double X1, double Y1, double X2, double Y2, double X, double Y);

/// <summary>
/// A path in absolute coordinates made of moves, lines, cubic curves and closes: what every SVG shape becomes before it is written
/// with the PDF path operators. Quadratic curves and elliptical arcs are converted to cubic curves as they are added.
/// </summary>
/// <remarks>ISO 32000-2 §8.5.2 (path construction) and SVG 1.1 §8.3 (path data), Appendix F.6 (elliptical arc implementation notes).</remarks>
internal sealed class SvgPath
{
    private const double Kappa = 0.5522847498307936;

    private readonly List<SvgSegment> _segments = [];
    private double _startX;
    private double _startY;

    /// <summary>Gets the segments.</summary>
    public IReadOnlyList<SvgSegment> Segments => _segments;

    /// <summary>Gets a value indicating whether the path has at least one segment that draws.</summary>
    public bool IsEmpty => _segments.Count == 0 || _segments.TrueForAll(static segment => segment.Kind == SvgSegmentKind.Move);

    /// <summary>Gets the current point.</summary>
    public double CurrentX { get; private set; }

    /// <summary>Gets the current point.</summary>
    public double CurrentY { get; private set; }

    /// <summary>Gets a value indicating whether a subpath has been begun.</summary>
    public bool HasCurrentPoint { get; private set; }

    public void MoveTo(double x, double y)
    {
        _segments.Add(new SvgSegment(SvgSegmentKind.Move, 0, 0, 0, 0, x, y));
        _startX = CurrentX = x;
        _startY = CurrentY = y;
        HasCurrentPoint = true;
    }

    public void LineTo(double x, double y)
    {
        _segments.Add(new SvgSegment(SvgSegmentKind.Line, 0, 0, 0, 0, x, y));
        CurrentX = x;
        CurrentY = y;
    }

    public void CubicTo(double x1, double y1, double x2, double y2, double x, double y)
    {
        _segments.Add(new SvgSegment(SvgSegmentKind.Cubic, x1, y1, x2, y2, x, y));
        CurrentX = x;
        CurrentY = y;
    }

    /// <summary>Adds a quadratic curve as the cubic curve with the same shape.</summary>
    public void QuadraticTo(double x1, double y1, double x, double y) => CubicTo(
        CurrentX + (2.0 / 3 * (x1 - CurrentX)),
        CurrentY + (2.0 / 3 * (y1 - CurrentY)),
        x + (2.0 / 3 * (x1 - x)),
        y + (2.0 / 3 * (y1 - y)),
        x,
        y);

    public void Close()
    {
        _segments.Add(new SvgSegment(SvgSegmentKind.Close, 0, 0, 0, 0, _startX, _startY));
        CurrentX = _startX;
        CurrentY = _startY;
    }

    /// <summary>Adds an elliptical arc from the current point to (<paramref name="x"/>, <paramref name="y"/>) as cubic curves of at most 90° each.</summary>
    /// <remarks>SVG 1.1 Appendix F.6.5 (conversion from endpoint to centre parameterization) and F.6.6 (out-of-range radii).</remarks>
    public void ArcTo(double rx, double ry, double rotation, bool largeArc, bool sweep, double x, double y)
    {
        double x0 = CurrentX;
        double y0 = CurrentY;
        if (x0 == x && y0 == y)
        {
            return;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx == 0 || ry == 0)
        {
            LineTo(x, y);
            return;
        }

        double phi = rotation * Math.PI / 180;
        double cos = Math.Cos(phi);
        double sin = Math.Sin(phi);
        double dx = (x0 - x) / 2;
        double dy = (y0 - y) / 2;
        double x1p = (cos * dx) + (sin * dy);
        double y1p = (-sin * dx) + (cos * dy);
        double lambda = ((x1p * x1p) / (rx * rx)) + ((y1p * y1p) / (ry * ry));
        if (lambda > 1)
        {
            double root = Math.Sqrt(lambda);
            rx *= root;
            ry *= root;
        }

        double numerator = (rx * rx * ry * ry) - (rx * rx * y1p * y1p) - (ry * ry * x1p * x1p);
        double denominator = (rx * rx * y1p * y1p) + (ry * ry * x1p * x1p);
        double coefficient = denominator == 0 ? 0 : Math.Sqrt(Math.Max(0, numerator / denominator));
        if (largeArc == sweep)
        {
            coefficient = -coefficient;
        }

        double cxp = coefficient * rx * y1p / ry;
        double cyp = -coefficient * ry * x1p / rx;
        double cx = (cos * cxp) - (sin * cyp) + ((x0 + x) / 2);
        double cy = (sin * cxp) + (cos * cyp) + ((y0 + y) / 2);
        double theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        double delta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && delta > 0)
        {
            delta -= 2 * Math.PI;
        }
        else if (sweep && delta < 0)
        {
            delta += 2 * Math.PI;
        }

        int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
        double step = delta / count;
        double t = 4.0 / 3 * Math.Tan(step / 4);
        double angle = theta1;
        for (int i = 0; i < count; i++)
        {
            double cos1 = Math.Cos(angle);
            double sin1 = Math.Sin(angle);
            double cos2 = Math.Cos(angle + step);
            double sin2 = Math.Sin(angle + step);
            (double ax, double ay) = Map(cos1 - (t * sin1), sin1 + (t * cos1));
            (double bx, double by) = Map(cos2 + (t * sin2), sin2 - (t * cos2));
            (double ex, double ey) = i == count - 1 ? (x, y) : Map(cos2, sin2);
            CubicTo(ax, ay, bx, by, ex, ey);
            angle += step;
        }

        (double, double) Map(double ux, double uy) =>
            ((cos * rx * ux) - (sin * ry * uy) + cx, (sin * rx * ux) + (cos * ry * uy) + cy);
    }

    /// <summary>Adds an ellipse as four cubic curves, clockwise in SVG coordinates from its rightmost point.</summary>
    public void Ellipse(double cx, double cy, double rx, double ry)
    {
        double kx = rx * Kappa;
        double ky = ry * Kappa;
        MoveTo(cx + rx, cy);
        CubicTo(cx + rx, cy + ky, cx + kx, cy + ry, cx, cy + ry);
        CubicTo(cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy);
        CubicTo(cx - rx, cy - ky, cx - kx, cy - ry, cx, cy - ry);
        CubicTo(cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy);
        Close();
    }

    /// <summary>Adds a rectangle, with corners rounded by <paramref name="rx"/> and <paramref name="ry"/> when they are positive.</summary>
    public void Rectangle(double x, double y, double width, double height, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0)
        {
            MoveTo(x, y);
            LineTo(x + width, y);
            LineTo(x + width, y + height);
            LineTo(x, y + height);
            Close();
            return;
        }

        double kx = rx * Kappa;
        double ky = ry * Kappa;
        double right = x + width;
        double bottom = y + height;
        MoveTo(x + rx, y);
        LineTo(right - rx, y);
        CubicTo(right - rx + kx, y, right, y + ry - ky, right, y + ry);
        LineTo(right, bottom - ry);
        CubicTo(right, bottom - ry + ky, right - rx + kx, bottom, right - rx, bottom);
        LineTo(x + rx, bottom);
        CubicTo(x + rx - kx, bottom, x, bottom - ry + ky, x, bottom - ry);
        LineTo(x, y + ry);
        CubicTo(x, y + ry - ky, x + rx - kx, y, x + rx, y);
        Close();
    }

    /// <summary>Returns the exact bounding box of the path (curve extremes, not control points); empty for a path without points.</summary>
    public SvgRect Bounds(Matrix matrix)
    {
        double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        PathPoint previous = default;
        foreach (SvgSegment segment in _segments)
        {
            PathPoint end = matrix.Transform(segment.X, segment.Y);
            Include(end);
            if (segment.Kind == SvgSegmentKind.Cubic)
            {
                PathPoint c1 = matrix.Transform(segment.X1, segment.Y1);
                PathPoint c2 = matrix.Transform(segment.X2, segment.Y2);
                IncludeExtremes(previous.X, c1.X, c2.X, end.X, previous, c1, c2, end);
                IncludeExtremes(previous.Y, c1.Y, c2.Y, end.Y, previous, c1, c2, end);
            }

            previous = end;
        }

        return double.IsInfinity(left) ? default : new SvgRect(left, top, right - left, bottom - top);

        void Include(PathPoint point)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        void IncludeExtremes(double p0, double p1, double p2, double p3, PathPoint a, PathPoint b, PathPoint c, PathPoint d)
        {
            // The derivative of the cubic is a quadratic in t: solve it for the extremes inside (0, 1).
            double qa = (-p0) + (3 * p1) - (3 * p2) + p3;
            double qb = 2 * (p0 - (2 * p1) + p2);
            double qc = p1 - p0;
            Span<double> roots = stackalloc double[2];
            int count = 0;
            if (Math.Abs(qa) < 1e-12)
            {
                if (Math.Abs(qb) > 1e-12)
                {
                    roots[count++] = -qc / qb;
                }
            }
            else
            {
                double discriminant = (qb * qb) - (4 * qa * qc);
                if (discriminant >= 0)
                {
                    double root = Math.Sqrt(discriminant);
                    roots[count++] = (-qb + root) / (2 * qa);
                    roots[count++] = (-qb - root) / (2 * qa);
                }
            }

            for (int i = 0; i < count; i++)
            {
                double t = roots[i];
                if (t > 0 && t < 1)
                {
                    double u = 1 - t;
                    Include(new PathPoint(
                        (u * u * u * a.X) + (3 * u * u * t * b.X) + (3 * u * t * t * c.X) + (t * t * t * d.X),
                        (u * u * u * a.Y) + (3 * u * u * t * b.Y) + (3 * u * t * t * c.Y) + (t * t * t * d.Y)));
                }
            }
        }
    }

    /// <summary>Writes the path to <paramref name="canvas"/> with each point mapped through <paramref name="matrix"/>; nothing is painted.</summary>
    /// <returns><see langword="true"/> when anything was written, so a painting operator shall follow.</returns>
    public bool Emit(PdfCanvas canvas, Matrix matrix)
    {
        bool begun = false;
        foreach (SvgSegment segment in _segments)
        {
            PathPoint end = matrix.Transform(segment.X, segment.Y);
            switch (segment.Kind)
            {
                case SvgSegmentKind.Move:
                    canvas.MoveTo(end.X, end.Y);
                    begun = true;
                    break;
                case SvgSegmentKind.Line when begun:
                    canvas.LineTo(end.X, end.Y);
                    break;
                case SvgSegmentKind.Cubic when begun:
                    PathPoint c1 = matrix.Transform(segment.X1, segment.Y1);
                    PathPoint c2 = matrix.Transform(segment.X2, segment.Y2);
                    canvas.CurveTo(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                    break;
                case SvgSegmentKind.Close when begun:
                    canvas.ClosePath();
                    break;
            }
        }

        return begun;
    }

    private static double Angle(double ux, double uy, double vx, double vy)
    {
        double angle = Math.Atan2((ux * vy) - (uy * vx), (ux * vx) + (uy * vy));
        return angle;
    }
}

/// <summary>Reads SVG path data (the <c>d</c> attribute) into an <see cref="SvgPath"/>.</summary>
/// <remarks>
/// SVG 1.1 §8.3 (path data) and its BNF in §8.3.9; arc flags may be written without separators (SVG 2 §9.3.9). On an error the path
/// keeps the segments read before it, as §F.2 says a renderer draws the path up to the error.
/// </remarks>
internal static class SvgPathParser
{
    /// <summary>Parses <paramref name="data"/> into <paramref name="path"/>.</summary>
    /// <returns><see langword="true"/> when all of the data was read.</returns>
    public static bool TryParse(ReadOnlySpan<char> data, SvgPath path)
    {
        int position = 0;
        char command = '\0';
        double lastControlX = 0, lastControlY = 0;
        char previous = '\0';
        Span<double> a = stackalloc double[7];
        while (true)
        {
            SvgNumber.SkipWhiteSpace(data, ref position);
            if (position >= data.Length)
            {
                return true;
            }

            char c = data[position];
            if (char.IsAsciiLetter(c))
            {
                command = c;
                position++;
                if (command is 'Z' or 'z')
                {
                    if (!path.HasCurrentPoint)
                    {
                        return false;
                    }

                    path.Close();
                    previous = command;
                    continue;
                }
            }
            else if (command == '\0' || command is 'Z' or 'z')
            {
                return false;
            }

            if (!path.HasCurrentPoint && command is not ('M' or 'm'))
            {
                return false;
            }

            bool relative = char.IsAsciiLetterLower(command);
            double ox = relative ? path.CurrentX : 0;
            double oy = relative ? path.CurrentY : 0;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    if (!Read(data, ref position, a[..2]))
                    {
                        return false;
                    }

                    path.MoveTo(ox + a[0], oy + a[1]);

                    // Further pairs after a moveto are implicit linetos.
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    if (!Read(data, ref position, a[..2]))
                    {
                        return false;
                    }

                    path.LineTo(ox + a[0], oy + a[1]);
                    break;
                case 'H':
                    if (!Read(data, ref position, a[..1]))
                    {
                        return false;
                    }

                    path.LineTo(ox + a[0], path.CurrentY);
                    break;
                case 'V':
                    if (!Read(data, ref position, a[..1]))
                    {
                        return false;
                    }

                    path.LineTo(path.CurrentX, oy + a[0]);
                    break;
                case 'C':
                    if (!Read(data, ref position, a[..6]))
                    {
                        return false;
                    }

                    path.CubicTo(ox + a[0], oy + a[1], ox + a[2], oy + a[3], ox + a[4], oy + a[5]);
                    lastControlX = ox + a[2];
                    lastControlY = oy + a[3];
                    break;
                case 'S':
                    {
                        if (!Read(data, ref position, a[..4]))
                        {
                            return false;
                        }

                        (double x1, double y1) = char.ToUpperInvariant(previous) is 'C' or 'S'
                            ? ((2 * path.CurrentX) - lastControlX, (2 * path.CurrentY) - lastControlY)
                            : (path.CurrentX, path.CurrentY);
                        path.CubicTo(x1, y1, ox + a[0], oy + a[1], ox + a[2], oy + a[3]);
                        lastControlX = ox + a[0];
                        lastControlY = oy + a[1];
                        break;
                    }

                case 'Q':
                    if (!Read(data, ref position, a[..4]))
                    {
                        return false;
                    }

                    path.QuadraticTo(ox + a[0], oy + a[1], ox + a[2], oy + a[3]);
                    lastControlX = ox + a[0];
                    lastControlY = oy + a[1];
                    break;
                case 'T':
                    {
                        if (!Read(data, ref position, a[..2]))
                        {
                            return false;
                        }

                        (double x1, double y1) = char.ToUpperInvariant(previous) is 'Q' or 'T'
                            ? ((2 * path.CurrentX) - lastControlX, (2 * path.CurrentY) - lastControlY)
                            : (path.CurrentX, path.CurrentY);
                        path.QuadraticTo(x1, y1, ox + a[0], oy + a[1]);
                        lastControlX = x1;
                        lastControlY = y1;
                        break;
                    }

                case 'A':
                    if (!Read(data, ref position, a[..3])
                        || !ReadFlag(data, ref position, out bool large)
                        || !ReadFlag(data, ref position, out bool sweep)
                        || !Read(data, ref position, a.Slice(3, 2)))
                    {
                        return false;
                    }

                    path.ArcTo(a[0], a[1], a[2], large, sweep, ox + a[3], oy + a[4]);
                    break;
                default:
                    return false;
            }

            previous = command;
        }
    }

    private static bool Read(ReadOnlySpan<char> data, ref int position, Span<double> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (!SvgNumber.TryRead(data, ref position, out values[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReadFlag(ReadOnlySpan<char> data, ref int position, out bool flag)
    {
        SvgNumber.SkipSeparator(data, ref position);
        flag = false;
        if (position >= data.Length || data[position] is not ('0' or '1'))
        {
            return false;
        }

        flag = data[position] == '1';
        position++;
        return true;
    }
}
