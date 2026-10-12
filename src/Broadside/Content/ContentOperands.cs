using System.Diagnostics.CodeAnalysis;

namespace Broadside.Content;

/// <summary>
/// A list of content stream operands, or of the elements of an array or dictionary operand, read in place. Valid only during the
/// callback that received it.
/// </summary>
/// <remarks>ISO 32000-2 §7.8.2.</remarks>
public readonly ref struct ContentOperands
{
    private readonly ReadOnlySpan<OperandEntry> _entries;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly ReadOnlySpan<int> _indices;
    private readonly int _first;
    private readonly bool _indexed;

    /// <summary>Initializes a new instance of the <see cref="ContentOperands"/> struct over top-level operands at <paramref name="indices"/>.</summary>
    internal ContentOperands(ReadOnlySpan<OperandEntry> entries, ReadOnlySpan<byte> bytes, ReadOnlySpan<int> indices)
    {
        _entries = entries;
        _bytes = bytes;
        _indices = indices;
        _indexed = true;
        Count = indices.Length;
    }

    /// <summary>Initializes a new instance of the <see cref="ContentOperands"/> struct over the <paramref name="count"/> siblings from <paramref name="first"/>.</summary>
    internal ContentOperands(ReadOnlySpan<OperandEntry> entries, ReadOnlySpan<byte> bytes, int first, int count)
    {
        _entries = entries;
        _bytes = bytes;
        _first = first;
        Count = count;
    }

    /// <summary>Gets the number of operands.</summary>
    public int Count { get; }

    /// <summary>Gets the operand at <paramref name="index"/>.</summary>
    /// <param name="index">The position, from 0.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public ContentOperand this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return new ContentOperand(_entries, _bytes, EntryIndex(index));
        }
    }

    /// <summary>Returns an enumerator over the operands.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    private int EntryIndex(int index)
    {
        if (_indexed)
        {
            return _indices[index];
        }

        int entry = _first;
        for (int step = 0; step < index; step++)
        {
            entry = _entries[entry].End;
        }

        return entry;
    }

    /// <summary>Enumerates operands in order without allocating.</summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "The enumerator pattern of Span<T>: found by foreach, never named.")]
    public ref struct Enumerator
    {
        private readonly ContentOperands _operands;
        private int _position;
        private int _entry;

        internal Enumerator(ContentOperands operands)
        {
            _operands = operands;
            _position = -1;
            _entry = -1;
        }

        /// <summary>Gets the current operand.</summary>
        public readonly ContentOperand Current => new(_operands._entries, _operands._bytes, _entry);

        /// <summary>Advances to the next operand.</summary>
        /// <returns><see langword="false"/> at the end.</returns>
        public bool MoveNext()
        {
            if (++_position >= _operands.Count)
            {
                return false;
            }

            _entry = _operands._indexed ? _operands._indices[_position] : _position == 0 ? _operands._first : _operands._entries[_entry].End;
            return true;
        }
    }
}
