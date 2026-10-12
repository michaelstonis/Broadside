using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>Names the composite font model looks up in Type 0 font, CIDFont and CMap dictionaries, created once (issue #53).</summary>
internal static class CompositeFontNames
{
    /// <summary><c>/DescendantFonts</c> (§9.7.6.1 Table 119).</summary>
    public static readonly CosName DescendantFonts = new("DescendantFonts");

    /// <summary><c>/ToUnicode</c> (§9.7.6.1 Table 119).</summary>
    public static readonly CosName ToUnicode = new("ToUnicode");

    /// <summary><c>/CIDSystemInfo</c> (§9.7.4.1 Table 115, §9.7.5.3 Table 118).</summary>
    public static readonly CosName CidSystemInfo = new("CIDSystemInfo");

    /// <summary><c>/Registry</c> (§9.7.3 Table 114).</summary>
    public static readonly CosName Registry = new("Registry");

    /// <summary><c>/Ordering</c> (§9.7.3 Table 114).</summary>
    public static readonly CosName Ordering = new("Ordering");

    /// <summary><c>/Supplement</c> (§9.7.3 Table 114).</summary>
    public static readonly CosName Supplement = new("Supplement");

    /// <summary><c>/DW</c> (§9.7.4.1 Table 115).</summary>
    public static readonly CosName DW = new("DW");

    /// <summary><c>/W</c> (§9.7.4.1 Table 115).</summary>
    public static readonly CosName W = new("W");

    /// <summary><c>/DW2</c> (§9.7.4.1 Table 115).</summary>
    public static readonly CosName DW2 = new("DW2");

    /// <summary><c>/W2</c> (§9.7.4.1 Table 115).</summary>
    public static readonly CosName W2 = new("W2");

    /// <summary><c>/CIDToGIDMap</c> (§9.7.4.1 Table 115).</summary>
    public static readonly CosName CidToGidMap = new("CIDToGIDMap");

    /// <summary><c>/Identity</c>, the identity <c>CIDToGIDMap</c> (§9.7.4.1 Table 115).</summary>
    public static readonly CosName Identity = new("Identity");

    /// <summary><c>/CIDFontType0</c> (§9.7.4.1 Table 115).</summary>
    public static readonly CosName CidFontType0 = new("CIDFontType0");

    /// <summary><c>/CIDFontType2</c> (§9.7.4.1 Table 115).</summary>
    public static readonly CosName CidFontType2 = new("CIDFontType2");

    /// <summary><c>/UseCMap</c> (§9.7.5.3 Table 118).</summary>
    public static readonly CosName UseCMap = new("UseCMap");

    /// <summary><c>/WMode</c> (§9.7.5.3 Table 118).</summary>
    public static readonly CosName WMode = new("WMode");
}
