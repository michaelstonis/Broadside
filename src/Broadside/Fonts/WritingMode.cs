namespace Broadside.Fonts;

/// <summary>The writing mode a CMap selects for the glyphs of a composite font: which metrics position them.</summary>
/// <remarks>ISO 32000-2 §9.2.4 and §9.7.5.1; the CMap's <c>WMode</c> (§9.7.5.3, Table 118; Adobe TN 5014 §5.2).</remarks>
public enum WritingMode
{
    /// <summary>Horizontal writing (<c>WMode</c> 0): glyphs advance by their widths, w0 (§9.7.4.3 <c>W</c>, <c>DW</c>).</summary>
    Horizontal = 0,

    /// <summary>Vertical writing (<c>WMode</c> 1): glyphs advance by w1 and are placed by the position vector v (§9.7.4.3 <c>W2</c>, <c>DW2</c>).</summary>
    Vertical = 1,
}
