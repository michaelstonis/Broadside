namespace Broadside.Filters.Jpx;

/// <summary>One code-block of a precinct sub-band and what the packets have told about it so far.</summary>
/// <remarks>ITU-T T.800 B.7 (equations B-17, B-18) and B.10 (packet header state).</remarks>
internal struct JpxCodeBlock
{
    /// <summary>The code-block's area, relative to the sub-band's origin.</summary>
    public int X0;
    public int Y0;
    public int X1;
    public int Y1;

    /// <summary>Whether a packet has included the code-block yet.</summary>
    public bool Included;

    /// <summary>The number of missing most significant bit-planes P.</summary>
    public int ZeroPlanes;

    /// <summary>Lblock, the number of length bits before the pass-count logarithm (starts at 3).</summary>
    public int LengthBits;

    /// <summary>The coding passes received so far.</summary>
    public int Passes;

    /// <summary>The first and last chunk of compressed data in the tile's chunk list, or -1.</summary>
    public int FirstChunk;
    public int LastChunk;

    /// <summary>The bytes received so far.</summary>
    public int DataLength;

    /// <summary>The passes in the codeword segment being filled, and the most it may hold (B.10.7.2, Table D.9).</summary>
    public int SegmentPasses;
    public int SegmentMax;

    /// <summary>The index of the codeword segment being filled, -1 before the first.</summary>
    public int SegmentIndex;
}

/// <summary>One chunk of a code-block's compressed data: the bytes one packet contributed.</summary>
/// <param name="Offset">The offset in the tile's packet data.</param>
/// <param name="Length">The number of bytes.</param>
/// <param name="Passes">The coding passes the bytes carry.</param>
/// <param name="Next">The next chunk of the same code-block, or -1.</param>
/// <param name="StartsSegment">Whether the chunk starts a new codeword segment (B.10.7.2).</param>
internal record struct JpxChunk(int Offset, int Length, int Passes, int Next, bool StartsSegment);

/// <summary>One codeword segment of a code-block, ready for tier-1: its bytes in the joined data and its coding passes.</summary>
internal readonly record struct JpxSegment(int Start, int Length, int Passes);

/// <summary>The code-blocks of one precinct in one sub-band, with its two tag trees.</summary>
/// <remarks>ITU-T T.800 B.6 and B.10.2.</remarks>
internal sealed class JpxPrecinctBand(int blocksWide, int blocksHigh, JpxCodeBlock[] blocks)
{
    public int BlocksWide { get; } = blocksWide;

    public int BlocksHigh { get; } = blocksHigh;

    public JpxCodeBlock[] Blocks { get; } = blocks;

    public JpxTagTree? Inclusion { get; } = blocks.Length == 0 ? null : new JpxTagTree(blocksWide, blocksHigh);

    public JpxTagTree? ZeroPlanes { get; } = blocks.Length == 0 ? null : new JpxTagTree(blocksWide, blocksHigh);
}

/// <summary>A sub-band of one resolution level of a tile-component.</summary>
/// <remarks>ITU-T T.800 B.5 (equation B-15, Table B.1) and E.1 (Mb, equation E-2).</remarks>
internal sealed class JpxBand
{
    /// <summary>The orientation: 0 LL, 1 HL, 2 LH, 3 HH.</summary>
    public int Orientation { get; init; }

    /// <summary>The sub-band's area in its own coordinates (tbx0, tby0 to tbx1, tby1).</summary>
    public long X0 { get; init; }

    public long Y0 { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Where the sub-band's samples sit in the tile-component's coefficient buffer.</summary>
    public int BufferX { get; init; }

    public int BufferY { get; init; }

    /// <summary>The number of magnitude bit-planes Mb (E-2).</summary>
    public int MagnitudeBits { get; init; }

    /// <summary>The irreversible reconstruction factor: half the quantization step size of E-3 (the tier-1 values are doubled), or 0 for the 5/3 path.</summary>
    public float Scale { get; init; }

    /// <summary>The code-block exponents in this sub-band, after the precinct limits (B-17, B-18).</summary>
    public int BlockWidthExponent { get; init; }

    public int BlockHeightExponent { get; init; }
}

/// <summary>One resolution level of a tile-component: its area, sub-bands and precincts.</summary>
/// <remarks>ITU-T T.800 B.5 (equation B-14) and B.6 (equation B-16).</remarks>
internal sealed class JpxResolution
{
    public long X0 { get; init; }

    public long Y0 { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public int PrecinctsWide { get; init; }

    public int PrecinctsHigh { get; init; }

    /// <summary>The precinct exponents PPx and PPy of this resolution level (A.6.1, Table A.21).</summary>
    public int PrecinctExponentX { get; init; }

    public int PrecinctExponentY { get; init; }

    /// <summary>The next layer each precinct expects: packets already read are skipped when progression volumes overlap (B.12.2).</summary>
    public required int[] NextLayer { get; init; }

    /// <summary>LL for level 0; HL, LH, HH otherwise.</summary>
    public required JpxBand[] Bands { get; init; }

    /// <summary>Precinct k, sub-band b at <c>k * Bands.Length + b</c>.</summary>
    public required JpxPrecinctBand[] PrecinctBands { get; init; }
}

/// <summary>One component of a tile: its area, coding parameters and resolution levels.</summary>
/// <remarks>ITU-T T.800 B.3 (equation B-12).</remarks>
internal sealed class JpxTileComponent
{
    public long X0 { get; init; }

    public long Y0 { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public required JpxComponentStyle Style { get; init; }

    /// <summary>The component's separations XRsiz and YRsiz.</summary>
    public int Dx { get; init; } = 1;

    public int Dy { get; init; } = 1;

    /// <summary>The region-of-interest shift s (A.6.3, H.1), 0 without one.</summary>
    public int RoiShift { get; init; }

    public required JpxResolution[] Resolutions { get; init; }
}
