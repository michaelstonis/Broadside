using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>The names the filter pipeline looks up: stream dictionary entries, filter names and their parameters, created once.</summary>
internal static class FilterNames
{
    /// <summary><c>/Filter</c>, the stream dictionary entry naming the filters to decode with (ISO 32000-2 §7.3.8.2, Table 5).</summary>
    public static readonly CosName Filter = new("Filter");

    /// <summary><c>/DecodeParms</c>, the parameters of the filters (§7.3.8.2, Table 5).</summary>
    public static readonly CosName DecodeParms = new("DecodeParms");

    /// <summary><c>/F</c>, the file specification of an external stream's data (§7.3.8.2, Table 5).</summary>
    public static readonly CosName F = new("F");

    /// <summary><c>/DL</c>, the decoded length hint (§7.3.8.2, Table 5).</summary>
    public static readonly CosName DL = new("DL");

    /// <summary><c>/ASCIIHexDecode</c> (§7.4.2).</summary>
    public static readonly CosName AsciiHexDecode = new("ASCIIHexDecode");

    /// <summary><c>/ASCII85Decode</c> (§7.4.3).</summary>
    public static readonly CosName Ascii85Decode = new("ASCII85Decode");

    /// <summary><c>/LZWDecode</c> (§7.4.4).</summary>
    public static readonly CosName LzwDecode = new("LZWDecode");

    /// <summary><c>/FlateDecode</c> (§7.4.4).</summary>
    public static readonly CosName FlateDecode = new("FlateDecode");

    /// <summary><c>/RunLengthDecode</c> (§7.4.5).</summary>
    public static readonly CosName RunLengthDecode = new("RunLengthDecode");

    /// <summary><c>/CCITTFaxDecode</c> (§7.4.6).</summary>
    public static readonly CosName CcittFaxDecode = new("CCITTFaxDecode");

    /// <summary><c>/JBIG2Decode</c> (§7.4.7).</summary>
    public static readonly CosName Jbig2Decode = new("JBIG2Decode");

    /// <summary><c>/DCTDecode</c> (§7.4.8).</summary>
    public static readonly CosName DctDecode = new("DCTDecode");

    /// <summary><c>/JPXDecode</c> (§7.4.9).</summary>
    public static readonly CosName JpxDecode = new("JPXDecode");

    /// <summary><c>/Crypt</c> (§7.4.10).</summary>
    public static readonly CosName Crypt = new("Crypt");

    /// <summary><c>/Name</c>, the Crypt filter parameter naming the crypt filter (§7.4.10, Table 14).</summary>
    public static readonly CosName Name = new("Name");

    /// <summary><c>/Identity</c>, the crypt filter that leaves data unchanged (§7.4.10, §7.6.6 Table 26).</summary>
    public static readonly CosName Identity = new("Identity");

    /// <summary><c>/Predictor</c> (§7.4.4.3, Table 8).</summary>
    public static readonly CosName Predictor = new("Predictor");

    /// <summary><c>/Colors</c> (§7.4.4.3, Table 8).</summary>
    public static readonly CosName Colors = new("Colors");

    /// <summary><c>/BitsPerComponent</c> (§7.4.4.3, Table 8).</summary>
    public static readonly CosName BitsPerComponent = new("BitsPerComponent");

    /// <summary><c>/Columns</c> (§7.4.4.3, Table 8).</summary>
    public static readonly CosName Columns = new("Columns");

    /// <summary><c>/EarlyChange</c> (§7.4.4.3, Table 8).</summary>
    public static readonly CosName EarlyChange = new("EarlyChange");

    /// <summary>
    /// The filter name abbreviations of §8.9.7, Table 92, which are valid only in inline images, and the full names they stand for.
    /// </summary>
    private static readonly Dictionary<CosName, CosName> Abbreviations = new()
    {
        [new CosName("AHx")] = AsciiHexDecode,
        [new CosName("A85")] = Ascii85Decode,
        [new CosName("LZW")] = LzwDecode,
        [new CosName("Fl")] = FlateDecode,
        [new CosName("RL")] = RunLengthDecode,
        [new CosName("CCF")] = CcittFaxDecode,
        [new CosName("DCT")] = DctDecode,
    };

    /// <summary>The standard filters of §7.4.1, Table 6.</summary>
    private static readonly HashSet<CosName> Standard =
        [AsciiHexDecode, Ascii85Decode, LzwDecode, FlateDecode, RunLengthDecode, CcittFaxDecode, Jbig2Decode, DctDecode, JpxDecode, Crypt];

    /// <summary>Returns the full filter name an inline-image abbreviation stands for (§8.9.7, Table 92).</summary>
    /// <param name="name">A filter name.</param>
    /// <param name="fullName">The full name, when <paramref name="name"/> is an abbreviation.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is an abbreviation.</returns>
    public static bool TryExpandAbbreviation(CosName name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CosName? fullName) =>
        Abbreviations.TryGetValue(name, out fullName);

    /// <summary>Returns whether <paramref name="name"/> is one of the standard filters of Table 6.</summary>
    /// <param name="name">A full filter name.</param>
    /// <returns><see langword="true"/> for a standard filter.</returns>
    public static bool IsStandard(CosName name) => Standard.Contains(name);

    /// <summary>Returns whether <paramref name="name"/> is one of the image codecs of §7.4.6 to §7.4.9.</summary>
    /// <param name="name">A full filter name.</param>
    /// <returns><see langword="true"/> for CCITTFaxDecode, JBIG2Decode, DCTDecode and JPXDecode.</returns>
    public static bool IsImageCodec(CosName name) =>
        name.Equals(CcittFaxDecode) || name.Equals(Jbig2Decode) || name.Equals(DctDecode) || name.Equals(JpxDecode);

    /// <summary>Returns whether the predictor functions of §7.4.4.4 apply after the filter named <paramref name="name"/>.</summary>
    /// <param name="name">A full filter name.</param>
    /// <returns><see langword="true"/> for LZWDecode and FlateDecode.</returns>
    public static bool TakesPredictor(CosName name) => name.Equals(LzwDecode) || name.Equals(FlateDecode);
}
