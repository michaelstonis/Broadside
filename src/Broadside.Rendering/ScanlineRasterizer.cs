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
    private bool _sorted;
    private bool _reachesRight;

    /// <summary>Starts a new shape clipped to <c>[0, width) × [0, height)</c>.</summary>
    public void Reset(int width, int height)
    {
        _width = width;
        _height = height;
        _count = 0;
        _lastX = int.MinValue;
        _lastY = int.MinValue;
        _sorted = false;
        _reachesRight = false;
    }

    /// <summary>
    /// Gets or sets the clipping path every paint goes through (ISO 32000-2 §8.5.4): <see cref="Fill"/> and <see cref="Paint"/> cut
    /// the shape's coverage by it. <see langword="null"/> clips to the bitmap only.
    /// </summary>
    public RasterClip? Clip { get; set; }

    /// <summary>
    /// Gets or sets the transparency group paints go into (ISO 32000-2 §11.4), which sees every run of a paint's coverage before the
    /// paint's sink, after the clip: a knockout group or one that keeps its own shape or alpha needs that. <see langword="null"/>
    /// (no group, or a group that needs nothing per paint) hands the runs straight to the sink.
    /// </summary>
    public GroupLayer? Hook { get; set; }

    /// <summary>Starts a new shape with the size of the last one.</summary>
    public void Clear() => Reset(_width, _height);

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
            _reachesRight = true;
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
            else
            {
                _reachesRight = true;
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

    /// <summary>
    /// Composites <paramref name="color"/> over <paramref name="bitmap"/> through <paramref name="compositor"/> (constant alpha and
    /// blend mode, ISO 32000-2 §11.3) by the coverage of the shape within <see cref="Clip"/>, then starts a new shape.
    /// </summary>
    /// <param name="bitmap">The target; at least as large as the clip.</param>
    /// <param name="rule">How the winding value decides coverage.</param>
    /// <param name="color">The opaque colour as premultiplied BGRA in memory order.</param>
    /// <param name="compositor">The alpha and blend mode; the default is opaque Normal.</param>
    public void Fill(Bitmap bitmap, FillRule rule, uint color, Compositor compositor = default)
    {
        var sink = new ColorSink(bitmap.Pixels, bitmap.Stride, color, compositor);
        Paint(rule, ref sink);
    }

    /// <summary>
    /// Hands the coverage of the shape, cut by <see cref="Clip"/>, to <paramref name="sink"/> as runs of equal coverage above zero,
    /// then starts a new shape. Every paint (fills, strokes, images, shadings) goes through here or <see cref="Fill"/>, so the
    /// clipping path applies to all of them (ISO 32000-2 §8.5.4).
    /// </summary>
    /// <param name="rule">How the winding value decides coverage.</param>
    /// <param name="sink">What the paint does with each run.</param>
    public void Paint<TSink>(FillRule rule, ref TSink sink)
        where TSink : ICoverageSink, allows ref struct
    {
        RasterClip? clip = Clip;
        if (_count > 0 && clip?.IsEmpty != true)
        {
            Sweep(rule, ref sink, clip is { IsWholeBitmap: false } ? clip : null, zeros: false, Hook);
        }

        Reset(_width, _height);
    }

    /// <summary>
    /// Hands the raw coverage of the shape to <paramref name="sink"/>, not clipped: for every row with an edge, every pixel from the
    /// first edge to the right border, zeros included. The shape stays; <see cref="Reset"/> starts a new one.
    /// </summary>
    public void Sweep<TSink>(FillRule rule, ref TSink sink)
        where TSink : ICoverageSink, allows ref struct
    {
        if (_count > 0)
        {
            Sweep(rule, ref sink, null, zeros: true, hook: null);
        }
    }

    /// <summary>
    /// Returns the pixel rectangle outside which the shape covers nothing: from the first to the last row with an edge, and from the
    /// leftmost edge to the rightmost one, or to the right border when an edge lies beyond it. False when the shape has no edge.
    /// </summary>
    public bool TryGetBounds(out int left, out int top, out int right, out int bottom)
    {
        if (_count == 0)
        {
            left = top = right = bottom = 0;
            return false;
        }

        Sort();
        Span<long> keys = _keys.AsSpan(0, _count);
        top = (int)(keys[0] >> 32);
        bottom = (int)(keys[^1] >> 32) + 1;
        int min = int.MaxValue;
        int max = int.MinValue;
        foreach (long key in keys)
        {
            int x = (int)(uint)key;
            min = Math.Min(min, x);
            max = Math.Max(max, x);
        }

        left = min;
        right = _reachesRight ? _width : max + 1;
        return true;
    }

    /// <summary>Sorts the cells by row, then column.</summary>
    private void Sort()
    {
        if (!_sorted)
        {
            _keys.AsSpan(0, _count).Sort(_cells.AsSpan(0, _count));
            _sorted = true;
        }
    }

    /// <summary>The sweep: turns the sorted cells into runs of constant coverage, clipped or not, with or without the zero runs.</summary>
    private void Sweep<TSink>(FillRule rule, ref TSink sink, RasterClip? clip, bool zeros, GroupLayer? hook)
        where TSink : ICoverageSink, allows ref struct
    {
        Sort();
        Span<long> keys = _keys.AsSpan(0, _count);
        Span<Cell> cells = _cells.AsSpan(0, _count);
        int index = 0;
        while (index < cells.Length)
        {
            int row = (int)(keys[index] >> 32);
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

                Emit(ref sink, clip, zeros, hook, row, x, 1, Coverage(accumulated + cover - area, rule));
                accumulated += cover;
                int next = index < cells.Length && (int)(keys[index] >> 32) == row ? (int)(uint)keys[index] : _width;
                if (next > x + 1)
                {
                    Emit(ref sink, clip, zeros, hook, row, x + 1, next - x - 1, Coverage(accumulated, rule));
                }
            }
        }
    }

    private static void Emit<TSink>(ref TSink sink, RasterClip? clip, bool zeros, GroupLayer? hook, int row, int x, int count, int coverage)
        where TSink : ICoverageSink, allows ref struct
    {
        if (coverage <= 0 && !zeros)
        {
            return;
        }

        if (clip is not null)
        {
            clip.Apply(row, x, count, coverage, ref sink, hook);
        }
        else if (hook is not null)
        {
            hook.Span(row, x, count, coverage, ref sink);
        }
        else
        {
            sink.Span(row, x, count, coverage);
        }
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
            _reachesRight = true;
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

    /// <summary>Composites an opaque colour by the coverage of each run, through a compositor.</summary>
    private readonly ref struct ColorSink(Span<byte> pixels, int stride, uint color, Compositor compositor) : ICoverageSink
    {
        private readonly Span<byte> _pixels = pixels;

        public void Span(int row, int x, int count, int coverage) =>
            compositor.Fill(MemoryMarshal.Cast<byte, uint>(_pixels.Slice(row * stride, (x + count) * 4)), x, count, coverage, color);
    }

    /// <summary>What the edges crossing one pixel contribute: their signed heights and the parts of the pixel left of them, times height.</summary>
    private struct Cell
    {
        public double Cover;
        public double Area;
    }
}
