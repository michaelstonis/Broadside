namespace Broadside.TestSupport;

/// <summary>
/// Known-answer JBIG2 data from ITU-T T.88 (02/2000): the generic regions of the Annex H.1 datastream cut into embedded page
/// streams, and the ISO 32000-2 §7.4.7 example. Shared by the JBIG2 tests, the fuzz seeds and the benchmarks.
/// </summary>
public static class Jbig2Samples
{
    /// <summary>
    /// T.88 Annex H.1 bytes 0x30-0x4D (segment 1, the page information of page 1, 64 x 56) and 0xB3-0xE9 (segment 4, an immediate
    /// lossless generic region coded with MMR, 54 x 44 at (4, 11)).
    /// </summary>
    public static byte[] AnnexHMmrGeneric { get; } = Convert.FromHexString(
        "000000013000010000001300000040000000380000000000000000010000000000042700010000002c000000360000002c000000040000000b000126a071cea7"
        + "ffffffffffffffffffffffffffffffffffffff" + "f8f0");

    /// <summary>
    /// T.88 Annex H.1 bytes 0x190-0x1AD (segment 8, page information of page 2) and 0x200-0x22D (segment 11, the same generic
    /// region coded arithmetically with template 0, moved AT pixels and typical prediction), with the page association rewritten
    /// to 1 as ISO 32000-2 §7.4.7 requires.
    /// </summary>
    public static byte[] AnnexHArithmeticGeneric { get; } = Convert.FromHexString(
        "0000000830000100000013000000400000003800000000000000000100000000000b27000100000023000000360000002c000000040000000b000803fffdff02fefefe04eeed87fbcb2bffac");

    /// <summary>The page both Annex H generic regions decode to: 64 x 56, SHA-256 of the PDF samples (0 = black, ISO 32000-2 §7.4.7 polarity).</summary>
    /// <remarks>jbig2dec 0.20 (<c>jbig2dec -e</c>) gives the raster MD5 f9cedebb0fb7684eb9abf49a9263dcba (1 = black) for both.</remarks>
    public const string AnnexHGenericPageSha256 = "4579c1470553d3af7ac9cc5e45c61d5c32584cf4f9f310f076ebdfda4004f432";

    /// <summary>
    /// T.88 Annex H.1 segments 0 and 16, the datastream's global segments: a Huffman symbol dictionary with an MMR and an uncompressed
    /// height class collective bitmap, and an arithmetic symbol dictionary (template 2). The <c>JBIG2Globals</c> of the three pages below.
    /// </summary>
    public static byte[] AnnexHGlobals { get; } = Convert.FromHexString(
        "000000000001000000001800010000000100000001e9cbf40026af04bff0782fe000400000001000010000000016080002ff00000001000000014fe78d681b142f3fffac");

    /// <summary>
    /// T.88 Annex H.1 page 1 (64 x 56), everything Huffman or MMR coded: a Huffman symbol dictionary, a Huffman text region (four strips,
    /// BOTTOMLEFT) using it and the global one, the MMR generic region, an MMR pattern dictionary and an MMR halftone region.
    /// </summary>
    public static byte[] AnnexHPage1 { get; } = Convert.FromHexString(
        "000000013000010000001300000040000000380000000000000000010000000000020001010000001c00010000000200000002e5cdf80079e0841081f08210861079f000800000000307420002010000003100000025000000080000000400000001000c0900100000000501100000000000000000000000000000000c4007087041d0000000042700010000002c000000360000002c000000040000000b000126a071cea7fffffffffffffffffffffffffffffffffffffff8f0000000051001010000002d0104040000000f20d184611845f2f97c8f11c39e45f2f97d42850aaa84622feeec446222352a0a83b9dcee77800000000617200501000000570000002000000024000000100000000f00010000000800000009000000000000000004000000aaaaaaaa8008008036d5556b5ad40040042ee952d2d2d28aa54a00200223e09524b4928a4a925492d24a292a4940040040");

    /// <summary>
    /// T.88 Annex H.1 page 2: page 1 again, every segment arithmetically coded (symbol dictionary, text region, generic region with TPGD,
    /// pattern dictionary, halftone region), with the page association rewritten to 1 as ISO 32000-2 §7.4.7 requires.
    /// </summary>
    public static byte[] AnnexHPage2 { get; } = Convert.FromHexString(
        "000000083000010000001300000040000000380000000000000000010000000000090001010000001b080002ff00000002000000024fe78c200e1dc7cf0111c4b26fffac0000000a07400009010000001f00000025000000080000000400000001000c08000000058d6e5a124085ffac0000000b27000100000023000000360000002c000000040000000b000803fffdff02fefefe04eeed87fbcb2bffac0000000c1001010000001c0604040000000f90716b6d99a7aa497df2e5481fdc68bc6e40bbffac0000000d17200c010000003e0000002000000024000000100000000f0002000000080000000900000000000000000400000087cb821e66a414eb3c4a15faccd6f3b16f4cedbfa7bfffac");

    /// <summary>
    /// T.88 Annex H.1 page 3 (37 x 8): a refinement/aggregate symbol dictionary built on the global arithmetic one, and a text region
    /// with a refined instance, page association rewritten to 1.
    /// </summary>
    public static byte[] AnnexHPage3 { get; } = Convert.FromHexString(
        "0000000f3000010000001300000025000000080000000000000000010000000000110021100100000020080202ffffffffff00000003000000024fe9d7d590c3b526a7fb6d14983fffac00000012072011010000002500000025000000080000000000000000008c1200000004a95c8bf4c37d966a28e5768fffac");

    /// <summary>Annex H pages 1 and 2: 64 x 56, SHA-256 of the PDF samples; jbig2dec 0.20 gives the raster MD5 68bcc9351975d139d0344c4c9f4bac20 (1 = black).</summary>
    public const string AnnexHPageSha256 = "d806f2e73907d03b1defbb7afadcee5962d80a009a844d3170d8b3f8f446a2fd";

    /// <summary>Annex H page 3: 37 x 8, SHA-256 of the PDF samples; jbig2dec 0.20 gives the raster MD5 b219c8d7e2edb95f24fd435550c3e756.</summary>
    public const string AnnexHPage3Sha256 = "e3695c570cb32d63f6eed2d33c883d11ea448e2ff334f8763051c8a055eb0f20";

    /// <summary>The ISO 32000-2 §7.4.7 example's <c>JBIG2Globals</c> stream: one arithmetic symbol dictionary (two symbols).</summary>
    public static byte[] IsoExampleGlobals { get; } = Convert.FromHexString(
        "0000000000010000000032000003fffdff02fefefe00000001000000012ae225aea9a5a538b4d9999c5c8e56ef0f8727f2b53d4e37ef795cc5506dffac");

    /// <summary>The ISO 32000-2 §7.4.7 example's image stream: page information (52 x 66) and an immediate text region.</summary>
    public static byte[] IsoExamplePage { get; } = Convert.FromHexString(
        "00000001300001000000130000003400000042000000000000000040000000000002062000010000001e000000340000004200000000000000000200100000000231db51ce51ffac");

    /// <summary>The ISO example's page: 52 x 66, SHA-256 of the PDF samples; jbig2dec 0.20 gives the raster MD5 9652c2cf760e5354e86f87fe63256935.</summary>
    public const string IsoExamplePageSha256 = "c18bc3e0494e72db117fbeb399ceba85bfe30b47b92f66e40b381615954f84a1";
}
