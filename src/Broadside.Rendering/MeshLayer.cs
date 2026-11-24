using System.Runtime.CompilerServices;
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
/// <para>ISO 32000-2 §8.7.4.5.7 and §8.7.4.5.8.</para>
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
    private uint[] _twin = [];
    private bool _twinning;

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

    /// <summary>Gets a value indicating whether a triangle has painted a pixel of the twin since <see cref="BeginTwin"/>.</summary>
    public bool TwinHasPaint { get; private set; }

    /// <summary>Gets the twin's pixels (<see cref="BeginTwin"/>), laid out as <see cref="Pixels"/>.</summary>
    public ReadOnlySpan<uint> TwinPixels => _twin.AsSpan(0, _twinning ? _width * _height : 0);

    /// <summary>Gets a value indicating whether triangles also paint the twin (<see cref="BeginTwin"/>).</summary>
    public bool Twinning => _twinning;

    /// <summary>Starts a layer covering the device pixels from (<paramref name="left"/>, <paramref name="top"/>) to (<paramref name="right"/>, <paramref name="bottom"/>), exclusive, all empty; colours are red, green and blue until <see cref="UseTable"/>.</summary>
    /// <returns><see langword="false"/> when the rectangle is empty.</returns>
    public bool Begin(int left, int top, int right, int bottom)
    {
        _table = null;
        _hasBox = false;
        _twinning = false;
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

    /// <summary>
    /// Starts a twin of the layer, all empty: a second set of pixels over the same rectangle that the triangles drawn with a grey
    /// level per vertex (<see cref="FillTriangle(in MeshVertex, in MeshVertex, in MeshVertex, float, float, float)"/>) paint with
    /// that level in all three channels, pixel for pixel as a layer of their own with that level as each vertex's three colours.
    /// A CMYK group's black plane is drawn so in the same walk as its C, M and Y.
    /// </summary>
    public void BeginTwin()
    {
        int length = _width * _height;
        Pooled.Ensure(ref _twin, length);
        _twin.AsSpan(0, length).Clear();
        _twinning = true;
        TwinHasPaint = false;
    }

    /// <summary>Stops painting the twin; its pixels are no longer kept.</summary>
    public void EndTwin() => _twinning = false;

    /// <summary>Hands over the twin's buffer (pooled, the caller returns it) and stops painting it.</summary>
    public uint[] TakeTwin()
    {
        uint[] twin = _twin;
        _twin = [];
        _twinning = false;
        return twin;
    }

    /// <summary>
    /// Makes <paramref name="pixels"/> (a twin taken by <see cref="TakeTwin"/>) the layer's pixels over the rectangle it was
    /// drawn for, as if its triangles had been drawn by <see cref="Begin"/> and <see cref="FillTriangle(in MeshVertex, in MeshVertex, in MeshVertex)"/>.
    /// </summary>
    /// <returns>The layer's previous buffer, which the caller now owns.</returns>
    public uint[] Adopt(uint[] pixels, int left, int top, int right, int bottom, bool hasPaint)
    {
        uint[] previous = _pixels;
        _pixels = pixels;
        _left = left;
        _top = top;
        _width = right - left;
        _height = bottom - top;
        _table = null;
        _hasBox = false;
        _twinning = false;
        HasPaint = hasPaint;
        return previous;
    }

    /// <summary>Returns the buffer to the pool.</summary>
    public void Release()
    {
        Pooled.Return(ref _pixels);
        Pooled.Return(ref _twin);
        _twinning = false;
        _width = _height = 0;
        _table = null;
    }

    /// <summary>
    /// Gets a value indicating whether a pixel centre of the layer lies in [<paramref name="minX"/>, <paramref name="maxX"/>) ×
    /// [<paramref name="minY"/>, <paramref name="maxY"/>): when not, no triangle inside that box paints a pixel (its rows and spans
    /// are half-open at the same ends), so it can be skipped before any of its setup.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Covers(double minX, double minY, double maxX, double maxY) =>
        Math.Max(_top, CeilingCentre(minY)) < Math.Min(_top + _height, CeilingCentre(maxY))
        && Math.Max(_left, CeilingCentre(minX)) < Math.Min(_left + _width, CeilingCentre(maxX));

    /// <summary>Paints the pixels whose centres lie in the triangle (half-open spans) with the colour interpolated linearly from its vertices.</summary>
    public void FillTriangle(in MeshVertex a, in MeshVertex b, in MeshVertex c) => Fill<Alone>(a, b, c, 0, 0, 0);

    /// <summary>
    /// Paints the triangle as <see cref="FillTriangle(in MeshVertex, in MeshVertex, in MeshVertex)"/> does and, while there is a twin
    /// (<see cref="BeginTwin"/>), its pixels in the twin with the grey level interpolated from <paramref name="ka"/>,
    /// <paramref name="kb"/> and <paramref name="kc"/>.
    /// </summary>
    public void FillTriangle(in MeshVertex a, in MeshVertex b, in MeshVertex c, float ka, float kb, float kc)
    {
        if (_twinning)
        {
            Fill<Twinned>(a, b, c, ka, kb, kc);
        }
        else
        {
            Fill<Alone>(a, b, c, ka, kb, kc);
        }
    }

    private void Fill<T>(in MeshVertex a, in MeshVertex b, in MeshVertex c, float ka, float kb, float kc)
        where T : struct, ITwin
    {
        if (_width == 0)
        {
            return;
        }

        MeshVertex v0 = a;
        MeshVertex v1 = b;
        MeshVertex v2 = c;
        float k0 = ka;
        float k1 = kb;
        float k2 = kc;
        Order(ref v0, ref v1, ref k0, ref k1);
        Order(ref v1, ref v2, ref k1, ref k2);
        Order(ref v0, ref v1, ref k0, ref k1);

        double e1x = v1.X - v0.X;
        double e1y = v1.Y - v0.Y;
        double e2x = v2.X - v0.X;
        double e2y = v2.Y - v0.Y;
        double area = (e1x * e2y) - (e2x * e1y);
        if (area == 0 || !double.IsFinite(area) || !double.IsFinite(v0.X + v1.X + v2.X + v0.Y + v1.Y + v2.Y))
        {
            return;
        }

        int firstRow = Math.Max(_top, CeilingCentre(v0.Y));
        int endRow = Math.Min(_top + _height, CeilingCentre(v2.Y));
        if (firstRow >= endRow)
        {
            return;
        }

        Plane p0 = Plane.Of(v0.C0, v1.C0, v2.C0, e1x, e1y, e2x, e2y, area);
        Plane p1 = _table is null ? Plane.Of(v0.C1, v1.C1, v2.C1, e1x, e1y, e2x, e2y, area) : default;
        Plane p2 = _table is null ? Plane.Of(v0.C2, v1.C2, v2.C2, e1x, e1y, e2x, e2y, area) : default;
        bool colours = p0.IsFinite && p1.IsFinite && p2.IsFinite;
        Plane pk = default;
        bool grey = false;
        if (T.On)
        {
            pk = Plane.Of(k0, k1, k2, e1x, e1y, e2x, e2y, area);
            grey = pk.IsFinite;
        }

        if (!colours && !grey)
        {
            return;
        }

        // The long edge v0 → v2 and the short ones v0 → v1 (rows above v1) and v1 → v2 (the rest).
        var longEdge = new Edge(v0.X, v0.Y, e2x, e2y);
        var upper = new Edge(v0.X, v0.Y, e1x, e1y);
        var lower = new Edge(v1.X, v1.Y, v2.X - v1.X, v2.Y - v1.Y);
        int row = firstRow;
        for (; row < endRow && row + 0.5 < v1.Y; row++)
        {
            Span<T>(row, longEdge, upper, v0, p0, p1, p2, pk, colours, grey);
        }

        for (; row < endRow; row++)
        {
            Span<T>(row, longEdge, lower, v0, p0, p1, p2, pk, colours, grey);
        }
    }

    /// <summary>Paints one row's span of the triangle, between its long edge and the short edge at that row.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Span<T>(int row, in Edge longEdge, in Edge shortEdge, in MeshVertex v0, in Plane p0, in Plane p1, in Plane p2, in Plane pk, bool colours, bool grey)
        where T : struct, ITwin
    {
        double y = row + 0.5;
        double xa = longEdge.At(y);
        double xb = shortEdge.At(y);
        double low = Math.Min(xa, xb);
        double high = Math.Max(xa, xb);
        double firstX = Math.Max(_left, Math.Ceiling(low - 0.5));
        double endX = Math.Min(_left + _width, Math.Ceiling(high - 0.5));
        if (firstX >= endX)
        {
            return;
        }

        int first = (int)firstX;
        int end = (int)endX;
        int start = ((row - _top) * _width) + (first - _left);
        int count = end - first;
        double dx = first + 0.5 - v0.X;
        double dy = y - v0.Y;
        double s0 = p0.At(dx, dy);
        double s1 = p1.At(dx, dy);
        double s2 = p2.At(dx, dy);
        double sk = T.On ? pk.At(dx, dy) : 0;
        if (!_hasBox)
        {
            if (!T.On || colours)
            {
                HasPaint = true;
                Span<uint> line = _pixels.AsSpan(start, count);
                if (_table is { } table)
                {
                    for (int i = 0; i < line.Length; i++)
                    {
                        line[i] = table[Index(s0)];
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
            }

            if (T.On && grey)
            {
                TwinHasPaint = true;
                Span<uint> levels = _twin.AsSpan(start, count);
                for (int i = 0; i < levels.Length; i++)
                {
                    levels[i] = Grey(sk);
                    sk += pk.Dx;
                }
            }

            return;
        }

        double tx = (_toTarget.A * (first + 0.5)) + (_toTarget.C * y) + _toTarget.E;
        double ty = (_toTarget.B * (first + 0.5)) + (_toTarget.D * y) + _toTarget.F;
        Span<uint> pixels = _pixels.AsSpan(start, count);
        for (int i = 0; i < count; i++)
        {
            if (tx >= _box.Left && tx <= _box.Right && ty >= _box.Bottom && ty <= _box.Top)
            {
                if (!T.On || colours)
                {
                    pixels[i] = _table is { } table ? table[Index(s0)] : Pack(s0, s1, s2);
                    HasPaint = true;
                }

                if (T.On && grey)
                {
                    _twin[start + i] = Grey(sk);
                    TwinHasPaint = true;
                }
            }

            s0 += p0.Dx;
            s1 += p1.Dx;
            s2 += p2.Dx;
            sk += pk.Dx;
            tx += _toTarget.A;
            ty += _toTarget.B;
        }
    }

    /// <summary>A triangle edge's x at a row's centre y, computed alike by both triangles that share it.</summary>
    private readonly struct Edge(double x, double y, double dx, double dy)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double At(double row) => dy == 0 ? x : x + ((row - y) * dx / dy);
    }

    /// <summary>The first whole number n with n + ½ ≥ <paramref name="value"/>, kept within ±2^30.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CeilingCentre(double value) => (int)Math.Clamp(Math.Ceiling(value - 0.5), -(1 << 30), 1 << 30);

    private static double EdgeX(in MeshVertex from, in MeshVertex to, double y)
    {
        double dy = to.Y - from.Y;
        return dy == 0 ? from.X : from.X + ((y - from.Y) * (to.X - from.X) / dy);
    }

    private static void Order(ref MeshVertex first, ref MeshVertex second, ref float firstLevel, ref float secondLevel)
    {
        if (second.Y < first.Y || (second.Y == first.Y && second.X < first.X))
        {
            (first, second) = (second, first);
            (firstLevel, secondLevel) = (secondLevel, firstLevel);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Index(double s) => (int)((Math.Clamp(s, 0, 1) * (ShadingSource.Size - 1)) + 0.5);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Pack(double r, double g, double b) =>
        (uint)Channel(b) | ((uint)Channel(g) << 8) | ((uint)Channel(r) << 16) | 0xFF000000u;

    /// <summary>The pixel <see cref="Pack"/> makes of <paramref name="level"/> in all three channels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Grey(double level)
    {
        uint channel = Channel(level);
        return channel | (channel << 8) | (channel << 16) | 0xFF000000u;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Channel(double value) => (byte)((Math.Clamp(value, 0, 1) * 255) + 0.5);

    /// <summary>Whether a fill paints the twin too, fixed per instantiation so the plain fill pays nothing for it.</summary>
    private interface ITwin
    {
        static abstract bool On { get; }
    }

    private readonly struct Alone : ITwin
    {
        public static bool On => false;
    }

    private readonly struct Twinned : ITwin
    {
        public static bool On => true;
    }

    /// <summary>A colour component as a linear function of the device position, relative to the first vertex.</summary>
    private readonly record struct Plane(double C, double Dx, double Dy)
    {
        public bool IsFinite => double.IsFinite(C) && double.IsFinite(Dx) && double.IsFinite(Dy);

        public static Plane Of(float c0, float c1, float c2, double e1x, double e1y, double e2x, double e2y, double area)
        {
            double d1 = c1 - c0;
            double d2 = c2 - c0;
            double dx = ((d1 * e2y) - (d2 * e1y)) / area;
            double dy = ((d2 * e1x) - (d1 * e2x)) / area;
            return new Plane(c0, dx, dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double At(double dx, double dy) => C + (Dx * dx) + (Dy * dy);
    }
}
