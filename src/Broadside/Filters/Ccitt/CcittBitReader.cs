using System.Buffers.Binary;
using System.Numerics;

namespace Broadside.Filters.Ccitt;

/// <summary>
/// Reads fax data MSB first (PDF has no fill order) from a span, by absolute bit position: peeking past the end reads zeros, and a
/// code is known to be whole only when <see cref="BitsLeft"/> covers its length. Saving <see cref="Position"/> and assigning it back
/// is how the decoder looks ahead.
/// </summary>
/// <remarks>ISO 32000-2 §7.4.6 (the data is a continuous bit stream); ITU-T T.4 §4, T.6 §2.</remarks>
internal ref struct CcittBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly long _totalBits;

    /// <summary>Initializes a new instance of the <see cref="CcittBitReader"/> struct.</summary>
    /// <param name="data">The encoded data.</param>
    public CcittBitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _totalBits = (long)data.Length * 8;
        Position = 0;
    }

    /// <summary>Gets or sets the absolute bit position of the next bit.</summary>
    public long Position { get; set; }

    /// <summary>Gets the number of bits after <see cref="Position"/> (0 at or past the end).</summary>
    public readonly long BitsLeft => Math.Max(0, _totalBits - Position);

    /// <summary>Gets the number of whole bytes up to <see cref="Position"/>, the last partly read byte included.</summary>
    public readonly int BytesConsumed => (int)Math.Min(_data.Length, (Position + 7) >> 3);

    /// <summary>Returns the next <paramref name="count"/> bits (1 to 25) without consuming them; bits past the end read as 0.</summary>
    /// <param name="count">How many bits.</param>
    /// <returns>The bits, right-aligned.</returns>
    public readonly int Peek(int count)
    {
        long position = Position;
        int index = (int)Math.Min(position >> 3, int.MaxValue);
        uint word;
        if ((long)index + 4 <= _data.Length)
        {
            word = BinaryPrimitives.ReadUInt32BigEndian(_data[index..]);
        }
        else
        {
            word = 0;
            for (int i = 0; i < 4; i++)
            {
                long at = (long)index + i;
                word = (word << 8) | (at < _data.Length ? _data[(int)at] : 0u);
            }
        }

        return (int)((word << (int)(position & 7)) >> (32 - count));
    }

    /// <summary>Consumes <paramref name="count"/> bits.</summary>
    /// <param name="count">How many bits.</param>
    public void Skip(int count) => Position += count;

    /// <summary>Reads one bit (0 past the end).</summary>
    /// <returns>The bit.</returns>
    public int ReadBit()
    {
        int bit = Peek(1);
        Position++;
        return bit;
    }

    /// <summary>Moves to the next byte boundary, unless already on one.</summary>
    public void AlignToByte() => Position = (Position + 7) & ~7L;

    /// <summary>Counts the zero bits from <see cref="Position"/>, up to the first 1 bit or the end of the data, without consuming them.</summary>
    /// <param name="limit">Stop counting at this many zeros.</param>
    /// <returns>The number of zeros; equal to <see cref="BitsLeft"/> when only zeros remain (and less than <paramref name="limit"/>).</returns>
    public readonly long CountZeros(long limit)
    {
        long count = 0;
        long position = Position;
        while (count < limit && position < _totalBits)
        {
            int index = (int)(position >> 3);
            int offset = (int)(position & 7);
            int bits = (byte)(_data[index] << offset);
            if (bits != 0)
            {
                return Math.Min(limit, count + BitOperations.LeadingZeroCount((uint)bits) - 24);
            }

            int zeros = 8 - offset;
            count += zeros;
            position += zeros;
        }

        return Math.Min(count, limit);
    }

    /// <summary>Gets a value indicating whether every bit from <see cref="Position"/> to the end of the data is 0 (true at the end).</summary>
    public readonly bool OnlyZerosLeft => CountZeros(long.MaxValue) >= BitsLeft;
}
