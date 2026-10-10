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
}
