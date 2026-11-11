using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
    private int _markerBytes;

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

    /// <summary>
    /// Gets how many times BYTEIN met the end of the segment or a marker and fed 1-bits instead of data. A well-formed segment needs
    /// at most a few; JBIG2 uses it to stop counting loops (symbols, instances, export runs) over damaged or truncated data.
    /// </summary>
    public readonly int MarkerBytes => _markerBytes;

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

    /// <summary>Gets the segment the decoder reads, for <see cref="Decode(ref uint, ReadOnlySpan{byte}, ref uint, ref uint, ref int, ref int)"/>.</summary>
    public readonly ReadOnlySpan<byte> Data => _data;

    /// <summary>
    /// Gets the A, C and CT registers and the read position, for a caller that keeps them in locals across a loop
    /// (<see cref="Decode(ref uint, ReadOnlySpan{byte}, ref uint, ref uint, ref int, ref int)"/>).
    /// </summary>
    /// <param name="a">The interval register A.</param>
    /// <param name="c">The code register C.</param>
    /// <param name="ct">The bit counter CT.</param>
    /// <param name="position">The position of the last byte read.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void GetRegisters(out uint a, out uint c, out int ct, out int position)
    {
        a = _a;
        c = _c;
        ct = _ct;
        position = _position;
    }

    /// <summary>Stores the registers and read position a caller kept in locals back into the decoder.</summary>
    /// <param name="a">The interval register A.</param>
    /// <param name="c">The code register C.</param>
    /// <param name="ct">The bit counter CT.</param>
    /// <param name="position">The position of the last byte read.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRegisters(uint a, uint c, int ct, int position)
    {
        _a = a;
        _c = c;
        _ct = ct;
        _position = position;
    }

    /// <summary>
    /// Returns the word of a context in state <paramref name="state"/> with the given more probable symbol, for
    /// <see cref="Decode(ref uint, ReadOnlySpan{byte}, ref uint, ref uint, ref int, ref int)"/>: Qe &lt;&lt; 16, the word index after an
    /// MPS (bits 8 to 14) and after an LPS (bits 1 to 7, with the SWITCH of Table C.2 applied), and the MPS (bit 0). Holding Qe in the
    /// context itself saves the decision a dependent load.
    /// </summary>
    /// <param name="state">The index I into Table C.2, 0 to 46.</param>
    /// <param name="mps">The more probable symbol, 0 or 1.</param>
    /// <returns>The context word.</returns>
    public static uint ContextWord(int state, int mps = 0) => Words[(state << 1) | mps];

    /// <summary>
    /// DECODE with the registers and read position in the caller's locals (from <see cref="GetRegisters"/>, stored back with
    /// <see cref="SetRegisters"/>) and a context word from <see cref="ContextWord"/>: the same decisions, state transitions and bytes
    /// read as <see cref="Decode(ref byte)"/>, but nothing touches memory except the context, so a coding pass keeps the decoder in
    /// registers; RENORMD shifts by the leading zeros of A at once between byte inputs. It does not count
    /// <see cref="MarkerBytes"/>.
    /// </summary>
    /// <param name="context">The context word.</param>
    /// <param name="data">The segment (<see cref="Data"/>).</param>
    /// <param name="a">The interval register A.</param>
    /// <param name="c">The code register C.</param>
    /// <param name="ct">The bit counter CT.</param>
    /// <param name="position">The position of the last byte read.</param>
    /// <returns>The decision, 0 or 1.</returns>
    /// <remarks>ITU-T T.800 C.3.2 to C.3.4, Figures C.15 to C.19.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Decode(ref uint context, ReadOnlySpan<byte> data, ref uint a, ref uint c, ref int ct, ref int position)
    {
        // Figures C.15 to C.17 without branches, since a pass's decisions are often close to even and mispredict: A and C stay below
        // 2^16 and 2^32, so the borrow of a 32-bit subtraction gives each comparison. lps: C lies in the LPS sub-interval;
        // exchange: the conditional exchange swaps the symbols; the decision is the MPS unless exactly one of them holds. Without
        // renormalization (A still at least 0x8000, only in the MPS sub-interval) neither holds and the context keeps its state.
        uint word = context;
        uint qe = word >> 16;
        a -= qe;
        uint lps = ((c >> 16) - qe) >> 31;
        if ((lps | (((a >> 15) & 1) ^ 1)) == 0)
        {
            // The MPS sub-interval without renormalization: the one outcome that skewed contexts mostly take, kept on a branch.
            c -= qe << 16;
            return (int)(word & 1);
        }

        uint exchange = (a - qe) >> 31;
        uint lpsMask = 0u - lps;
        c -= (qe << 16) & ~lpsMask;
        a = (qe & lpsMask) | (a & ~lpsMask);
        uint flip = lps ^ exchange;
        uint renormalize = ((a >> 15) & 1) ^ 1;
        uint next = Unsafe.Add(ref MemoryMarshal.GetReference(Words), (int)((word >> (int)(8 - (7 * flip))) & 0x7F));
        context = word ^ ((word ^ next) & (0u - renormalize));

        // RENORMD: A has n leading zeros in its 16 bits, so it and C shift n times; BYTEIN comes in whenever CT runs out.
        int n = BitOperations.LeadingZeroCount(a) - 16;
        a <<= n;
        while (n > ct)
        {
            c <<= ct;
            n -= ct;
            ByteIn(data, ref c, ref ct, ref position);
        }

        c <<= n;
        ct -= n;
        return (int)((word ^ flip) & 1);
    }

    /// <summary>BYTEIN (T.800 C.3.4, Figure C.19) on registers held by the caller.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ByteIn(ReadOnlySpan<byte> data, ref uint c, ref int ct, ref int position)
    {
        uint current = (uint)position < (uint)data.Length ? data[position] : 0xFFu;
        int next = position + 1;
        uint following = (uint)next < (uint)data.Length ? data[next] : 0xFFu;
        if (current == 0xFF)
        {
            if (following > 0x8F)
            {
                c += 0xFF00;
                ct = 8;
            }
            else
            {
                position = next;
                c += following << 9;
                ct = 7;
            }
        }
        else
        {
            position = next;
            c += following << 8;
            ct = 8;
        }
    }

    // The context words of ContextWord by (I << 1) | MPS, Table C.2 (T.800) / Table E.1 (T.88) with both transitions resolved;
    // padded to 128 entries so that any 7-bit index stays inside the table.
    private static ReadOnlySpan<uint> Words =>
    [
        0x56010206, 0x56010305, 0x34010418, 0x3401051B, 0x18010624, 0x18010727, 0x0AC10830, 0x0AC10933,
        0x05210A74, 0x05210B77, 0x02214C84, 0x02214D87, 0x56010E1A, 0x56010F19, 0x54011038, 0x5401113B,
        0x48011238, 0x4801133B, 0x38011438, 0x3801153B, 0x30011644, 0x30011747, 0x24011848, 0x2401194B,
        0x1C011A50, 0x1C011B53, 0x16013A54, 0x16013B57, 0x56011E3A, 0x56011F39, 0x54012038, 0x5401213B,
        0x5101223C, 0x5101233F, 0x48012440, 0x48012543, 0x38012644, 0x38012747, 0x34012848, 0x3401294B,
        0x30012A4C, 0x30012B4F, 0x28012C4C, 0x28012D4F, 0x24012E50, 0x24012F53, 0x22013054, 0x22013157,
        0x1C013258, 0x1C01335B, 0x1801345C, 0x1801355F, 0x16013660, 0x16013763, 0x14013864, 0x14013967,
        0x12013A68, 0x12013B6B, 0x11013C6C, 0x11013D6F, 0x0AC13E70, 0x0AC13F73, 0x09C14074, 0x09C14177,
        0x08A14278, 0x08A1437B, 0x0521447C, 0x0521457F, 0x04414680, 0x04414783, 0x02A14884, 0x02A14987,
        0x02214A88, 0x02214B8B, 0x01414C8C, 0x01414D8F, 0x01114E90, 0x01114F93, 0x00855094, 0x00855197,
        0x00495298, 0x0049539B, 0x0025549C, 0x0025559F, 0x001556A0, 0x001557A3, 0x000958A4, 0x000959A7,
        0x00055AA8, 0x00055BAB, 0x00015AAC, 0x00015BAF, 0x56015CB8, 0x56015DBB, 0x00000000, 0x00000000,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

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
                _markerBytes += _markerBytes < int.MaxValue ? 1 : 0;
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
