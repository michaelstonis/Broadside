using System.Runtime.CompilerServices;

namespace Broadside.Filters.Codecs;

/// <summary>
/// The MQ binary arithmetic decoder shared by the JPEG 2000 (JPXDecode) and JBIG2 (JBIG2Decode) codecs: the decoding procedures
/// INITDEC, DECODE, RENORMD and BYTEIN over one codeword segment, with the 47-state probability estimation of Table C.2.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.800 | ISO/IEC 15444-1 Annex C.3 (Figures C.15 to C.20, Table C.2), identical to ITU-T T.88 | ISO/IEC 14492 Annex E.3
/// (Table E.1): only the context models differ, and those belong to the codecs. A context is one byte, <c>(I &lt;&lt; 1) | MPS</c>,
/// so a codec keeps its contexts in any <see cref="Span{T}"/> of bytes (19 for JPEG 2000 tier-1, up to 65,536 for a JBIG2 generic
/// region) and initializes them with <see cref="Context"/>.
/// </para>
/// <para>
/// Reading past the end of the segment behaves as if the segment ended with the two bytes <c>0xFF 0xFF</c> (T.800 D.4.1, B.10.7.1:
/// a marker code), so BYTEIN feeds 1-bits from then on and never reads out of bounds; the codecs need not copy a segment to append
/// them. Allocation-free: a <see langword="ref struct"/> over the segment.
/// </para>
/// </remarks>
internal ref struct MqDecoder
{
    // Table C.2 (T.800) / Table E.1 (T.88), one entry per state: Qe << 16 | NMPS << 8 | NLPS << 1 | SWITCH.
    private static readonly uint[] States = Build(
    [
        0x5601, 1, 1, 1, 0x3401, 2, 6, 0, 0x1801, 3, 9, 0, 0x0AC1, 4, 12, 0, 0x0521, 5, 29, 0, 0x0221, 38, 33, 0,
        0x5601, 7, 6, 1, 0x5401, 8, 14, 0, 0x4801, 9, 14, 0, 0x3801, 10, 14, 0, 0x3001, 11, 17, 0, 0x2401, 12, 18, 0,
        0x1C01, 13, 20, 0, 0x1601, 29, 21, 0, 0x5601, 15, 14, 1, 0x5401, 16, 14, 0, 0x5101, 17, 15, 0, 0x4801, 18, 16, 0,
        0x3801, 19, 17, 0, 0x3401, 20, 18, 0, 0x3001, 21, 19, 0, 0x2801, 22, 19, 0, 0x2401, 23, 20, 0, 0x2201, 24, 21, 0,
        0x1C01, 25, 22, 0, 0x1801, 26, 23, 0, 0x1601, 27, 24, 0, 0x1401, 28, 25, 0, 0x1201, 29, 26, 0, 0x1101, 30, 27, 0,
        0x0AC1, 31, 28, 0, 0x09C1, 32, 29, 0, 0x08A1, 33, 30, 0, 0x0521, 34, 31, 0, 0x0441, 35, 32, 0, 0x02A1, 36, 33, 0,
        0x0221, 37, 34, 0, 0x0141, 38, 35, 0, 0x0111, 39, 36, 0, 0x0085, 40, 37, 0, 0x0049, 41, 38, 0, 0x0025, 42, 39, 0,
        0x0015, 43, 40, 0, 0x0009, 44, 41, 0, 0x0005, 45, 42, 0, 0x0001, 45, 43, 0, 0x5601, 46, 46, 0,
    ]);

    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private uint _c;
    private uint _a;
    private int _ct;

    /// <summary>Initializes a new instance of the <see cref="MqDecoder"/> struct over one codeword segment (INITDEC).</summary>
    /// <param name="data">The segment, without the two terminating <c>0xFF</c> bytes.</param>
    /// <remarks>ITU-T T.800 C.3.5, Figure C.20; ITU-T T.88 E.3.5.</remarks>
    public MqDecoder(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
        _c = (uint)ByteAt(0) << 16;
        ByteIn();
        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    /// <summary>The number of probability states.</summary>
    public const int StateCount = 47;

    /// <summary>Returns the byte of a context in state <paramref name="state"/> with the given more probable symbol.</summary>
    /// <param name="state">The index I into Table C.2, 0 to 46.</param>
    /// <param name="mps">The more probable symbol, 0 or 1.</param>
    /// <returns>The context byte.</returns>
    public static byte Context(int state, int mps = 0) => (byte)((state << 1) | mps);

    /// <summary>Decodes one binary decision in <paramref name="context"/> and updates its state (DECODE).</summary>
    /// <param name="context">The context byte, <c>(I &lt;&lt; 1) | MPS</c>.</param>
    /// <returns>The decision, 0 or 1.</returns>
    /// <remarks>ITU-T T.800 C.3.2, Figures C.15 to C.17; ITU-T T.88 E.3.2.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ref byte context)
    {
        int mps = context & 1;
        uint state = States[context >> 1];
        uint qe = state >> 16;
        int decision;
        _a -= qe;
        if ((_c >> 16) < qe)
        {
            // LPS_EXCHANGE.
            if (_a < qe)
            {
                decision = mps;
                context = (byte)((((state >> 8) & 0xFF) << 1) | (uint)mps);
            }
            else
            {
                decision = 1 - mps;
                context = (byte)((state & 0xFE) | (uint)(mps ^ (int)(state & 1)));
            }

            _a = qe;
            Renormalize();
        }
        else
        {
            _c -= qe << 16;
            if ((_a & 0x8000) != 0)
            {
                return mps;
            }

            // MPS_EXCHANGE.
            if (_a < qe)
            {
                decision = 1 - mps;
                context = (byte)((state & 0xFE) | (uint)(mps ^ (int)(state & 1)));
            }
            else
            {
                decision = mps;
                context = (byte)((((state >> 8) & 0xFF) << 1) | (uint)mps);
            }

            Renormalize();
        }

        return decision;
    }

    private static uint[] Build(ReadOnlySpan<int> rows)
    {
        uint[] states = new uint[StateCount];
        for (int i = 0; i < StateCount; i++)
        {
            states[i] = ((uint)rows[4 * i] << 16) | ((uint)rows[(4 * i) + 1] << 8) | ((uint)rows[(4 * i) + 2] << 1) | (uint)rows[(4 * i) + 3];
        }

        return states;
    }

    /// <summary>RENORMD (T.800 C.3.3, Figure C.18).</summary>
    private void Renormalize()
    {
        do
        {
            if (_ct == 0)
            {
                ByteIn();
            }

            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
        while ((_a & 0x8000) == 0);
    }

    /// <summary>BYTEIN (T.800 C.3.4, Figure C.19): a 0xFF followed by a byte above 0x8F is a marker and feeds 1-bits in place.</summary>
    private void ByteIn()
    {
        if (ByteAt(_position) == 0xFF)
        {
            if (ByteAt(_position + 1) > 0x8F)
            {
                _c += 0xFF00;
                _ct = 8;
            }
            else
            {
                _position++;
                _c += (uint)ByteAt(_position) << 9;
                _ct = 7;
            }
        }
        else
        {
            _position++;
            _c += (uint)ByteAt(_position) << 8;
            _ct = 8;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly byte ByteAt(int position) => (uint)position < (uint)_data.Length ? _data[position] : (byte)0xFF;
}
