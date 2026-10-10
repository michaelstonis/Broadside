using System.Runtime.CompilerServices;

namespace Broadside.Filters.Dct;

/// <summary>
/// The QM arithmetic decoder of JPEG: decodes binary decisions from the entropy-coded segment of an arithmetic-coded scan, each
/// with the adaptive probability estimate kept in its statistics bin.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.81 Annex D.2 (Figure D.16 Decode(S), Figures D.17 and D.18 conditional exchanges, Figure D.19 Renorm_d, Figures D.20
/// and D.21 Byte_in with the removal of stuffed zero bytes, Figure D.22 Initdec) and Table D.3 (the 113-state probability
/// estimation state machine, entry 0 = Qe X'5A1D'). This is the QM coder, which JBIG2 and JPEG 2000 do not share: their MQ coder
/// (T.88 Table E.1, 47 states, bit stuffing) lives in <c>Broadside.Filters.Codecs.MqDecoder</c>.
/// </para>
/// <para>
/// Layout follows libjpeg-turbo <c>src/jdarith.c</c> <c>arith_decode</c> and <c>src/jaricom.c</c> (commit 43ea8097; the
/// Independent JPEG Group's software, see THIRD-PARTY-NOTICES.txt): one byte per statistics bin (bit 7 = the MPS sense, bits 0-6 =
/// the state index), the C register holding the 16-bit code base and the next input bits with a floating cut point at CT, and a
/// 114th state that never adapts for the decisions T.81 codes with a fixed probability of one half (§F.1.4.4.2 sign, §G.1.3.3
/// refinement bits; ITU-T T.851 §10.3 Table 5).
/// </para>
/// <para>
/// A marker inside the data ends it: zeros are supplied from there, which is legal in arithmetic coding (the decoder may need
/// more bits than the encoder wrote, §D.2.6). The end of the data does the same and is recorded as <see cref="Exhausted"/>.
/// </para>
/// </remarks>
internal ref struct ArithmeticDecoder
{
    /// <summary>The statistics bin state that codes with a fixed probability of one half and never adapts.</summary>
    public const byte FixedState = 113;

    /// <summary>
    /// Table D.3 packed per state: Qe &lt;&lt; 16 | Next_Index_MPS &lt;&lt; 8 | Switch_MPS &lt;&lt; 7 | Next_Index_LPS; entry 113 is
    /// the fixed estimate (Qe X'5A1D', both next indices 113, no switch).
    /// </summary>
    private static readonly int[] States =
    [
        0x5a1d0181, 0x2586020e, 0x11140310, 0x080b0412, 0x03d80514, 0x01da0617, 0x00e50719, 0x006f081c,
        0x0036091e, 0x001a0a21, 0x000d0b23, 0x00060c09, 0x00030d0a, 0x00010d0c, 0x5a7f0f8f, 0x3f251024,
        0x2cf21126, 0x207c1227, 0x17b91328, 0x1182142a, 0x0cef152b, 0x09a1162d, 0x072f172e, 0x055c1830,
        0x04061931, 0x03031a33, 0x02401b34, 0x01b11c36, 0x01441d38, 0x00f51e39, 0x00b71f3b, 0x008a203c,
        0x0068213e, 0x004e223f, 0x003b2320, 0x002c0921, 0x5ae125a5, 0x484c2640, 0x3a0d2741, 0x2ef12843,
        0x261f2944, 0x1f332a45, 0x19a82b46, 0x15182c48, 0x11772d49, 0x0e742e4a, 0x0bfb2f4b, 0x09f8304d,
        0x0861314e, 0x0706324f, 0x05cd3330, 0x04de3432, 0x040f3532, 0x03633633, 0x02d43734, 0x025c3835,
        0x01f83936, 0x01a43a37, 0x01603b38, 0x01253c39, 0x00f63d3a, 0x00cb3e3b, 0x00ab3f3d, 0x008f203d,
        0x5b1241c1, 0x4d044250, 0x412c4351, 0x37d84452, 0x2fe84553, 0x293c4654, 0x23794756, 0x1edf4857,
        0x1aa94957, 0x174e4a48, 0x14244b48, 0x119c4c4a, 0x0f6b4d4a, 0x0d514e4b, 0x0bb64f4d, 0x0a40304d,
        0x583251d0, 0x4d1c5258, 0x438e5359, 0x3bdd545a, 0x34ee555b, 0x2eae565c, 0x299a575d, 0x25164756,
        0x557059d8, 0x4ca95a5f, 0x44d95b60, 0x3e225c61, 0x38245d63, 0x32b45e63, 0x2e17565d, 0x56a860df,
        0x4f466165, 0x47e56266, 0x41cf6367, 0x3c3d6468, 0x375e5d63, 0x52316669, 0x4c0f676a, 0x4639686b,
        0x415e6367, 0x56276ae9, 0x50e76b6c, 0x4b85676d, 0x55976d6e, 0x504f6b6f, 0x5a106fee, 0x55226d70,
        0x59eb6ff0, 0x5a1d7171,
    ];

    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private long _c;
    private int _a;
    private int _ct;
    private bool _atMarker;

    /// <summary>Initializes a new instance of the <see cref="ArithmeticDecoder"/> struct at the start of an entropy-coded segment (Initdec).</summary>
    /// <param name="data">The whole JPEG data.</param>
    /// <param name="position">Where the segment starts.</param>
    public ArithmeticDecoder(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        _position = position;
        _c = 0;
        _a = 0;
        _ct = -16; // read two bytes into C before the first decision
        _atMarker = false;
        Exhausted = false;
        Failed = false;
    }

    /// <summary>Gets the position of the first byte not yet read: the marker that ended the segment, once it was reached.</summary>
    public readonly int Position => _position;

    /// <summary>Gets a value indicating whether the data ended without a marker while decisions still needed bits.</summary>
    public bool Exhausted { get; private set; }

    /// <summary>Gets or sets a value indicating whether the decoded values became impossible; nothing more is decoded until the next restart.</summary>
    public bool Failed { get; set; }

    /// <summary>Decodes one binary decision with the statistics bin <paramref name="bin"/> and updates its estimate.</summary>
    /// <param name="bin">The statistics bin: MPS in bit 7, the Table D.3 index in bits 0 to 6.</param>
    /// <returns>The decision, 0 or 1.</returns>
    /// <remarks>ITU-T T.81 §D.2.4 (Figures D.16 to D.18), §D.2.5 (Figures D.5, D.6 estimation), §D.2.6 (Figure D.19 renormalization).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ref byte bin)
    {
        while (_a < 0x8000)
        {
            if (--_ct < 0)
            {
                _c = (_c << 8) + NextByte();
                if ((_ct += 8) < 0 && ++_ct == 0)
                {
                    // Two initial bytes are in: A starts at X'10000' after the shift below.
                    _a = 0x8000;
                }
            }

            _a <<= 1;
        }

        int state = bin;
        int packed = States[state & 0x7F];
        int qe = packed >>> 16;
        int nextLps = packed & 0xFF;
        int nextMps = (packed >> 8) & 0xFF;
        int interval = _a - qe;
        _a = interval;
        long cut = (long)interval << _ct;
        if (_c >= cut)
        {
            // The LPS sub-interval, with the conditional exchange of Figure D.17.
            _c -= cut;
            if (_a < qe)
            {
                _a = qe;
                bin = (byte)((state & 0x80) ^ nextMps);
            }
            else
            {
                _a = qe;
                bin = (byte)((state & 0x80) ^ nextLps);
                state ^= 0x80;
            }
        }
        else if (_a < 0x8000)
        {
            // The MPS sub-interval needs renormalization, with the conditional exchange of Figure D.18.
            if (_a < qe)
            {
                bin = (byte)((state & 0x80) ^ nextLps);
                state ^= 0x80;
            }
            else
            {
                bin = (byte)((state & 0x80) ^ nextMps);
            }
        }

        return state >> 7;
    }

    /// <summary>Byte_in (Figures D.20 and D.21): the next data byte with stuffed zeros removed; 0 at a marker or the end.</summary>
    private int NextByte()
    {
        if (_atMarker)
        {
            return 0;
        }

        ReadOnlySpan<byte> data = _data;
        if (_position >= data.Length)
        {
            _atMarker = true;
            Exhausted = true;
            return 0;
        }

        byte value = data[_position];
        if (value != 0xFF)
        {
            _position++;
            return value;
        }

        // FF: a stuffed zero follows (data FF), or a marker begins (fill FFs may precede its code).
        int next = _position + 1;
        while (next < data.Length && data[next] == 0xFF)
        {
            next++;
        }

        if (next < data.Length && data[next] == 0)
        {
            _position = next + 1;
            return 0xFF;
        }

        _atMarker = true;
        Exhausted = next >= data.Length;
        return 0;
    }
}
