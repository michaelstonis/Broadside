using System.Buffers;
using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Scan-converts one filled shape at a time with analytic anti-aliasing: line segments in device space are clipped to the bitmap,
/// cut at every pixel boundary, and each piece adds its signed height (cover) and the share of its pixel it leaves to its left
/// (area) to the cell of that pixel; a sweep along each row turns the running sum into the fraction of every pixel the shape covers
/// and composites the colour by it.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §10.7.4: device space with pixel boundaries on integer coordinates; shapes are not rounded to pixels. Coverage is the
/// exact area of the pixel inside each edge-bounded region, the cell scheme of FreeType's and Anti-Grain Geometry's scanline
/// rasterizers. The winding value integrated over a pixel decides its coverage: non-zero (§8.5.3.3.2) clamps its magnitude at 1;
/// even-odd (§8.5.3.3.3) folds it into 0..1 by parity. Where several edges cross one pixel, this is the area-weighted estimate every
/// such rasterizer makes. A shape covering a pixel only partly gives it partial coverage rather than painting it whole; zero-area
/// shapes paint nothing, except for degenerate subpaths, which <see cref="AddPixel"/> handles as §8.5.3.3.1 requires.
/// </para>
/// <para>
/// Hot path (CLAUDE.md): after warm-up nothing is allocated per shape, segment, cell or scanline. Cells live in pooled arrays that
/// grow by doubling, and are sorted in place by a 64-bit row-and-column key.
/// </para>
/// </remarks>
internal sealed class ScanlineRasterizer : IDisposable
{
    private long[] _keys = ArrayPool<long>.Shared.Rent(1024);
    private Cell[] _cells = ArrayPool<Cell>.Shared.Rent(1024);
    private int _count;
    private int _width;
    private int _height;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;

    /// <summary>Starts a new shape clipped to <c>[0, width) × [0, height)</c>.</summary>
    public void Reset(int width, int height)
    {
        _width = width;
        _height = height;
        _count = 0;
        _lastX = int.MinValue;
        _lastY = int.MinValue;
    }

    /// <summary>Gets a value indicating whether the shape has no edges inside the clip.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>Adds the edge from (x0, y0) to (x1, y1) in device pixels.</summary>
    public void AddLine(double x0, double y0, double x1, double y1)
    {
        if (y0 == y1 || !double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1))
        {
            return;
        }

        // Rows outside the bitmap never show; the part of the edge in them is dropped (cover only counts in its own row).
        double top = Math.Min(y0, y1);
        double bottom = Math.Max(y0, y1);
        if (bottom <= 0 || top >= _height)
        {
            return;
        }

        if (top < 0 || bottom > _height)
        {
            double t0 = Math.Clamp((0 - y0) / (y1 - y0), 0, 1);
            double t1 = Math.Clamp((_height - y0) / (y1 - y0), 0, 1);
            (double ta, double tb) = (Math.Min(t0, t1), Math.Max(t0, t1));
            double dx = x1 - x0;
            double dy = y1 - y0;
            (x0, y0, x1, y1) = (x0 + (dx * ta), Math.Clamp(y0 + (dy * ta), 0, _height), x0 + (dx * tb), Math.Clamp(y0 + (dy * tb), 0, _height));
        }

        // Columns: what lies right of the bitmap affects only pixels right of it, so it is dropped; what lies left of it still
        // covers every pixel to its right, so it becomes a vertical edge on the left border with the same rows.
        if (x0 >= _width && x1 >= _width)
        {
            return;
        }

        if (x0 >= 0 && x1 >= 0 && x0 <= _width && x1 <= _width)
        {
            AddClippedLine(x0, y0, x1, y1);
            return;
        }

        Span<double> cuts = stackalloc double[4];
        int n = 0;
        cuts[n++] = 0;
        if ((x0 < 0) != (x1 < 0))
        {
            cuts[n++] = (0 - x0) / (x1 - x0);
        }

        if ((x0 > _width) != (x1 > _width))
        {
            cuts[n++] = (_width - x0) / (x1 - x0);
        }

        cuts[n++] = 1;
        cuts[..n].Sort();
        for (int i = 0; i + 1 < n; i++)
        {
            double ta = cuts[i];
            double tb = cuts[i + 1];
            if (tb <= ta)
            {
                continue;
            }

            double xa = x0 + ((x1 - x0) * ta);
            double ya = y0 + ((y1 - y0) * ta);
            double xb = x0 + ((x1 - x0) * tb);
            double yb = y0 + ((y1 - y0) * tb);
            double middle = (xa + xb) / 2;
            if (middle < 0)
            {
                AddClippedLine(0, ya, 0, yb);
            }
            else if (middle < _width)
            {
                AddClippedLine(Math.Clamp(xa, 0, _width), ya, Math.Clamp(xb, 0, _width), yb);
            }
        }
    }

    /// <summary>
    /// Adds the device pixel under (x, y) as a unit square: the region a degenerate subpath encloses (ISO 32000-2 §8.5.3.3.1).
    /// </summary>
    public void AddPixel(double x, double y)
    {
        if (!(x >= 0 && x < _width && y >= 0 && y < _height))
        {
            return;
        }

        double left = Math.Floor(x);
        double top = Math.Floor(y);
        AddLine(left, top, left, top + 1);
        AddLine(left + 1, top + 1, left + 1, top);
    }

    /// <summary>Composites <paramref name="color"/> over <paramref name="bitmap"/> by the coverage of the shape, then starts a new shape.</summary>
    /// <param name="bitmap">The target; at least as large as the clip.</param>
    /// <param name="rule">How the winding value decides coverage.</param>
    /// <param name="color">The opaque colour as premultiplied BGRA in memory order.</param>
    public void Fill(Bitmap bitmap, FillRule rule, uint color)
    {
        if (_count == 0)
        {
            return;
        }

        Span<long> keys = _keys.AsSpan(0, _count);
        Span<Cell> cells = _cells.AsSpan(0, _count);
        keys.Sort(cells);
        Span<byte> pixels = bitmap.Pixels;
        int stride = bitmap.Stride;
        int index = 0;
        while (index < cells.Length)
        {
            int row = (int)(keys[index] >> 32);
            Span<uint> line = MemoryMarshal.Cast<byte, uint>(pixels.Slice(row * stride, _width * 4));
            double accumulated = 0;
            while (index < cells.Length && (int)(keys[index] >> 32) == row)
            {
                int x = (int)(uint)keys[index];
                double cover = 0;
                double area = 0;
                do
                {
                    cover += cells[index].Cover;
                    area += cells[index].Area;
                    index++;
                }
                while (index < cells.Length && keys[index] == keys[index - 1]);

                Blend(line, x, 1, Coverage(accumulated + cover - area, rule), color);
                accumulated += cover;
                int next = index < cells.Length && (int)(keys[index] >> 32) == row ? (int)(uint)keys[index] : _width;
                if (next > x + 1)
                {
                    Blend(line, x + 1, next - x - 1, Coverage(accumulated, rule), color);
                }
            }
        }

        Reset(_width, _height);
    }

    /// <summary>Returns the pooled cell arrays.</summary>
    public void Dispose()
    {
        ArrayPool<long>.Shared.Return(_keys);
        ArrayPool<Cell>.Shared.Return(_cells);
        _keys = [];
        _cells = [];
        _count = 0;
    }

    /// <summary>Turns a winding value integrated over a pixel into the 8-bit coverage the fill rule gives it.</summary>
    private static int Coverage(double winding, FillRule rule)
    {
        double value = Math.Abs(winding);
        if (rule == FillRule.EvenOdd)
        {
            value -= 2 * Math.Floor(value / 2);
            if (value > 1)
            {
                value = 2 - value;
            }
        }
        else if (value > 1)
        {
            value = 1;
        }

        return (int)((value * 255) + 0.5);
    }

    /// <summary>Composites an opaque colour over <paramref name="count"/> pixels at <paramref name="coverage"/> (0 to 255).</summary>
    private static void Blend(Span<uint> line, int x, int count, int coverage, uint color)
    {
        if (coverage <= 0)
        {
            return;
        }

        Span<uint> span = line.Slice(x, count);
        if (coverage >= 255)
        {
            span.Fill(color);
            return;
        }

        // Premultiplied "source over" with source alpha = coverage: d = s × c + d × (1 − c), per channel, rounded.
        int inverse = 255 - coverage;
        Span<byte> bytes = MemoryMarshal.AsBytes(span);
        Span<byte> source = stackalloc byte[4];
        MemoryMarshal.Write(source, in color);
        int s0 = source[0] * coverage;
        int s1 = source[1] * coverage;
        int s2 = source[2] * coverage;
        int s3 = source[3] * coverage;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = Divide255(s0 + (bytes[i] * inverse));
            bytes[i + 1] = Divide255(s1 + (bytes[i + 1] * inverse));
            bytes[i + 2] = Divide255(s2 + (bytes[i + 2] * inverse));
            bytes[i + 3] = Divide255(s3 + (bytes[i + 3] * inverse));
        }
    }

    /// <summary>Rounds <paramref name="value"/> / 255 for 0 ≤ value ≤ 255 × 255.</summary>
    private static byte Divide255(int value)
    {
        value += 128;
        return (byte)((value + (value >> 8)) >> 8);
    }

    /// <summary>Adds an edge that lies inside <c>[0, width] × [0, height]</c>, one cell per pixel it crosses.</summary>
    private void AddClippedLine(double x0, double y0, double x1, double y1)
    {
        if (y0 == y1)
        {
            return;
        }

        double sign = y1 > y0 ? 1 : -1;
        double top = Math.Min(y0, y1);
        double bottom = Math.Max(y0, y1);
        double slope = (x1 - x0) / (y1 - y0);
        int first = Math.Max(0, (int)Math.Floor(top));
        int last = Math.Min(_height - 1, (int)Math.Ceiling(bottom) - 1);
        for (int row = first; row <= last; row++)
        {
            double ya = Math.Max(top, row);
            double yb = Math.Min(bottom, row + 1);
            if (yb <= ya)
            {
                continue;
            }

            double xa = x0 + ((ya - y0) * slope);
            double xb = x0 + ((yb - y0) * slope);
            AddRowPiece(row, Math.Clamp(xa, 0, _width), Math.Clamp(xb, 0, _width), sign * (yb - ya));
        }
    }

    /// <summary>Adds a piece of an edge inside one row, spanning x from <paramref name="xa"/> to <paramref name="xb"/>, with signed height <paramref name="dy"/>.</summary>
    private void AddRowPiece(int row, double xa, double xb, double dy)
    {
        double left = Math.Min(xa, xb);
        double right = Math.Max(xa, xb);
        int column = (int)Math.Floor(left);
        int lastColumn = (int)Math.Ceiling(right) - 1;
        if (lastColumn <= column)
        {
            // Within one pixel column (or exactly on a pixel boundary).
            AddCell(column, row, dy, dy * (((left + right) / 2) - column));
            return;
        }

        double width = right - left;
        for (int x = column; x <= lastColumn; x++)
        {
            double u = Math.Max(left, x);
            double v = Math.Min(right, x + 1);
            if (v <= u)
            {
                continue;
            }

            double part = dy * ((v - u) / width);
            AddCell(x, row, part, part * (((u + v) / 2) - x));
        }
    }

    private void AddCell(int x, int y, double cover, double area)
    {
        if (x >= _width)
        {
            return;
        }

        if (x == _lastX && y == _lastY)
        {
            ref Cell last = ref _cells[_count - 1];
            last.Cover += cover;
            last.Area += area;
            return;
        }

        if (_count == _cells.Length)
        {
            Grow();
        }

        _keys[_count] = ((long)y << 32) | (uint)x;
        _cells[_count] = new Cell { Cover = cover, Area = area };
        _count++;
        _lastX = x;
        _lastY = y;
    }

    private void Grow()
    {
        int size = _cells.Length * 2;
        long[] keys = ArrayPool<long>.Shared.Rent(size);
        Cell[] cells = ArrayPool<Cell>.Shared.Rent(size);
        _keys.AsSpan(0, _count).CopyTo(keys);
        _cells.AsSpan(0, _count).CopyTo(cells);
        ArrayPool<long>.Shared.Return(_keys);
        ArrayPool<Cell>.Shared.Return(_cells);
        _keys = keys;
        _cells = cells;
    }

    /// <summary>What the edges crossing one pixel contribute: their signed heights and the parts of the pixel left of them, times height.</summary>
    private struct Cell
    {
        public double Cover;
        public double Area;
    }
}
