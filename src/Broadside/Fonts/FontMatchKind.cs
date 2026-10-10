namespace Broadside.Fonts;

/// <summary>How the program a font resolver found relates to the font asked for.</summary>
/// <remarks>
/// ISO 32000-2 §9.5 NOTE 5: font naming, substitution and glyph selection for fonts that are not embedded are implementation
/// dependent; §9.6.2.2 for the Standard 14 fonts and §9.8.1 for synthesizing or selecting a similar font from the font descriptor.
/// </remarks>
public enum FontMatchKind
{
    /// <summary>The font itself, found by its name.</summary>
    Exact = 0,

    /// <summary>
    /// A font standing in for a Standard 14 font, or for a name that is another name of one (such as Liberation Sans for Helvetica or
    /// <c>Arial,Bold</c>); the metrics still come from the Standard 14 font.
    /// </summary>
    Standard14 = 1,

    /// <summary>A font of the same family, matched by family name and style (such as Arial Bold Italic for <c>Arial,BoldItalic</c>).</summary>
    Family = 2,

    /// <summary>
    /// No font of that name or family was found: a Standard 14 stand-in was chosen from the font descriptor's flags and metrics and
    /// the font's name (fixed pitch, serif, bold, italic).
    /// </summary>
    Similar = 3,
}
