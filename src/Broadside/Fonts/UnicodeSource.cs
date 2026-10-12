namespace Broadside.Fonts;

/// <summary>Which method mapped a character code to Unicode text.</summary>
/// <remarks>
/// ISO 32000-2 §9.10.2 lists the methods in priority order: <see cref="ToUnicode"/>, <see cref="GlyphName"/> (simple fonts),
/// <see cref="CidCollection"/> (composite fonts of a known character collection). When they all fail "there is no way to determine
/// what the character code represents"; Broadside then tries the font program's own Unicode "cmap" table
/// (<see cref="FontProgram"/>, beyond the specification, as PDFBox does) before giving up (<see cref="Unmapped"/>).
/// </remarks>
public enum UnicodeSource
{
    /// <summary>No method gave a value; the text is U+FFFD REPLACEMENT CHARACTER.</summary>
    Unmapped = 0,

    /// <summary>The font's ToUnicode CMap (§9.10.3).</summary>
    ToUnicode,

    /// <summary>The glyph name the simple font's encoding gives the code, through the Adobe Glyph List (§9.10.2, §9.6.5).</summary>
    GlyphName,

    /// <summary>The code's CID through the Registry-Ordering-UCS2 table of the font's character collection (§9.10.2).</summary>
    CidCollection,

    /// <summary>The glyph's code point in a Unicode "cmap" subtable of the embedded TrueType or OpenType program.</summary>
    FontProgram,
}
