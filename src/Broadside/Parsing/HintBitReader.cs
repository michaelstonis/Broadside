namespace Broadside.Parsing;

/// <summary>Reads unsigned fields of 0 to 32 bits from hint table data, most significant bit first (F.4.1).</summary>
/// <remarks>
/// ISO 32000-2 F.4.1. A field of width 0 reads 0 and consumes nothing. Reading past the end, or a field width over 32, sets
/// <see cref="Failed"/> and reads 0 from then on, so a caller checks once after a group of reads.
/// </remarks>
internal ref struct HintBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private long _bit;

    /// <summary>Initializes a new instance of the <see cref="HintBitReader"/> struct at the start of <paramref name="data"/>.</summary>
    /// <param name="data">The hint table data.</param>
    public HintBitReader(ReadOnlySpan<byte> data) => _data = data;

    /// <summary>Gets a value indicating whether a read ran past the data or asked for an invalid width.</summary>
    public bool Failed { get; private set; }

    /// <summary>Reads an unsigned field.</summary>
    /// <param name="width">The width in bits, 0 to 32.</param>
    /// <returns>The value, or 0 once <see cref="Failed"/>.</returns>
    public long Read(int width)
    {
        if (Failed || width is < 0 or > 32 || _bit + width > (long)_data.Length * 8)
        {
            Failed = true;
            return 0;
        }

        long value = 0;
        for (int index = 0; index < width; index++, _bit++)
        {
            int bit = (_data[(int)(_bit >> 3)] >> (7 - (int)(_bit & 7))) & 1;
            value = (value << 1) | (long)bit;
        }

        return value;
    }

    /// <summary>Reads a 16-bit "number of bits" header item, which shall be 0 to 32 (F.4.2).</summary>
    /// <returns>The width; an out-of-range value sets <see cref="Failed"/>.</returns>
    public int ReadWidth()
    {
        long width = Read(16);
        if (width > 32)
        {
            Failed = true;
            return 0;
        }

        return (int)width;
    }

    /// <summary>Reads <paramref name="count"/> fields of <paramref name="width"/> bits, then moves to the next byte boundary.</summary>
    /// <param name="count">The number of fields.</param>
    /// <param name="width">The width of each.</param>
    /// <returns>The values.</returns>
    public long[] ReadColumn(int count, int width)
    {
        if (Failed || (long)count * width > ((long)_data.Length * 8) - _bit)
        {
            Failed = true;
            return new long[count];
        }

        long[] values = new long[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = Read(width);
        }

        AlignToByte();
        return values;
    }

    /// <summary>Skips <paramref name="bits"/> bits.</summary>
    /// <param name="bits">How many.</param>
    public void Skip(int bits)
    {
        if (Failed || _bit + bits > (long)_data.Length * 8)
        {
            Failed = true;
            return;
        }

        _bit += bits;
    }

    /// <summary>Moves to the next byte boundary, unless already on one.</summary>
    public void AlignToByte() => _bit = (_bit + 7) & ~7L;
}
