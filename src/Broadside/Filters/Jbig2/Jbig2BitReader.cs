namespace Broadside.Filters.Jbig2;

/// <summary>
/// An MSB-first bit reader over the data part of a Huffman-coded JBIG2 segment (ITU-T T.88 §5.2: bits are read from the most
/// significant end of each byte), with byte alignment and byte skips for the parts whose sizes are given in bytes (BMSIZE, RSIZE).
/// </summary>
/// <remarks>
/// Past the end it reads 0 bits and counts them, so a caller decoding a count of items from damaged data can stop
/// (<see cref="IsExhausted"/>). Allocates nothing.
/// </remarks>
internal ref struct Jbig2BitReader
{
    /// <summary>How many bits past the end a reader may read before it is <see cref="IsExhausted"/>.</summary>
    public const int OverrunAllowance = 1 << 13;

    private readonly ReadOnlySpan<byte> _data;
    private long _bit;

    /// <summary>Initializes a new instance of the <see cref="Jbig2BitReader"/> struct at the first bit of <paramref name="data"/>.</summary>
    /// <param name="data">The bytes to read.</param>
    public Jbig2BitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _bit = 0;
    }

    /// <summary>Gets the offset of the byte holding the next bit (after <see cref="Align"/>, the next byte).</summary>
    public readonly int ByteOffset => (int)Math.Min(int.MaxValue, _bit >> 3);

    /// <summary>Gets the data not read yet, from the next byte boundary.</summary>
    public readonly ReadOnlySpan<byte> Remaining => _data[Math.Min(_data.Length, (int)Math.Min(int.MaxValue, (_bit + 7) >> 3))..];

    /// <summary>Gets a value indicating whether the reader has read past the end of its data at all.</summary>
    public readonly bool IsPastEnd => _bit > (long)_data.Length * 8;

    /// <summary>Gets a value indicating whether the reader has read far past the end of its data: the data is damaged or truncated.</summary>
    public readonly bool IsExhausted => _bit > ((long)_data.Length * 8) + OverrunAllowance;

    /// <summary>Reads one bit; 0 past the end.</summary>
    public int ReadBit()
    {
        long bit = _bit++;
        long index = bit >> 3;
        return index < _data.Length ? (_data[(int)index] >> (7 - (int)(bit & 7))) & 1 : 0;
    }

    /// <summary>Reads <paramref name="count"/> bits (0 to 32), most significant first.</summary>
    /// <param name="count">The number of bits.</param>
    /// <returns>The bits as an unsigned value.</returns>
    public uint ReadBits(int count)
    {
        uint value = 0;
        for (int i = 0; i < count; i++)
        {
            value = (value << 1) | (uint)ReadBit();
        }

        return value;
    }

    /// <summary>Skips the bits left in the current byte (T.88 "skip over any bits remaining in the last byte read").</summary>
    public void Align() => _bit = (_bit + 7) & ~7L;

    /// <summary>Moves to the byte boundary <paramref name="bytes"/> bytes after the current (aligned) byte offset.</summary>
    /// <param name="bytes">The number of bytes to skip.</param>
    public void SkipBytes(long bytes)
    {
        Align();
        _bit += bytes * 8;
    }
}
