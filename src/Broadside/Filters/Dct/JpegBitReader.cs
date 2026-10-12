using System.Runtime.CompilerServices;

namespace Broadside.Filters.Dct;

/// <summary>
/// Reads the entropy-coded segment of a scan MSB first: removes the stuffed zero after each <c>FF</c> data byte, stops in front of
/// the first marker without consuming it, and from there (or the end of the data) supplies zero bits, recording that it did.
/// </summary>
/// <remarks>
/// ITU-T T.81 §B.1.1.5 (byte stuffing), §F.2.2.5 (Figure F.18, NEXTBIT, which detects markers), §F.2.2.1 and §F.2.2.4 (Figures F.12
/// and F.17: RECEIVE and EXTEND), §F.2.2.3 (Figure F.16: DECODE). Feeding zeros at a marker is libjpeg's behaviour; a decoder
/// that consumed one of them has run out of data (<see cref="Overread"/>).
/// </remarks>
internal ref struct JpegBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private ulong _buffer;
    private int _bits;
    private int _padding;
    private bool _atMarker;

    /// <summary>Initializes a new instance of the <see cref="JpegBitReader"/> struct at the start of an entropy-coded segment.</summary>
    /// <param name="data">The whole JPEG data.</param>
    /// <param name="position">Where the segment starts.</param>
    public JpegBitReader(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        _position = position;
        _buffer = 0;
        _bits = 0;
        _padding = 0;
        _atMarker = false;
    }

    /// <summary>Gets a value indicating whether a read consumed bits beyond the data (the zeros supplied after a marker or the end).</summary>
    public readonly bool Overread => _padding > _bits;

    /// <summary>Gets a value indicating whether the reader stopped in front of a marker or the end of the data.</summary>
    public readonly bool AtMarker => _atMarker;

    /// <summary>Gets the position of the first byte not yet loaded.</summary>
    public readonly int Position => _position;

    /// <summary>Gets the number of whole data bytes loaded but not consumed (excluding supplied zeros).</summary>
    public readonly int UnusedBytes => Math.Max(0, _bits - _padding) / 8;

    /// <summary>Decodes one Huffman-coded symbol.</summary>
    /// <param name="table">The table.</param>
    /// <param name="valid">Set to <see langword="false"/> when the bits are not a code of the table.</param>
    /// <returns>The symbol; 0 for an invalid code (libjpeg's safest substitute).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(HuffmanTable table, ref bool valid)
    {
        if (_bits < 16)
        {
            Fill();
        }

        int entry = table.Lookup[(int)(_buffer >> (64 - HuffmanTable.LookupBits))];
        if (entry != 0)
        {
            int length = entry >> 8;
            _buffer <<= length;
            _bits -= length;
            return entry & 0xFF;
        }

        return DecodeLong(table, ref valid);
    }

    /// <summary>Reads <paramref name="count"/> bits (0 to 16) as an unsigned number.</summary>
    /// <param name="count">The number of bits.</param>
    /// <returns>The bits.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Receive(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        if (_bits < count)
        {
            Fill();
        }

        int value = (int)(_buffer >> (64 - count));
        _buffer <<= count;
        _bits -= count;
        return value;
    }

    /// <summary>Reads <paramref name="count"/> bits and sign-extends them as the difference or coefficient of that category.</summary>
    /// <param name="count">The category (SSSS), 0 to 16.</param>
    /// <returns>The value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReceiveExtend(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        int value = Receive(count);
        return value < 1 << (count - 1) ? value - (1 << count) + 1 : value;
    }

    /// <summary>Reads one bit.</summary>
    /// <returns>The bit.</returns>
    public int ReadBit() => Receive(1);

    private int DecodeLong(HuffmanTable table, ref bool valid)
    {
        int code = (int)(_buffer >> 48);
        int[] maxCode = table.MaxCode;
        for (int length = HuffmanTable.LookupBits + 1; length <= 16; length++)
        {
            int prefix = code >> (16 - length);
            if (prefix <= maxCode[length])
            {
                _buffer <<= length;
                _bits -= length;
                int index = table.ValueOffset[length] + prefix;
                if ((uint)index < 256u)
                {
                    return table.Values[index];
                }

                break;
            }
        }

        _buffer <<= 16;
        _bits -= 16;
        valid = false;
        return 0;
    }

    private void Fill()
    {
        while (_bits <= 56)
        {
            if (_atMarker)
            {
                _bits += 8;
                _padding += 8;
                continue;
            }

            if (_position >= _data.Length)
            {
                _atMarker = true;
                continue;
            }

            byte value = _data[_position];
            if (value == 0xFF)
            {
                if (_position + 1 < _data.Length && _data[_position + 1] == 0)
                {
                    _position += 2;
                }
                else
                {
                    _atMarker = true;
                    continue;
                }
            }
            else
            {
                _position++;
            }

            _buffer |= (ulong)value << (56 - _bits);
            _bits += 8;
        }
    }
}
