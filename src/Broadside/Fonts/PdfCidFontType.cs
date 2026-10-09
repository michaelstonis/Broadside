namespace Broadside.Fonts;

/// <summary>The type of a CIDFont, from the <c>Subtype</c> of its dictionary.</summary>
/// <remarks>ISO 32000-2 §9.7.4.1, Table 115.</remarks>
public enum PdfCidFontType
{
    /// <summary><c>CIDFontType0</c>: glyph descriptions in Compact Font Format (CFF), selected by CID.</summary>
    CidFontType0 = 0,

    /// <summary><c>CIDFontType2</c>: glyph descriptions in TrueType format, selected by glyph index through <c>CIDToGIDMap</c>.</summary>
    CidFontType2 = 1,
}
