using Broadside.Graphics;

namespace Broadside.Redaction;

/// <summary>
/// The geometry of applying a redaction: areas as convex quadrilaterals, their overlap with glyph, image and form boxes (the
/// separating axis test), and the cuts of rectangles and line segments by an area's bounding box in user space.
/// </summary>
/// <remarks>ISO 32000-2 §8.3.2 (coordinate spaces), §8.5.2 (path construction), §12.5.6.23 (QuadPoints of a Redact annotation).</remarks>
internal static class RedactionGeometry
{
    /// <summary>Two shapes that overlap by less than this are taken to touch only.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>
    /// Orders the four points of <paramref name="quad"/> counter-clockwise around their convex hull, so a quadrilateral written in any
    /// order (QuadPoints are often written top-left, top-right, bottom-left, bottom-right) is one convex shape.
    /// </summary>
    public static Quad Hull(Quad quad)
    {
        Span<PathPoint> points = [quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft];
        double cx = (points[0].X + points[1].X + points[2].X + points[3].X) / 4;
        double cy = (points[0].Y + points[1].Y + points[2].Y + points[3].Y) / 4;
        Span<double> angles = stackalloc double[4];
        for (int index = 0; index < 4; index++)
        {
            angles[index] = Math.Atan2(points[index].Y - cy, points[index].X - cx);
        }

        angles.Sort(points);
        return new Quad(points[0], points[1], points[2], points[3]);
    }

    /// <summary>Maps the points of <paramref name="quad"/> through <paramref name="matrix"/>.</summary>
    public static Quad Transform(in Quad quad, in Matrix matrix) => new(
        matrix.Transform(quad.BottomLeft),
        matrix.Transform(quad.BottomRight),
        matrix.Transform(quad.TopRight),
        matrix.Transform(quad.TopLeft));

    /// <summary>Returns whether two convex quadrilaterals overlap by more than a touch (the separating axis test).</summary>
    public static bool Intersects(in Quad first, in Quad second)
    {
        Span<PathPoint> a = [first.BottomLeft, first.BottomRight, first.TopRight, first.TopLeft];
        Span<PathPoint> b = [second.BottomLeft, second.BottomRight, second.TopRight, second.TopLeft];
        return Overlaps(a, b);
    }

    /// <summary>Returns whether the segment from <paramref name="start"/> to <paramref name="end"/> meets the convex quadrilateral.</summary>
    public static bool Intersects(in Quad area, PathPoint start, PathPoint end)
    {
        Span<PathPoint> a = [area.BottomLeft, area.BottomRight, area.TopRight, area.TopLeft];
        Span<PathPoint> b = [start, end];
        return Overlaps(a, b);
    }

    /// <summary>Returns whether every point of <paramref name="inner"/> lies in the convex, counter-clockwise <paramref name="hull"/>.</summary>
    public static bool Contains(in Quad hull, in Quad inner) =>
        Contains(hull, inner.BottomLeft) && Contains(hull, inner.BottomRight) && Contains(hull, inner.TopRight) && Contains(hull, inner.TopLeft);

    /// <summary>Returns whether <paramref name="point"/> lies in the convex, counter-clockwise <paramref name="hull"/>.</summary>
    public static bool Contains(in Quad hull, PathPoint point)
    {
        Span<PathPoint> h = [hull.BottomLeft, hull.BottomRight, hull.TopRight, hull.TopLeft];
        for (int index = 0; index < 4; index++)
        {
            PathPoint p = h[index];
            PathPoint q = h[(index + 1) % 4];
            double cross = ((q.X - p.X) * (point.Y - p.Y)) - ((q.Y - p.Y) * (point.X - p.X));
            if (cross < -Tolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns whether two rectangles overlap by more than a touch.</summary>
    public static bool Overlaps(in PdfRectangle first, in PdfRectangle second) =>
        first.Left < second.Right - Tolerance && second.Left < first.Right - Tolerance
        && first.Bottom < second.Top - Tolerance && second.Bottom < first.Top - Tolerance;

    /// <summary>
    /// Clips the segment from <paramref name="start"/> to <paramref name="end"/> by <paramref name="box"/> (Liang-Barsky): the
    /// parameters, from 0 at the start to 1 at the end, between which it is inside. False when it misses the box.
    /// </summary>
    public static bool ClipSegment(PathPoint start, PathPoint end, in PdfRectangle box, out double enter, out double leave)
    {
        enter = 0;
        leave = 1;
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        return Clip(-dx, start.X - box.Left, ref enter, ref leave)
            && Clip(dx, box.Right - start.X, ref enter, ref leave)
            && Clip(-dy, start.Y - box.Bottom, ref enter, ref leave)
            && Clip(dy, box.Top - start.Y, ref enter, ref leave)
            && leave - enter > Tolerance || (dx == 0 && dy == 0 && Inside(box, start));
    }

    /// <summary>Returns whether <paramref name="point"/> lies in <paramref name="box"/>, its edges included.</summary>
    public static bool Inside(in PdfRectangle box, PathPoint point) =>
        point.X >= box.Left && point.X <= box.Right && point.Y >= box.Bottom && point.Y <= box.Top;

    /// <summary>
    /// Writes the parts of <paramref name="rect"/> outside <paramref name="cut"/> (at most four: below, above, left and right of it)
    /// into <paramref name="pieces"/>, and returns how many.
    /// </summary>
    public static int Subtract(in PdfRectangle rect, in PdfRectangle cut, Span<PdfRectangle> pieces)
    {
        int count = 0;
        if (!Overlaps(rect, cut))
        {
            pieces[count++] = rect;
            return count;
        }

        double bottom = Math.Max(rect.Bottom, cut.Bottom);
        double top = Math.Min(rect.Top, cut.Top);
        if (cut.Bottom > rect.Bottom)
        {
            pieces[count++] = new PdfRectangle(rect.Left, rect.Bottom, rect.Right, cut.Bottom);
        }

        if (cut.Top < rect.Top)
        {
            pieces[count++] = new PdfRectangle(rect.Left, cut.Top, rect.Right, rect.Top);
        }

        if (cut.Left > rect.Left)
        {
            pieces[count++] = new PdfRectangle(rect.Left, bottom, cut.Left, top);
        }

        if (cut.Right < rect.Right)
        {
            pieces[count++] = new PdfRectangle(cut.Right, bottom, rect.Right, top);
        }

        return count;
    }

    /// <summary>The bounding box of a quadrilateral.</summary>
    public static PdfRectangle Bounds(in Quad quad) => quad.Bounds;

    /// <summary>Grows a rectangle by <paramref name="margin"/> on every side.</summary>
    public static PdfRectangle Inflate(in PdfRectangle box, double margin) =>
        margin <= 0 ? box : new PdfRectangle(box.Left - margin, box.Bottom - margin, box.Right + margin, box.Top + margin);

    /// <summary>
    /// Returns whether <paramref name="point"/> is inside the closed polygon <paramref name="polygon"/> by the even-odd or non-zero
    /// winding rule.
    /// </summary>
    public static bool InsidePolygon(ReadOnlySpan<PathPoint> polygon, PathPoint point, FillRule rule)
    {
        int winding = 0;
        int crossings = 0;
        for (int index = 0; index < polygon.Length; index++)
        {
            PathPoint a = polygon[index];
            PathPoint b = polygon[(index + 1) % polygon.Length];
            if (a.Y <= point.Y)
            {
                if (b.Y > point.Y && Side(a, b, point) > 0)
                {
                    winding++;
                    crossings++;
                }
            }
            else if (b.Y <= point.Y && Side(a, b, point) < 0)
            {
                winding--;
                crossings++;
            }
        }

        return rule == FillRule.EvenOdd ? (crossings & 1) != 0 : winding != 0;
    }

    private static double Side(PathPoint a, PathPoint b, PathPoint point) => ((b.X - a.X) * (point.Y - a.Y)) - ((point.X - a.X) * (b.Y - a.Y));

    private static bool Clip(double p, double q, ref double enter, ref double leave)
    {
        if (p == 0)
        {
            return q >= 0;
        }

        double r = q / p;
        if (p < 0)
        {
            if (r > leave)
            {
                return false;
            }

            enter = Math.Max(enter, r);
        }
        else
        {
            if (r < enter)
            {
                return false;
            }

            leave = Math.Min(leave, r);
        }

        return true;
    }

    /// <summary>The separating axis test of two convex point sets (a polygon, a segment or a point).</summary>
    private static bool Overlaps(ReadOnlySpan<PathPoint> a, ReadOnlySpan<PathPoint> b) => !Separates(a, a, b) && !Separates(b, a, b);

    private static bool Separates(ReadOnlySpan<PathPoint> edges, ReadOnlySpan<PathPoint> a, ReadOnlySpan<PathPoint> b)
    {
        int count = edges.Length == 2 ? 1 : edges.Length;
        for (int index = 0; index < count; index++)
        {
            PathPoint p = edges[index];
            PathPoint q = edges[(index + 1) % edges.Length];
            double nx = -(q.Y - p.Y);
            double ny = q.X - p.X;
            if (Math.Abs(nx) + Math.Abs(ny) < 1e-12)
            {
                continue;
            }

            (double minA, double maxA) = Project(a, nx, ny);
            (double minB, double maxB) = Project(b, nx, ny);
            double scale = Math.Sqrt((nx * nx) + (ny * ny));
            if (maxA <= minB + (Tolerance * scale) || maxB <= minA + (Tolerance * scale))
            {
                return true;
            }
        }

        return false;
    }

    private static (double Min, double Max) Project(ReadOnlySpan<PathPoint> points, double nx, double ny)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (PathPoint point in points)
        {
            double value = (point.X * nx) + (point.Y * ny);
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        return (min, max);
    }
}
