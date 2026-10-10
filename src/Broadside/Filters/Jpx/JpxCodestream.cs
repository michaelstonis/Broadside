namespace Broadside.Filters.Jpx;

/// <summary>One component of the image as the SIZ marker segment declares it.</summary>
/// <param name="Depth">The precision, 1 to 38 bits ((Ssiz &amp; 0x7F) + 1).</param>
/// <param name="Signed">Whether the samples are signed (bit 7 of Ssiz).</param>
/// <param name="Dx">The horizontal separation XRsiz, 1 to 255.</param>
/// <param name="Dy">The vertical separation YRsiz, 1 to 255.</param>
/// <remarks>ITU-T T.800 A.5.1, Tables A.9 and A.11.</remarks>
internal readonly record struct JpxComponentInfo(int Depth, bool Signed, int Dx, int Dy);

/// <summary>The image and tile geometry of the SIZ marker segment, with the tile counts of equation B-5.</summary>
/// <remarks>ITU-T T.800 A.5.1 (Table A.9), B.2 and B.3.</remarks>
internal sealed class JpxImageSize
{
    public long Width { get; init; }

    public long Height { get; init; }

    public long OriginX { get; init; }

    public long OriginY { get; init; }

    public long TileWidth { get; init; }

    public long TileHeight { get; init; }

    public long TileOriginX { get; init; }

    public long TileOriginY { get; init; }

    public required JpxComponentInfo[] Components { get; init; }

    /// <summary>Gets the number of tiles across (B-5).</summary>
    public int TilesWide => (int)JpxMath.CeilDivide(Width - TileOriginX, TileWidth);

    /// <summary>Gets the number of tiles down (B-5).</summary>
    public int TilesHigh => (int)JpxMath.CeilDivide(Height - TileOriginY, TileHeight);

    /// <summary>Gets the tile's area on the reference grid (B-7 to B-10).</summary>
    public (long X0, long Y0, long X1, long Y1) TileBounds(int tile)
    {
        int p = tile % TilesWide;
        int q = tile / TilesWide;
        return (
            Math.Max(TileOriginX + (p * TileWidth), OriginX),
            Math.Max(TileOriginY + (q * TileHeight), OriginY),
            Math.Min(TileOriginX + ((p + 1) * TileWidth), Width),
            Math.Min(TileOriginY + ((q + 1) * TileHeight), Height));
    }
}

/// <summary>The coding style of one component: the SPcod or SPcoc parameters of a COD or COC marker segment.</summary>
/// <remarks>ITU-T T.800 A.6.1 and A.6.2, Tables A.13 and A.15 to A.21.</remarks>
internal sealed class JpxComponentStyle
{
    /// <summary>The code-block style bit "selective arithmetic coding bypass".</summary>
    public const int Bypass = 0x01;

    /// <summary>The code-block style bit "reset context probabilities on coding pass boundaries".</summary>
    public const int ResetContexts = 0x02;

    /// <summary>The code-block style bit "termination on each coding pass".</summary>
    public const int TerminateEachPass = 0x04;

    /// <summary>The code-block style bit "vertically causal context".</summary>
    public const int VerticallyCausal = 0x08;

    /// <summary>The code-block style bit "predictable termination".</summary>
    public const int PredictableTermination = 0x10;

    /// <summary>The code-block style bit "segmentation symbols are used".</summary>
    public const int SegmentationSymbols = 0x20;

    /// <summary>Gets the number of decomposition levels NL, 0 to 32.</summary>
    public int Levels { get; init; }

    /// <summary>Gets the code-block width exponent xcb, 2 to 10.</summary>
    public int BlockWidthExponent { get; init; }

    /// <summary>Gets the code-block height exponent ycb, 2 to 10.</summary>
    public int BlockHeightExponent { get; init; }

    /// <summary>Gets the code-block style bits (Table A.19).</summary>
    public int BlockStyle { get; init; }

    /// <summary>Gets a value indicating whether the 5/3 reversible filter is used (Table A.20); otherwise the 9/7 irreversible one.</summary>
    public bool Reversible { get; init; }

    /// <summary>Gets the precinct size bytes, one per resolution level (PPx in the low nibble, PPy in the high one; Table A.21).</summary>
    public required byte[] Precincts { get; init; }

    /// <summary>Returns the precinct exponents (PPx, PPy) of resolution level <paramref name="resolution"/>.</summary>
    public (int X, int Y) PrecinctExponents(int resolution)
    {
        byte value = resolution < Precincts.Length ? Precincts[resolution] : (byte)0xFF;
        return (value & 0x0F, value >> 4);
    }
}

/// <summary>The parameters of a COD marker segment that apply to every component: Scod and SGcod.</summary>
/// <remarks>ITU-T T.800 A.6.1, Tables A.12 to A.17.</remarks>
internal sealed class JpxCodingStyle
{
    /// <summary>Gets a value indicating whether SOP marker segments may be used (Scod bit 1).</summary>
    public bool MayUseSop { get; init; }

    /// <summary>Gets a value indicating whether EPH markers are used (Scod bit 2).</summary>
    public bool UsesEph { get; init; }

    /// <summary>Gets the progression order (Table A.16): 0 LRCP, 1 RLCP, 2 RPCL, 3 PCRL, 4 CPRL.</summary>
    public int Progression { get; init; }

    /// <summary>Gets the number of layers, 1 to 65535.</summary>
    public int Layers { get; init; }

    /// <summary>Gets the multiple component transformation (Table A.17): 1 applies the RCT or ICT to components 0 to 2.</summary>
    public int Transform { get; init; }

    /// <summary>Gets the coding style of every component without a COC.</summary>
    public required JpxComponentStyle Component { get; init; }
}

/// <summary>The quantization of one component: a QCD or QCC marker segment.</summary>
/// <remarks>ITU-T T.800 A.6.4 and A.6.5, Tables A.27 to A.30; E.1.</remarks>
internal sealed class JpxQuantization
{
    /// <summary>Gets the quantization style (Table A.28): 0 none, 1 scalar derived, 2 scalar expounded.</summary>
    public int Style { get; init; }

    /// <summary>Gets the number of guard bits G, 0 to 7.</summary>
    public int GuardBits { get; init; }

    /// <summary>Gets the exponents, one per sub-band in the order LL, then HL, LH, HH from the lowest resolution up (one for style 1).</summary>
    public required int[] Exponents { get; init; }

    /// <summary>Gets the mantissas, parallel to <see cref="Exponents"/> (zero for style 0).</summary>
    public required int[] Mantissas { get; init; }

    /// <summary>Returns the exponent of sub-band index <paramref name="band"/> (0 = LL, else 1 + 3 (r - 1) + orientation - 1).</summary>
    /// <param name="band">The sub-band index.</param>
    /// <param name="levels">NL of the component.</param>
    /// <param name="decompositionLevel">The sub-band's decomposition level nb.</param>
    /// <returns>The exponent; for the derived style, equation E-5.</returns>
    public int Exponent(int band, int levels, int decompositionLevel)
    {
        if (Exponents.Length == 0)
        {
            return 0;
        }

        if (Style == 1)
        {
            return Exponents[0] - levels + decompositionLevel;
        }

        return Exponents[Math.Min(band, Exponents.Length - 1)];
    }
}

/// <summary>The COD, COC, QCD and QCC marker segments of one header (main or tile-part).</summary>
/// <remarks>ITU-T T.800 A.6: tile-part COC &gt; tile-part COD &gt; main COC &gt; main COD; QCC and QCD alike.</remarks>
internal sealed class JpxMarkerSet(int components)
{
    public JpxCodingStyle? Cod { get; set; }

    public JpxComponentStyle?[] Coc { get; } = new JpxComponentStyle?[components];

    public JpxQuantization? Qcd { get; set; }

    public JpxQuantization?[] Qcc { get; } = new JpxQuantization?[components];
}

/// <summary>One tile: its tile-parts' packet data and its tile-part header markers.</summary>
/// <remarks>ITU-T T.800 A.4.2 and B.3: the tile-parts of a tile hold its packets in order.</remarks>
internal sealed class JpxTile(int index, int components)
{
    public int Index { get; } = index;

    /// <summary>Gets the packet data of each tile-part, as (offset, length) into the codestream, in order.</summary>
    public List<(int Offset, int Length)> Parts { get; } = [];

    /// <summary>Gets the markers of the tile's first tile-part header.</summary>
    public JpxMarkerSet Markers { get; } = new(components);
}

/// <summary>The coding parameters in force for one tile, after the precedence of A.6.</summary>
internal sealed class JpxTileParameters
{
    public required JpxCodingStyle Coding { get; init; }

    public required JpxComponentStyle[] Components { get; init; }

    public required JpxQuantization[] Quantization { get; init; }

    /// <summary>Resolves the parameters of <paramref name="tile"/> from the main header and its own.</summary>
    public static JpxTileParameters Resolve(JpxMarkerSet main, JpxMarkerSet tile)
    {
        JpxCodingStyle coding = tile.Cod ?? main.Cod!;
        int count = main.Coc.Length;
        var components = new JpxComponentStyle[count];
        var quantization = new JpxQuantization[count];
        for (int c = 0; c < count; c++)
        {
            components[c] = tile.Coc[c] ?? tile.Cod?.Component ?? main.Coc[c] ?? main.Cod!.Component;
            quantization[c] = tile.Qcc[c] ?? tile.Qcd ?? main.Qcc[c] ?? main.Qcd!;
        }

        return new JpxTileParameters { Coding = coding, Components = components, Quantization = quantization };
    }
}

/// <summary>A parsed codestream: geometry, main header markers and the tiles that have data.</summary>
internal sealed class JpxCodestream
{
    public required JpxImageSize Size { get; init; }

    public required JpxMarkerSet Main { get; init; }

    /// <summary>Gets the tiles that have at least one tile-part, in order of their index.</summary>
    public required IReadOnlyList<JpxTile> Tiles { get; init; }
}

/// <summary>Integer helpers for the coordinate equations of Annex B.</summary>
internal static class JpxMath
{
    /// <summary>ceil(a / b) for b &gt; 0 and any sign of a.</summary>
    public static long CeilDivide(long a, long b) => a >= 0 ? (a + b - 1) / b : -(-a / b);

    /// <summary>ceil(a / 2^s) for any sign of a (an arithmetic shift floors).</summary>
    public static long CeilShift(long a, int s) => -(-a >> s);

    /// <summary>floor(a / 2^s) for any sign of a.</summary>
    public static long FloorShift(long a, int s) => a >> s;

    /// <summary>floor(log2(n)) for n &gt;= 1.</summary>
    public static int FloorLog2(int n) => 31 - int.LeadingZeroCount(n);
}
