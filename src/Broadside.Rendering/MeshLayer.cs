using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// A vertex of a Gouraud-shaded triangle in device pixels, with its colour: red, green and blue from 0 to 1, or, when the layer
/// reads colours from a table (<see cref="MeshLayer.UseTable"/>), the table position s from 0 to 1 in <see cref="C0"/>.
/// </summary>
internal readonly record struct MeshVertex(double X, double Y, float C0, float C1, float C2);

/// <summary>
/// The colours a mesh shading gives the pixels of one paint, before they are composited: triangles are drawn into it in order, each
/// pixel whose centre lies inside a triangle taking the colour interpolated there, a later triangle over an earlier one. A pixel no
/// triangle reaches stays empty, so the paint can tell the shading's own points from those outside it (unpainted, or the
/// Background in a shading pattern).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.5 and §8.7.4.5.6 (Gouraud shading: the colour inside a triangle is the linear interpolation of its vertices'
/// colours, or of t passed through the function afterwards), §8.7.4.5.2 (a function-based shading, sampled into a lattice of such
/// triangles by the rasterizer), §8.7.4.3 Table 77 (the BBox: points outside it are not the shading's). The mesh is the shading's
/// own geometry, not a path: its triangles are sampled at pixel centres with no anti-aliasing (the AntiAlias hint of Table 77 is
/// not acted on), and each centre belongs to exactly one of two triangles sharing an edge (half-open spans in x and y, the edge
/// computed alike from both sides), so meshes have no cracks or double-painted seams. The painted shape (the clip, the BBox, a
/// pattern's path) is anti-aliased as usual by the paint that reads this layer.
/// </para>
/// <para>
/// Hot path: one pooled buffer, grown as needed and returned by <see cref="Release"/>; drawing a triangle allocates nothing and
/// costs a few multiply-adds per covered pixel.
/// </para>
/// </remarks>
internal sealed class MeshLayer
{
    private uint[] _pixels = [];
    private int _left;
    private int _top;
    private int _width;
    private int _height;
    private uint[]? _table;
    private bool _hasBox;
    private Matrix _toTarget;
    private PdfRectangle _box;

    /// <summary>Gets the layer's pixels, row by row from (<see cref="Left"/>, <see cref="Top"/>); 0 is empty, any other value an opaque premultiplied BGRA colour.</summary>
    public ReadOnlySpan<uint> Pixels => _pixels.AsSpan(0, _width * _height);

    /// <summary>Gets the device column of the layer's first pixel.</summary>
    public int Left => _left;

    /// <summary>Gets the device row of the layer's first pixel.</summary>
    public int Top => _top;

    /// <summary>Gets the layer's width in pixels.</summary>
    public int Width => _width;

    /// <summary>Gets the layer's height in pixels.</summary>
    public int Height => _height;

    /// <summary>Gets a value indicating whether a triangle has painted a pixel since <see cref="Begin"/>.</summary>
    public bool HasPaint { get; private set; }

    /// <summary>Starts a layer covering the device pixels from (<paramref name="left"/>, <paramref name="top"/>) to (<paramref name="right"/>, <paramref name="bottom"/>), exclusive, all empty; colours are red, green and blue until <see cref="UseTable"/>.</summary>
    /// <returns><see langword="false"/> when the rectangle is empty.</returns>
    public bool Begin(int left, int top, int right, int bottom)
    {
        _table = null;
        _hasBox = false;
        HasPaint = false;
        if (right <= left || bottom <= top)
        {
            _width = _height = 0;
            return false;
        }

        _left = left;
        _top = top;
        _width = right - left;
        _height = bottom - top;
        int length = checked(_width * _height);
        Pooled.Ensure(ref _pixels, length);
        _pixels.AsSpan(0, length).Clear();
        return true;
    }

    /// <summary>Reads each vertex's <see cref="MeshVertex.C0"/> as a position in <paramref name="table"/> (s from 0 to 1) instead of a colour.</summary>
    public void UseTable(uint[] table) => _table = table;

    /// <summary>Leaves out the pixels whose centres map, through <paramref name="toTarget"/>, outside <paramref name="box"/> (§8.7.4.3, BBox).</summary>
    public void UseBox(in Matrix toTarget, PdfRectangle box)
    {
        _hasBox = true;
        _toTarget = toTarget;
        _box = box;
    }

    /// <summary>Returns the buffer to the pool.</summary>
    public void Release()
    {
        Pooled.Return(ref _pixels);
        _width = _height = 0;
        _table = null;
    }

    /// <summary>Paints the pixels whose centres lie in the triangle (half-open spans) with the colour interpolated linearly from its vertices.</summary>
    public void FillTriangle(in MeshVertex a, in MeshVertex b, in MeshVertex c)
    {
        if (_width == 0)
        {
            return;
        }

        // Order by y, then x, so the edge between two points is computed the same way by both triangles that share it.
        MeshVertex v0 = a;
        MeshVertex v1 = b;
        MeshVertex v2 = c;
        Order(ref v0, ref v1);
        Order(ref v1, ref v2);
        Order(ref v0, ref v1);

        double area = ((v1.X - v0.X) * (v2.Y - v0.Y)) - ((v2.X - v0.X) * (v1.Y - v0.Y));
        if (area == 0 || !double.IsFinite(area) || !double.IsFinite(v0.X + v1.X + v2.X + v0.Y + v1.Y + v2.Y))
        {
            return;
        }

        // The rows whose centres y + ½ lie in [y0, y2), within the layer.
        int firstRow = Math.Max(_top, CeilingCentre(v0.Y));
        int endRow = Math.Min(_top + _height, CeilingCentre(v2.Y));
        if (firstRow >= endRow)
        {
            return;
        }

        // The colour planes: c(x, y) = c0 + dx (x − x0) + dy (y − y0).
        Plane p0 = Plane.Of(v0.C0, v1.C0, v2.C0, v0, v1, v2, area);
        Plane p1 = _table is null ? Plane.Of(v0.C1, v1.C1, v2.C1, v0, v1, v2, area) : default;
        Plane p2 = _table is null ? Plane.Of(v0.C2, v1.C2, v2.C2, v0, v1, v2, area) : default;
        if (!(p0.IsFinite && p1.IsFinite && p2.IsFinite))
        {
            return;
        }

        Span<uint> pixels = _pixels.AsSpan(0, _width * _height);
        int right = _left + _width;
        for (int row = firstRow; row < endRow; row++)
        {
            double y = row + 0.5;
            double xa = EdgeX(v0, v2, y);
            double xb = y < v1.Y ? EdgeX(v0, v1, y) : EdgeX(v1, v2, y);
            double low = Math.Min(xa, xb);
            double high = Math.Max(xa, xb);
            int first = Math.Max(_left, CeilingCentre(low));
            int end = Math.Min(right, CeilingCentre(high));
            if (first >= end)
            {
                continue;
            }

            Span<uint> line = pixels.Slice(((row - _top) * _width) + (first - _left), end - first);
            double dx = first + 0.5 - v0.X;
            double dy = y - v0.Y;
            double s0 = p0.At(dx, dy);
            double s1 = p1.At(dx, dy);
            double s2 = p2.At(dx, dy);
            if (!_hasBox)
            {
                // The common case, specialised: every pixel of the span is painted.
                HasPaint = true;
                if (_table is { } colours)
                {
                    for (int i = 0; i < line.Length; i++)
                    {
                        line[i] = colours[Index(s0)];
                        s0 += p0.Dx;
                    }
                }
                else
                {
                    for (int i = 0; i < line.Length; i++)
                    {
                        line[i] = Pack(s0, s1, s2);
                        s0 += p0.Dx;
                        s1 += p1.Dx;
                        s2 += p2.Dx;
                    }
                }

                continue;
            }

            double tx = 0;
            double ty = 0;
            if (_hasBox)
            {
                tx = (_toTarget.A * (first + 0.5)) + (_toTarget.C * y) + _toTarget.E;
                ty = (_toTarget.B * (first + 0.5)) + (_toTarget.D * y) + _toTarget.F;
            }

            for (int i = 0; i < line.Length; i++)
            {
                if (!_hasBox || (tx >= _box.Left && tx <= _box.Right && ty >= _box.Bottom && ty <= _box.Top))
                {
                    line[i] = _table is { } table ? table[Index(s0)] : Pack(s0, s1, s2);
                    HasPaint = true;
                }

                s0 += p0.Dx;
                s1 += p1.Dx;
                s2 += p2.Dx;
                tx += _toTarget.A;
                ty += _toTarget.B;
            }
        }
    }

    /// <summary>The first whole number n with n + ½ ≥ <paramref name="value"/>, kept within ±2^30.</summary>
    private static int CeilingCentre(double value) => (int)Math.Clamp(Math.Ceiling(value - 0.5), -(1 << 30), 1 << 30);

    private static double EdgeX(in MeshVertex from, in MeshVertex to, double y)
    {
        double dy = to.Y - from.Y;
        return dy == 0 ? from.X : from.X + ((y - from.Y) * (to.X - from.X) / dy);
    }

    private static void Order(ref MeshVertex first, ref MeshVertex second)
    {
        if (second.Y < first.Y || (second.Y == first.Y && second.X < first.X))
        {
            (first, second) = (second, first);
        }
    }

    private static int Index(double s) => (int)((Math.Clamp(s, 0, 1) * (ShadingSource.Size - 1)) + 0.5);

    private static uint Pack(double r, double g, double b) =>
        (uint)Channel(b) | ((uint)Channel(g) << 8) | ((uint)Channel(r) << 16) | 0xFF000000u;

    private static byte Channel(double value) => (byte)((Math.Clamp(value, 0, 1) * 255) + 0.5);

    /// <summary>A colour component as a linear function of the device position, relative to the first vertex.</summary>
    private readonly record struct Plane(double C, double Dx, double Dy)
    {
        public bool IsFinite => double.IsFinite(C) && double.IsFinite(Dx) && double.IsFinite(Dy);

        public static Plane Of(float c0, float c1, float c2, in MeshVertex v0, in MeshVertex v1, in MeshVertex v2, double area)
        {
            double d1 = c1 - c0;
            double d2 = c2 - c0;
            double dx = ((d1 * (v2.Y - v0.Y)) - (d2 * (v1.Y - v0.Y))) / area;
            double dy = ((d2 * (v1.X - v0.X)) - (d1 * (v2.X - v0.X))) / area;
            return new Plane(c0, dx, dy);
        }

        public double At(double dx, double dy) => C + (Dx * dx) + (Dy * dy);
    }
}
