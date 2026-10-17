using System.Buffers;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The software rasterizer's current clipping path as coverage, with the stack <c>q</c> and <c>Q</c> push and pop: a device
/// rectangle with fractional edges, intersected with an optional anti-aliased 8-bit coverage mask. Every paint's coverage goes
/// through <see cref="Apply"/>, which multiplies it by the clip's.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.5.4: the clip starts as the whole page (here the bitmap area the page maps to, its crop box within its media box,
/// §14.11.2) and only shrinks, by intersection with the inside of a path under the fill rule of <c>W</c> or <c>W*</c>; restoring a
/// state brings back the clip saved with it. A form's bounding box (§8.10.1) is a rectangle clip like any other.
/// </para>
/// <para>
/// A rectangle aligned with the pixel axes (the common <c>re W n</c>, a form's box, under any transform that keeps it axis-aligned)
/// only narrows the rectangle: no mask, no per-pixel work beyond the fractional edge pixels (§10.7.4 area coverage of the edge). Any
/// other path is scan converted like a fill and multiplied into a mask that covers only the pixels it can reach; a mask is written
/// in place when the state that made it is still current, and otherwise copied, so a saved state's clip is never changed. Masks
/// are pooled per rasterizer: a warm render allocates nothing.
/// </para>
/// <para>
/// Coverage composes by multiplication: a shape's edge coverage times the clip's at the same pixel, the usual estimate of the area
/// of their intersection.
/// </para>
/// </remarks>
internal sealed class RasterClip : IDisposable
{
    /// <summary>The most mask buffers kept between renders; others go back to the shared pool.</summary>
    private const int MaxFree = 64;

    private State _current;
    private State[] _stack = new State[16];
    private int _depth;
    private int _width;
    private int _height;
    private byte[][] _free = new byte[8][];
    private int _freeCount;

    // Derived from _current by Refresh: the pixels where the clip's coverage can be above zero, and its fractional edge columns.
    private int _rowTop;

    // Where the coordinates paints use start in the clip's pixels (SetOrigin).
    private int _originX;
    private int _originY;
    private int _rowBottom;
    private int _columnLeft;
    private int _columnRight;
    private int _leftEdge;
    private int _rightEdge;

    // The soft mask every paint's coverage is also multiplied by (§11.6.4.3), as shape or as opacity; not part of the clip's stack.
    private SoftMaskPlane? _softMask;
    private bool _softMaskIsShape;

    /// <summary>Gets a value indicating whether nothing can be painted: the clip has no inside.</summary>
    public bool IsEmpty => _rowTop >= _rowBottom || _columnLeft >= _columnRight;

    /// <summary>Gets a value indicating whether the clip is the whole bitmap and there is no soft mask, so paints need no clipping.</summary>
    public bool IsWholeBitmap { get; private set; }

    /// <summary>Returns the pixel rectangle outside which the clip's coverage is zero (right and bottom exclusive), for a paint that fills the whole clip (<c>sh</c>).</summary>
    public void GetBounds(out int left, out int top, out int right, out int bottom)
    {
        left = _columnLeft - _originX;
        top = _rowTop - _originY;
        right = _columnRight - _originX;
        bottom = _rowBottom - _originY;
    }

    /// <summary>
    /// Sets where the coordinates paints use start in the clip's (the render's) pixels: a transparency group paints into a buffer
    /// that covers only its bounds, whose pixel (0, 0) is the render's (<paramref name="x"/>, <paramref name="y"/>). Every member
    /// takes and gives coordinates in that system; the clip itself stays in the render's.
    /// </summary>
    public void SetOrigin(int x, int y)
    {
        _originX = x;
        _originY = y;
    }

    /// <summary>
    /// Sets the soft mask paints go through after the clip (ISO 32000-2 §11.6.4.3), or none. As shape (fm, alpha is shape) it
    /// multiplies the coverage the group hook and the paint see; as opacity (qm) the hook sees the coverage as shape and the paint
    /// the coverage times the mask, so a knockout group replaces by shape and composites by opacity.
    /// </summary>
    public void SetSoftMask(SoftMaskPlane? mask, bool isShape)
    {
        _softMask = mask;
        _softMaskIsShape = isShape;
        Refresh();
    }

    /// <summary>Starts a render into <paramref name="width"/> × <paramref name="height"/> pixels: the clip is all of them (§8.5.4, the initial clipping path).</summary>
    public void Reset(int width, int height)
    {
        Release();
        _width = width;
        _height = height;
        _current = new State { Right = width, Bottom = height };
        _originX = 0;
        _originY = 0;
        Refresh();
    }

    /// <summary>Ends a render: every mask goes back to the free list.</summary>
    public void Release()
    {
        if (_current.OwnsMask)
        {
            Free(_current.Mask!);
        }

        for (int index = 0; index < _depth; index++)
        {
            if (_stack[index].OwnsMask)
            {
                Free(_stack[index].Mask!);
            }
        }

        Array.Clear(_stack, 0, _depth);
        _depth = 0;
        _current = default;
        Refresh();
    }

    /// <summary>Saves the clip (§8.4.2 <c>q</c>).</summary>
    public void Save()
    {
        if (_depth == _stack.Length)
        {
            Array.Resize(ref _stack, _stack.Length * 2);
        }

        _stack[_depth++] = _current;
        _current.OwnsMask = false;
    }

    /// <summary>Restores the clip of the matching <see cref="Save"/> (§8.4.2 <c>Q</c>).</summary>
    public void Restore()
    {
        if (_depth == 0)
        {
            return;
        }

        if (_current.OwnsMask)
        {
            Free(_current.Mask!);
        }

        _current = _stack[--_depth];
        _stack[_depth] = default;
        Refresh();
    }

    /// <summary>Intersects the clip with an axis-aligned rectangle in device pixels; no mask is involved.</summary>
    public void IntersectRectangle(double left, double top, double right, double bottom)
    {
        left += _originX;
        right += _originX;
        top += _originY;
        bottom += _originY;
        _current.Left = Math.Max(_current.Left, Math.Min(left, right));
        _current.Top = Math.Max(_current.Top, Math.Min(top, bottom));
        _current.Right = Math.Min(_current.Right, Math.Max(left, right));
        _current.Bottom = Math.Min(_current.Bottom, Math.Max(top, bottom));
        Refresh();
    }

    /// <summary>
    /// Intersects the clip with the inside of the shape <paramref name="shape"/> holds (its edges added in device pixels) under
    /// <paramref name="rule"/>, then clears the shape.
    /// </summary>
    public void IntersectPath(ScanlineRasterizer shape, FillRule rule)
    {
        if (IsEmpty || !shape.TryGetBounds(out int shapeLeft, out int shapeTop, out int shapeRight, out int shapeBottom))
        {
            SetEmpty();
            shape.Clear();
            return;
        }

        shapeLeft += _originX;
        shapeRight += _originX;
        shapeTop += _originY;
        shapeBottom += _originY;

        int left = Math.Max(_columnLeft, shapeLeft);
        int top = Math.Max(_rowTop, shapeTop);
        int right = Math.Min(_columnRight, shapeRight);
        int bottom = Math.Min(_rowBottom, shapeBottom);
        if (left >= right || top >= bottom)
        {
            SetEmpty();
            shape.Clear();
            return;
        }

        State target = _current;
        if (!_current.OwnsMask)
        {
            target.Mask = Rent((right - left) * (bottom - top));
            target.OriginX = left;
            target.OriginY = top;
            target.Stride = right - left;
            target.OwnsMask = true;
        }

        var sink = new MaskSink(in _current, in target, left, top, right, bottom, _originX, _originY);
        shape.Sweep(rule, ref sink);
        sink.Finish();
        target.MaskLeft = Math.Max(left, sink.MinX);
        target.MaskTop = Math.Max(top, sink.MinRow);
        target.MaskRight = Math.Min(right, sink.MaxX);
        target.MaskBottom = Math.Min(bottom, sink.MaxRow);
        _current = target;
        Refresh();
    }

    /// <summary>
    /// Hands a run of a shape's coverage to <paramref name="sink"/> cut by the clip: only the pixels inside it, each with the shape's
    /// coverage times the clip's, in runs of equal coverage above zero.
    /// </summary>
    /// <param name="row">The row, in the coordinates paints use (see <see cref="SetOrigin"/>).</param>
    /// <param name="x">The first pixel of the run.</param>
    /// <param name="count">The pixels in the run.</param>
    /// <param name="coverage">The shape's coverage over the run.</param>
    /// <param name="sink">What the paint does with each run.</param>
    /// <param name="hook">The transparency group the runs paint into, which sees every run before the sink (or <see langword="null"/>).</param>
    public void Apply<TSink>(int row, int x, int count, int coverage, ref TSink sink, GroupLayer? hook = null)
        where TSink : ICoverageSink, allows ref struct
    {
        row += _originY;
        x += _originX;
        if (row < _rowTop || row >= _rowBottom)
        {
            return;
        }

        int start = Math.Max(x, _columnLeft);
        int end = Math.Min(x + count, _columnRight);
        if (start >= end)
        {
            return;
        }

        double rowShare = Math.Min(_current.Bottom, row + 1) - Math.Max(_current.Top, row);
        int rowCoverage = rowShare >= 1 ? coverage : Scale(coverage, rowShare);
        if (rowCoverage <= 0)
        {
            return;
        }

        byte[]? mask = _current.Mask;
        if (mask is null)
        {
            if (start == _leftEdge)
            {
                Emit(ref sink, hook, row, start, 1, Scale(rowCoverage, ColumnShare(start)));
                start++;
            }

            bool rightPartial = end - 1 == _rightEdge && end > start;
            if (rightPartial)
            {
                end--;
            }

            Emit(ref sink, hook, row, start, end - start, rowCoverage);

            if (rightPartial)
            {
                Emit(ref sink, hook, row, end, 1, Scale(rowCoverage, ColumnShare(end)));
            }

            return;
        }

        // Runs of equal mask values (long ones inside and outside the clip) are found with a vectorized search.
        ReadOnlySpan<byte> line = mask.AsSpan(((row - _current.OriginY) * _current.Stride) - _current.OriginX + start, end - start);
        int column = start;
        while (column < end)
        {
            byte value = line[column - start];
            if (column == _leftEdge || column == _rightEdge)
            {
                Emit(ref sink, hook, row, column, 1, Divide255(Scale(rowCoverage, ColumnShare(column)) * value));
                column++;
                continue;
            }

            int limit = _rightEdge > column && _rightEdge < end ? _rightEdge : end;
            int length = line[(column + 1 - start)..(limit - start)].IndexOfAnyExcept(value);
            int runEnd = length < 0 ? limit : column + 1 + length;
            Emit(ref sink, hook, row, column, runEnd - column, rowCoverage == 255 ? value : Divide255(rowCoverage * value));
            column = runEnd;
        }
    }

    /// <summary>Returns every pooled mask buffer.</summary>
    public void Dispose()
    {
        Release();
        for (int index = 0; index < _freeCount; index++)
        {
            ArrayPool<byte>.Shared.Return(_free[index]);
        }

        Array.Clear(_free);
        _freeCount = 0;
    }

    /// <summary>Hands a run, in clip coordinates, to the sink (through the group hook, when there is one) in the coordinates paints use.</summary>
    private void Emit<TSink>(ref TSink sink, GroupLayer? hook, int row, int x, int count, int coverage)
        where TSink : ICoverageSink, allows ref struct
    {
        if (coverage <= 0 || count <= 0)
        {
            return;
        }

        SoftMaskPlane? mask = _softMask;
        if (mask is null)
        {
            Deliver(ref sink, hook, row, x, count, coverage, 255);
            return;
        }

        // Runs of equal mask values; an opacity of zero still has a shape for a knockout group to replace by.
        int end = x + count;
        while (x < end)
        {
            int next = mask.Run(row, x, end, out byte value);
            if (_softMaskIsShape)
            {
                Deliver(ref sink, hook, row, x, next - x, value == 255 ? coverage : Divide255(coverage * value), 255);
            }
            else
            {
                Deliver(ref sink, hook, row, x, next - x, coverage, value);
            }

            x = next;
        }
    }

    /// <summary>Hands one run to the hook (shape and opacity apart) or the sink (their product), in the coordinates paints use.</summary>
    private void Deliver<TSink>(ref TSink sink, GroupLayer? hook, int row, int x, int count, int coverage, int opacity)
        where TSink : ICoverageSink, allows ref struct
    {
        if (coverage <= 0)
        {
            return;
        }

        if (hook is not null)
        {
            hook.Span(row - _originY, x - _originX, count, coverage, ref sink, opacity);
            return;
        }

        int paint = opacity == 255 ? coverage : Divide255(coverage * opacity);
        if (paint > 0)
        {
            sink.Span(row - _originY, x - _originX, count, paint);
        }
    }

    private static int Scale(int coverage, double share) => (int)((coverage * share) + 0.5);

    /// <summary>Rounds <paramref name="value"/> / 255 for 0 ≤ value ≤ 255 × 255.</summary>
    private static byte Divide255(int value)
    {
        value += 128;
        return (byte)((value + (value >> 8)) >> 8);
    }

    /// <summary>The share of pixel column <paramref name="column"/> inside the clip rectangle.</summary>
    private double ColumnShare(int column) => Math.Clamp(Math.Min(_current.Right, column + 1) - Math.Max(_current.Left, column), 0, 1);

    /// <summary>Makes the clip empty: nothing painted until the state is restored.</summary>
    private void SetEmpty()
    {
        if (_current.OwnsMask)
        {
            Free(_current.Mask!);
        }

        _current = default;
        Refresh();
    }

    /// <summary>Recomputes the pixel ranges the clip can cover from the rectangle and the mask's bounds.</summary>
    private void Refresh()
    {
        ref readonly State state = ref _current;
        _rowTop = (int)Math.Floor(Math.Clamp(state.Top, 0, _height));
        _rowBottom = (int)Math.Ceiling(Math.Clamp(state.Bottom, 0, _height));
        _columnLeft = (int)Math.Floor(Math.Clamp(state.Left, 0, _width));
        _columnRight = (int)Math.Ceiling(Math.Clamp(state.Right, 0, _width));
        _leftEdge = state.Left > _columnLeft ? _columnLeft : -1;
        _rightEdge = state.Right < _columnRight ? _columnRight - 1 : -1;
        if (state.Mask is not null)
        {
            _rowTop = Math.Max(_rowTop, state.MaskTop);
            _rowBottom = Math.Min(_rowBottom, state.MaskBottom);
            _columnLeft = Math.Max(_columnLeft, state.MaskLeft);
            _columnRight = Math.Min(_columnRight, state.MaskRight);
        }

        IsWholeBitmap = _softMask is null && state.Mask is null && state.Left <= 0 && state.Top <= 0 && state.Right >= _width && state.Bottom >= _height;
    }

    /// <summary>Takes the smallest free buffer of at least <paramref name="size"/> bytes, or a pooled one.</summary>
    private byte[] Rent(int size)
    {
        int best = -1;
        for (int index = 0; index < _freeCount; index++)
        {
            if (_free[index].Length >= size && (best < 0 || _free[index].Length < _free[best].Length))
            {
                best = index;
            }
        }

        if (best < 0)
        {
            return ArrayPool<byte>.Shared.Rent(size);
        }

        byte[] buffer = _free[best];
        _free[best] = _free[--_freeCount];
        _free[_freeCount] = null!;
        return buffer;
    }

    private void Free(byte[] buffer)
    {
        if (_freeCount == MaxFree)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            return;
        }

        if (_freeCount == _free.Length)
        {
            Array.Resize(ref _free, _free.Length * 2);
        }

        _free[_freeCount++] = buffer;
    }

    /// <summary>One clip: the device rectangle and the optional mask with the pixels it can be above zero in.</summary>
    private struct State
    {
        public double Left;
        public double Top;
        public double Right;
        public double Bottom;
        public byte[]? Mask;
        public int OriginX;
        public int OriginY;
        public int Stride;
        public int MaskLeft;
        public int MaskTop;
        public int MaskRight;
        public int MaskBottom;
        public bool OwnsMask;
    }

    /// <summary>
    /// Writes the intersection of the old mask (or full coverage) with a shape's coverage into the new mask over a pixel rectangle,
    /// zeroing what the shape does not cover, and tracks where the result can be above zero.
    /// </summary>
    private struct MaskSink : ICoverageSink
    {
        private readonly byte[]? _source;
        private readonly int _sourceOffsetX;
        private readonly int _sourceOriginY;
        private readonly int _sourceStride;
        private readonly byte[] _target;
        private readonly int _targetOriginX;
        private readonly int _targetOriginY;
        private readonly int _targetStride;
        private readonly int _left;
        private readonly int _top;
        private readonly int _right;
        private readonly int _bottom;
        private readonly int _shiftX;
        private readonly int _shiftY;
        private int _row;

        public MaskSink(in State source, in State target, int left, int top, int right, int bottom, int shiftX, int shiftY)
        {
            _shiftX = shiftX;
            _shiftY = shiftY;
            _source = source.Mask;
            _sourceOffsetX = source.OriginX;
            _sourceOriginY = source.OriginY;
            _sourceStride = source.Stride;
            _target = target.Mask!;
            _targetOriginX = target.OriginX;
            _targetOriginY = target.OriginY;
            _targetStride = target.Stride;
            _left = left;
            _top = top;
            _right = right;
            _bottom = bottom;
            _row = top - 1;
            MinX = int.MaxValue;
            MinRow = int.MaxValue;
            MaxX = int.MinValue;
            MaxRow = int.MinValue;
        }

        public int MinX { get; private set; }

        public int MaxX { get; private set; }

        public int MinRow { get; private set; }

        public int MaxRow { get; private set; }

        public void Span(int row, int x, int count, int coverage)
        {
            row += _shiftY;
            x += _shiftX;
            if (row < _top || row >= _bottom)
            {
                return;
            }

            if (row != _row)
            {
                // The first run of a row starts at its first cell: zero the rows the shape skipped and the pixels left of the run.
                ZeroRows(_row + 1, row);
                _row = row;
                if (x > _left)
                {
                    Target(row, _left, Math.Min(x, _right)).Clear();
                }
            }

            int start = Math.Max(x, _left);
            int end = Math.Min(x + count, _right);
            if (start >= end)
            {
                return;
            }

            Span<byte> target = Target(row, start, end);
            if (coverage <= 0)
            {
                target.Clear();
                return;
            }

            if (_source is null)
            {
                target.Fill((byte)coverage);
            }
            else if (coverage == 255)
            {
                if (!ReferenceEquals(_source, _target))
                {
                    _source.AsSpan(((row - _sourceOriginY) * _sourceStride) + start - _sourceOffsetX, end - start).CopyTo(target);
                }
            }
            else
            {
                ReadOnlySpan<byte> source = _source.AsSpan(((row - _sourceOriginY) * _sourceStride) + start - _sourceOffsetX, end - start);
                for (int index = 0; index < target.Length; index++)
                {
                    target[index] = Divide255(source[index] * coverage);
                }
            }

            MinX = Math.Min(MinX, start);
            MaxX = Math.Max(MaxX, end);
            MinRow = Math.Min(MinRow, row);
            MaxRow = Math.Max(MaxRow, row + 1);
        }

        /// <summary>Zeroes the rows below the last one the shape reached.</summary>
        public readonly void Finish() => ZeroRows(_row + 1, _bottom);

        private readonly void ZeroRows(int from, int to)
        {
            for (int row = Math.Max(from, _top); row < to; row++)
            {
                Target(row, _left, _right).Clear();
            }
        }

        private readonly Span<byte> Target(int row, int start, int end) =>
            _target.AsSpan(((row - _targetOriginY) * _targetStride) + start - _targetOriginX, end - start);
    }
}
