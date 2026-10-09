namespace Broadside.Fonts;

/// <summary>The type of a font, from the <c>Subtype</c> entry of its font dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §9.5, Table 108. Type 0 fonts are composite; the others are simple fonts (§9.6). CIDFonts (<c>CIDFontType0</c>,
/// <c>CIDFontType2</c>) are not listed: they are used only as the descendant of a Type 0 font, never as a font on their own.
/// </remarks>
public enum PdfFontType
{
    /// <summary><c>Type1</c>: a Type 1 font (§9.6.2), including a Standard 14 font.</summary>
    Type1 = 0,

    /// <summary><c>MMType1</c>: an instance of a multiple master font (§9.6.2.3).</summary>
    MMType1 = 1,

    /// <summary><c>TrueType</c>: a TrueType or OpenType font (§9.6.3).</summary>
    TrueType = 2,

    /// <summary><c>Type3</c>: a font whose glyphs are content streams (§9.6.4).</summary>
    Type3 = 3,

    /// <summary><c>Type0</c>: a composite font (§9.7).</summary>
    Type0 = 4,
}
