using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A TrueType or OpenType font: a live view over its font dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §9.6.3. The dictionary has the entries of a Type 1 font dictionary (Table 109). Glyph names from the encoding are
/// what §9.6.5.4 maps through the font program's "cmap" table for a nonsymbolic font; a non-embedded TrueType font named after a
/// Standard 14 font (such as <c>Arial</c>) takes that font's metrics. <see cref="GetGlyphId"/> selects the glyph of a character
/// code in the embedded program (<see cref="PdfFont.Program"/>).
/// </remarks>
public sealed class PdfTrueTypeFont : PdfSimpleFont
{
    private volatile TrueTypeGlyphSelector? _selector;

    internal PdfTrueTypeFont(PdfDocument document, CosDictionary dictionary, CosReference? reference)
        : base(document, dictionary, reference, PdfFontType.TrueType)
    {
    }

    /// <summary>Gets the glyph id that a character code selects in the font's program; 0, the missing glyph, when none.</summary>
    /// <param name="code">The character code.</param>
    /// <returns>
    /// The glyph id in <see cref="PdfFont.Program"/>, or, for a font without a usable embedded program, in the program of
    /// <see cref="PdfSimpleFont.Substitute"/> (selected by glyph name); 0 for every code when the font has neither.
    /// </returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the font or its program.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.6.5.4. A font with <c>MacRomanEncoding</c> or <c>WinAnsiEncoding</c>, an encoding dictionary, or the
    /// Nonsymbolic flag selects by glyph name: the encoding's name for the code, its Unicode value from the Adobe Glyph List, and the
    /// program's (3, 1) "cmap" subtable; without (3, 1), the name's Mac OS Roman code (Table 113) and the (1, 0) subtable; failing
    /// both, the program's "post" names. A symbolic font, or one without an encoding, selects by the code: the (3, 0) subtable with the
    /// high byte 0x00, 0xF0, 0xF1 or 0xF2, else the (1, 0) subtable.
    /// </para>
    /// <para>
    /// When that finds nothing, the font falls back as viewers do and records an information diagnostic: the other route, the code as
    /// a Unicode value, and for a symbolic program without a usable "cmap", the code as the glyph id. Each code is worked out once and
    /// kept until the font dictionary, its encoding or descriptor, or the program changes.
    /// </para>
    /// </remarks>
    public int GetGlyphId(byte code)
    {
        SimpleFontMetrics metrics = Metrics;
        FontProgram? program = Program;
        if (program is null)
        {
            return GetSubstituteGlyphId(code);
        }

        TrueTypeGlyphSelector? selector = _selector;
        if (selector is null || selector.Metrics != metrics || selector.Program != program)
        {
            selector = new TrueTypeGlyphSelector(this, metrics, program);
            _selector = selector;
        }

        return selector.GetGlyphId(code);
    }
}
