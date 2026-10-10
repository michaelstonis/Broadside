namespace Broadside.Filters.Jpx;

/// <summary>
/// Reads the bits of a packet header, MSB first, skipping the stuffed bit after every 0xFF byte. Past the end of the data it returns
/// zeros and sets <see cref="Overrun"/>.
/// </summary>
/// <remarks>ITU-T T.800 B.10.1: a 0xFF byte is followed by a byte whose most significant bit is a stuffed 0.</remarks>
internal ref struct JpxPacketHeaderReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _byte;
    private int _bits;

    /// <summary>Initializes a new instance of the <see cref="JpxPacketHeaderReader"/> struct at <paramref name="position"/>.</summary>
    public JpxPacketHeaderReader(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        Position = position;
    }

    /// <summary>Gets the offset of the next byte to read.</summary>
    public int Position { get; private set; }

    /// <summary>Gets a value indicating whether a bit past the end of the data was requested.</summary>
    public bool Overrun { get; private set; }

    /// <summary>Reads one bit.</summary>
    public int ReadBit()
    {
        if (_bits == 0)
        {
            _bits = _byte == 0xFF ? 7 : 8;
            if (Position < _data.Length)
            {
                _byte = _data[Position++];
            }
            else
            {
                _byte = 0;
                Overrun = true;
            }
        }

        _bits--;
        return (_byte >> _bits) & 1;
    }

    /// <summary>Reads <paramref name="count"/> bits (at most 31) as an unsigned number, MSB first.</summary>
    public int ReadBits(int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++)
        {
            value = (value << 1) | ReadBit();
        }

        return value;
    }

    /// <summary>Ends the header: drops the bits left in the current byte and, after a 0xFF, the byte holding the stuffed bit.</summary>
    public void Align()
    {
        _bits = 0;
        if (_byte == 0xFF)
        {
            if (Position < _data.Length)
            {
                Position++;
            }
            else
            {
                Overrun = true;
            }

            _byte = 0;
        }
    }
}
