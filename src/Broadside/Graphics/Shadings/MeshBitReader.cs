namespace Broadside.Graphics.Shadings;

/// <summary>
/// Reads unsigned fields of 1 to 32 bits, most significant bit first, from mesh data: the bit stream of ISO 32000-2 §8.7.4.5.5
/// to §8.7.4.5.8. Allocation-free.
/// </summary>
internal ref struct MeshBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private long _position;

    public MeshBitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>Gets the number of bits not read yet.</summary>
    public readonly long RemainingBits => ((long)_data.Length * 8) - _position;

    /// <summary>Gets the position, in bits from the start.</summary>
    public readonly long Position => _position;

    /// <summary>Reads a field; the caller checks <see cref="RemainingBits"/> first.</summary>
    /// <param name="width">The field's width, 1 to 32.</param>
    /// <returns>The field's value.</returns>
    public uint Read(int width)
    {
        ulong value = 0;
        int left = width;
        while (left > 0)
        {
            int index = (int)(_position >> 3);
            int offset = (int)(_position & 7);
            int available = 8 - offset;
            int take = Math.Min(available, left);
            int bits = (_data[index] >> (available - take)) & ((1 << take) - 1);
            value = (value << take) | (uint)bits;
            left -= take;
            _position += take;
        }

        return (uint)value;
    }

    /// <summary>Skips to the next byte boundary (§8.7.4.5.5: each vertex occupies a whole number of bytes).</summary>
    public void AlignToByte() => _position = (_position + 7) & ~7L;

    /// <summary>Moves to a position, in bits from the start.</summary>
    public void Seek(long position) => _position = position;
}
