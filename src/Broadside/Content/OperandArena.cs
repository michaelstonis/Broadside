using System.Buffers;

namespace Broadside.Content;

/// <summary>One operand or container element in the <see cref="OperandArena"/>, in parse order.</summary>
internal struct OperandEntry
{
    /// <summary>The kind.</summary>
    public ContentOperandKind Kind;

    /// <summary>Whether a string was written in hexadecimal.</summary>
    public bool IsHexadecimal;

    /// <summary>The value of a number, or 1/0 for a boolean.</summary>
    public double Number;

    /// <summary>The offset of a name's or string's decoded bytes in the arena's byte buffer.</summary>
    public int BytesStart;

    /// <summary>The length of a name's or string's decoded bytes.</summary>
    public int BytesLength;

    /// <summary>The number of direct elements of an array or dictionary.</summary>
    public int Count;

    /// <summary>The index one past the entry and everything it contains: the next sibling.</summary>
    public int End;
}

/// <summary>
/// The operands read since the last operator, held flat and without objects: numbers inline, names and strings as decoded byte
/// ranges in one buffer, arrays and dictionaries as entry ranges. Pooled buffers grow when needed and are reused, so reading
/// operands allocates nothing once warmed up.
/// </summary>
/// <remarks>ISO 32000-2 §7.8.2: operands precede their operator; none are left over when it has executed.</remarks>
internal sealed class OperandArena
{
    private OperandEntry[] _entries = ArrayPool<OperandEntry>.Shared.Rent(64);
    private int[] _topLevel = ArrayPool<int>.Shared.Rent(16);
    private byte[] _bytes = ArrayPool<byte>.Shared.Rent(256);
    private int[] _open = ArrayPool<int>.Shared.Rent(8);
    private int _count;
    private int _topCount;
    private int _byteCount;
    private int _depth;
    private int _droppedDepth;

    /// <summary>Gets or sets the most entries (operands and container elements together) held before older ones are dropped.</summary>
    public int Limit { get; set; } = ContentOptions.DefaultMaxOperands;

    /// <summary>Gets the number of top-level operands.</summary>
    public int Count => _topCount;

    /// <summary>Gets the nesting depth of arrays and dictionaries still open.</summary>
    public int OpenDepth => _depth;

    /// <summary>Gets a value indicating whether the innermost open container is a dictionary.</summary>
    public bool InDictionary => _depth > 0 && _entries[_open[_depth - 1]].Kind == ContentOperandKind.Dictionary;

    /// <summary>Gets or sets a value indicating whether operands were dropped because <see cref="Limit"/> was reached.</summary>
    public bool Overflowed { get; set; }

    /// <summary>Gets all top-level operands.</summary>
    public ContentOperands Operands => new(_entries.AsSpan(0, _count), _bytes.AsSpan(0, _byteCount), _topLevel.AsSpan(0, _topCount));

    /// <summary>Gets the last <paramref name="count"/> top-level operands.</summary>
    public ContentOperands Last(int count) =>
        new(_entries.AsSpan(0, _count), _bytes.AsSpan(0, _byteCount), _topLevel.AsSpan(_topCount - count, count));

    /// <summary>Drops every operand.</summary>
    public void Clear()
    {
        _count = 0;
        _topCount = 0;
        _byteCount = 0;
        _depth = 0;
        _droppedDepth = 0;
    }

    /// <summary>Gives oversized buffers back to the pool, after a run that needed them.</summary>
    public void Trim()
    {
        Clear();
        Shrink(ref _entries, 4096, 64);
        Shrink(ref _topLevel, 4096, 16);
        Shrink(ref _bytes, 1 << 16, 256);
    }

    /// <summary>Adds a number.</summary>
    public void AddNumber(double value, bool isInteger) =>
        Add(new OperandEntry { Kind = isInteger ? ContentOperandKind.Integer : ContentOperandKind.Real, Number = value });

    /// <summary>Adds a boolean.</summary>
    public void AddBoolean(bool value) => Add(new OperandEntry { Kind = ContentOperandKind.Boolean, Number = value ? 1 : 0 });

    /// <summary>Adds the null object.</summary>
    public void AddNull() => Add(new OperandEntry { Kind = ContentOperandKind.Null });

    /// <summary>Returns room for at least <paramref name="length"/> decoded bytes of a name or string; commit with <see cref="CommitBytes"/>.</summary>
    public Span<byte> ReserveBytes(int length)
    {
        if (_bytes.Length - _byteCount < length)
        {
            Grow(ref _bytes, _byteCount, _byteCount + length);
        }

        return _bytes.AsSpan(_byteCount);
    }

    /// <summary>Adds a name or string whose <paramref name="length"/> decoded bytes were written into <see cref="ReserveBytes"/>.</summary>
    public void CommitBytes(ContentOperandKind kind, int length, bool hexadecimal = false)
    {
        int written = _byteCount;
        if (!MakeRoom())
        {
            return;
        }

        if (_byteCount != written)
        {
            // The pending operands were dropped to make room: move the new bytes to the start of the emptied buffer.
            _bytes.AsSpan(written, length).CopyTo(_bytes);
        }

        Add(new OperandEntry { Kind = kind, BytesStart = _byteCount, BytesLength = length, IsHexadecimal = hexadecimal });
        _byteCount += length;
    }

    /// <summary>Opens an array or dictionary; later entries are its elements until <see cref="EndContainer"/>.</summary>
    public void BeginContainer(ContentOperandKind kind)
    {
        if (_droppedDepth > 0 || !Add(new OperandEntry { Kind = kind }))
        {
            _droppedDepth++;
            return;
        }

        if (_depth == _open.Length)
        {
            Grow(ref _open, _depth, _depth * 2);
        }

        _open[_depth++] = _count - 1;
    }

    /// <summary>Closes the innermost open container if it is of <paramref name="kind"/>; returns false, changing nothing, if it is not.</summary>
    public bool EndContainer(ContentOperandKind kind)
    {
        if (_droppedDepth > 0)
        {
            _droppedDepth--;
            return true;
        }

        if (_depth == 0 || _entries[_open[_depth - 1]].Kind != kind)
        {
            return false;
        }

        _entries[_open[--_depth]].End = _count;
        return true;
    }

    /// <summary>Closes every open container; returns false when there was none.</summary>
    public bool CloseAll()
    {
        bool any = _depth > 0 || _droppedDepth > 0;
        while (_depth > 0)
        {
            _entries[_open[--_depth]].End = _count;
        }

        _droppedDepth = 0;
        return any;
    }

    private static void Grow<T>(ref T[] array, int used, int minimum)
    {
        T[] larger = ArrayPool<T>.Shared.Rent(Math.Max(minimum, array.Length * 2));
        array.AsSpan(0, used).CopyTo(larger);
        ArrayPool<T>.Shared.Return(array);
        array = larger;
    }

    private static void Shrink<T>(ref T[] array, int threshold, int size)
    {
        if (array.Length > threshold)
        {
            ArrayPool<T>.Shared.Return(array);
            array = ArrayPool<T>.Shared.Rent(size);
        }
    }

    /// <summary>Makes room for one entry; false when there is none because the arena is full inside a container.</summary>
    private bool MakeRoom()
    {
        if (_droppedDepth > 0)
        {
            return false;
        }

        if (_count >= Limit)
        {
            Overflowed = true;
            if (_depth > 0)
            {
                return false;
            }

            // At the top level the oldest operands go: all those pending are dropped, so the operator that follows gets the newest.
            Clear();
        }

        return true;
    }

    /// <summary>Adds an entry; false when it was dropped because the arena is full inside a container.</summary>
    private bool Add(OperandEntry entry)
    {
        if (!MakeRoom())
        {
            return false;
        }

        if (_count == _entries.Length)
        {
            Grow(ref _entries, _count, _count * 2);
        }

        int index = _count++;
        entry.End = _count;
        _entries[index] = entry;
        if (_depth == 0)
        {
            if (_topCount == _topLevel.Length)
            {
                Grow(ref _topLevel, _topCount, _topCount * 2);
            }

            _topLevel[_topCount++] = index;
        }
        else
        {
            _entries[_open[_depth - 1]].Count++;
        }

        return true;
    }
}
