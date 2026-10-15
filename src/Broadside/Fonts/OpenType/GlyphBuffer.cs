using System.Buffers;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// The glyphs of one run while it is shaped, in logical order: each glyph's id, the cluster (UTF-16 index of the first character it
/// came from), the feature mask selecting which lookups apply to it, and its glyph class. The storage is pooled and reused by the
/// thread that rents it, so shaping allocates nothing per glyph once the buffer has grown to the longest run.
/// </summary>
internal sealed class GlyphBuffer
{
    [ThreadStatic]
    private static GlyphBuffer? _cached;

    private ShapingGlyph[] _items = [];

    /// <summary>Gets the number of glyphs.</summary>
    public int Length { get; private set; }

    /// <summary>Gets the glyphs.</summary>
    public Span<ShapingGlyph> Glyphs => _items.AsSpan(0, Length);

    /// <summary>Gets or sets the number of glyphs past which multiple substitutions stop growing the run.</summary>
    public int MaxLength { get; set; } = int.MaxValue;

    /// <summary>Gets the glyph at <paramref name="index"/>.</summary>
    public ref ShapingGlyph this[int index] => ref _items[index];

    /// <summary>Takes this thread's buffer (or a new one), empty.</summary>
    public static GlyphBuffer Rent()
    {
        GlyphBuffer buffer = _cached ?? new GlyphBuffer();
        _cached = null;
        buffer.Length = 0;
        buffer.MaxLength = int.MaxValue;
        return buffer;
    }

    /// <summary>Gives the buffer back to this thread for the next run.</summary>
    public static void Return(GlyphBuffer buffer) => _cached = buffer;

    /// <summary>Appends a glyph.</summary>
    public void Add(in ShapingGlyph glyph)
    {
        EnsureCapacity(Length + 1);
        _items[Length++] = glyph;
    }

    /// <summary>Replaces the glyph at <paramref name="index"/> by <paramref name="count"/> copies of it (to be overwritten), or removes it for 0.</summary>
    /// <returns>Whether the run had room (it stays below <see cref="MaxLength"/>).</returns>
    public bool Expand(int index, int count)
    {
        if (count == 1)
        {
            return true;
        }

        if (count == 0)
        {
            RemoveAt(index);
            return true;
        }

        if (Length + count - 1 > MaxLength)
        {
            return false;
        }

        EnsureCapacity(Length + count - 1);
        Array.Copy(_items, index + 1, _items, index + count, Length - index - 1);
        for (int offset = 1; offset < count; offset++)
        {
            _items[index + offset] = _items[index];
        }

        Length += count - 1;
        return true;
    }

    /// <summary>Removes the glyph at <paramref name="index"/>.</summary>
    public void RemoveAt(int index)
    {
        Array.Copy(_items, index + 1, _items, index, Length - index - 1);
        Length--;
    }

    /// <summary>Shortens the run to <paramref name="length"/> glyphs.</summary>
    public void Truncate(int length) => Length = length;

    private void EnsureCapacity(int capacity)
    {
        if (capacity <= _items.Length)
        {
            return;
        }

        ShapingGlyph[] grown = ArrayPool<ShapingGlyph>.Shared.Rent(Math.Max(capacity, Math.Max(16, _items.Length * 2)));
        Array.Copy(_items, grown, Length);
        if (_items.Length > 0)
        {
            ArrayPool<ShapingGlyph>.Shared.Return(_items);
        }

        _items = grown;
    }
}

/// <summary>A glyph of a <see cref="GlyphBuffer"/>.</summary>
internal struct ShapingGlyph
{
    /// <summary>The glyph id.</summary>
    public int Glyph;

    /// <summary>The UTF-16 index of the first character the glyph came from.</summary>
    public int Cluster;

    /// <summary>The features that apply to the glyph (<see cref="ShapePlan"/> masks).</summary>
    public uint Mask;

    /// <summary>The glyph class (<see cref="GdefTable.BaseGlyph"/>, <see cref="GdefTable.LigatureGlyph"/>, <see cref="GdefTable.MarkGlyph"/>, 4 component).</summary>
    public int Class;
}
