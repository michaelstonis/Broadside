using Broadside.Annotations;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Point and rectangle tests against parallelograms and quadrilaterals for hit-testing; edges count as inside.</summary>
/// <remarks>ISO 32000-2 §8.3.4: a rectangle mapped by a matrix is a parallelogram; §12.5.6.10: QuadPoints corners come in any order.</remarks>
internal static class HitTestGeometry
{
    /// <summary>Whether <paramref name="point"/> is inside the parallelogram <c>origin + s·u + t·v</c>, 0 ≤ s, t ≤ 1; never for a flat one.</summary>
    public static bool Contains(PathPoint origin, PathPoint u, PathPoint v, PathPoint point)
    {
        double determinant = (u.X * v.Y) - (u.Y * v.X);
        if (determinant == 0 || !double.IsFinite(determinant))
        {
            return false;
        }

        double dx = point.X - origin.X;
        double dy = point.Y - origin.Y;
        double s = ((dx * v.Y) - (dy * v.X)) / determinant;
        double t = ((u.X * dy) - (u.Y * dx)) / determinant;
        return s >= 0 && s <= 1 && t >= 0 && t <= 1;
    }

    /// <summary>Whether the parallelogram <c>origin + s·u + t·v</c> and the upright rectangle share a point (separating axes).</summary>
    public static bool Intersects(PathPoint origin, PathPoint u, PathPoint v, in PdfRectangle area)
    {
        PathPoint a = origin;
        var b = new PathPoint(origin.X + u.X, origin.Y + u.Y);
        var c = new PathPoint(b.X + v.X, b.Y + v.Y);
        var d = new PathPoint(origin.X + v.X, origin.Y + v.Y);
        return OverlapsUpright(a, b, c, d, area)
            && Overlap(-u.Y, u.X, a, b, c, d, area)
            && Overlap(-v.Y, v.X, a, b, c, d, area);
    }

    /// <summary>Whether <paramref name="point"/> is inside the convex hull of a quadrilateral's four points, in whatever order they come.</summary>
    /// <remarks>The hull of four points is the union of the four triangles three of them make; a flat triangle holds nothing.</remarks>
    public static bool Contains(in PdfQuadrilateral quad, PathPoint point) =>
        InTriangle(quad.Point1, quad.Point2, quad.Point3, point)
        || InTriangle(quad.Point1, quad.Point2, quad.Point4, point)
        || InTriangle(quad.Point1, quad.Point3, quad.Point4, point)
        || InTriangle(quad.Point2, quad.Point3, quad.Point4, point);

    /// <summary>Whether the convex hull of a quadrilateral's four points and the upright rectangle share a point.</summary>
    /// <remarks>Separating axes: the rectangle's two and the normals of the six segments between the points, which include the hull's edges.</remarks>
    public static bool Intersects(in PdfQuadrilateral quad, in PdfRectangle area)
    {
        PathPoint a = quad.Point1;
        PathPoint b = quad.Point2;
        PathPoint c = quad.Point3;
        PathPoint d = quad.Point4;
        return OverlapsUpright(a, b, c, d, area)
            && Overlap(a.Y - b.Y, b.X - a.X, a, b, c, d, area)
            && Overlap(a.Y - c.Y, c.X - a.X, a, b, c, d, area)
            && Overlap(a.Y - d.Y, d.X - a.X, a, b, c, d, area)
            && Overlap(b.Y - c.Y, c.X - b.X, a, b, c, d, area)
            && Overlap(b.Y - d.Y, d.X - b.X, a, b, c, d, area)
            && Overlap(c.Y - d.Y, d.X - c.X, a, b, c, d, area);
    }

    /// <summary>Whether two upright rectangles share a point.</summary>
    public static bool Overlaps(in PdfRectangle first, in PdfRectangle second) =>
        first.Left <= second.Right && second.Left <= first.Right && first.Bottom <= second.Top && second.Bottom <= first.Top;

    /// <summary>Whether a point is inside an upright rectangle.</summary>
    public static bool Contains(in PdfRectangle rectangle, PathPoint point) =>
        point.X >= rectangle.Left && point.X <= rectangle.Right && point.Y >= rectangle.Bottom && point.Y <= rectangle.Top;

    /// <summary>The upright rectangle around four points.</summary>
    public static PdfRectangle Bounds(PathPoint a, PathPoint b, PathPoint c, PathPoint d) => new(
        Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)),
        Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)),
        Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)),
        Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));

    private static bool InTriangle(PathPoint a, PathPoint b, PathPoint c, PathPoint p)
    {
        double area = Cross(a, b, c);
        if (area == 0 || !double.IsFinite(area))
        {
            return false;
        }

        double d1 = Cross(a, b, p);
        double d2 = Cross(b, c, p);
        double d3 = Cross(c, a, p);
        bool negative = d1 < 0 || d2 < 0 || d3 < 0;
        bool positive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(negative && positive) && !double.IsNaN(d1 + d2 + d3);
    }

    private static double Cross(PathPoint a, PathPoint b, PathPoint p) => ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));

    private static bool OverlapsUpright(PathPoint a, PathPoint b, PathPoint c, PathPoint d, in PdfRectangle area) =>
        Overlaps(Bounds(a, b, c, d), area);

    /// <summary>Whether the four points and the rectangle's corners overlap when projected on the axis (x, y); a zero axis separates nothing.</summary>
    private static bool Overlap(double x, double y, PathPoint a, PathPoint b, PathPoint c, PathPoint d, in PdfRectangle area)
    {
        if (x == 0 && y == 0)
        {
            return true;
        }

        double pa = (a.X * x) + (a.Y * y);
        double pb = (b.X * x) + (b.Y * y);
        double pc = (c.X * x) + (c.Y * y);
        double pd = (d.X * x) + (d.Y * y);
        double min = Math.Min(Math.Min(pa, pb), Math.Min(pc, pd));
        double max = Math.Max(Math.Max(pa, pb), Math.Max(pc, pd));

        double r1 = (area.Left * x) + (area.Bottom * y);
        double r2 = (area.Right * x) + (area.Bottom * y);
        double r3 = (area.Right * x) + (area.Top * y);
        double r4 = (area.Left * x) + (area.Top * y);
        double rmin = Math.Min(Math.Min(r1, r2), Math.Min(r3, r4));
        double rmax = Math.Max(Math.Max(r1, r2), Math.Max(r3, r4));
        return min <= rmax && rmin <= max;
    }
}
