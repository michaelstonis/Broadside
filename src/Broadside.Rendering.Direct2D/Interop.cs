using Broadside.Graphics;
using Windows.Win32.Graphics.Direct2D.Common;

namespace Broadside.Rendering.Direct2D;

/// <summary>Conversions between Broadside's double-precision geometry and Direct2D's single-precision structures.</summary>
internal static class Interop
{
    /// <summary>Coordinates are kept within ±10^18 so that Direct2D's single-precision arithmetic stays finite.</summary>
    private const double CoordinateLimit = 1e18;

    /// <summary>Direct2D's infinite rectangle (<c>D2D1::InfiniteRect</c>).</summary>
    public static D2D_RECT_F InfiniteRect => new() { left = -float.MaxValue, top = -float.MaxValue, right = float.MaxValue, bottom = float.MaxValue };

    /// <summary>Returns the Direct2D matrix of <paramref name="m"/>; both map row vectors, <c>[x y 1] × M</c>.</summary>
    public static D2D_MATRIX_3X2_F Matrix(in Matrix m)
    {
        D2D_MATRIX_3X2_F result = default;
        result.Anonymous.Anonymous1.m11 = (float)m.A;
        result.Anonymous.Anonymous1.m12 = (float)m.B;
        result.Anonymous.Anonymous1.m21 = (float)m.C;
        result.Anonymous.Anonymous1.m22 = (float)m.D;
        result.Anonymous.Anonymous1.dx = (float)m.E;
        result.Anonymous.Anonymous1.dy = (float)m.F;
        return result;
    }

    /// <summary>Returns the Direct2D point of (<paramref name="x"/>, <paramref name="y"/>), clamped to the coordinate limit.</summary>
    public static D2D_POINT_2F Point(double x, double y) => new()
    {
        x = (float)Math.Clamp(x, -CoordinateLimit, CoordinateLimit),
        y = (float)Math.Clamp(y, -CoordinateLimit, CoordinateLimit),
    };

    /// <summary>Returns the Direct2D point of <paramref name="point"/>.</summary>
    public static D2D_POINT_2F Point(PathPoint point) => Point(point.X, point.Y);
}
