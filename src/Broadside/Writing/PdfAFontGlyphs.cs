using Broadside.Fonts;

namespace Broadside.Writing;

/// <summary>
/// The glyph and width facts of an embedded simple font that PDF/A checks for each code the content shows (issue #229): the glyph
/// must be in the program (veraPDF rule 6.2.11.4.1-2, never .notdef, 6.2.11.8-1) and its width the font dictionary's (6.2.11.5-1).
/// </summary>
internal static class PdfAFontGlyphs
{
    /// <summary>The difference, in thousandths of text space, beyond which veraPDF finds a width inconsistent (6.2.11.5-1).</summary>
    public const double WidthTolerance = 1;

    /// <summary>The glyph the embedded program gives <paramref name="code"/> (0: none), or <see langword="null"/> when the font is not checked.</summary>
    public static int? Find(PdfSimpleFont font, byte code) => font.Program is null ? null : font switch
    {
        PdfTrueTypeFont trueType => trueType.GetGlyphId(code),
        PdfType1Font type1 => type1.GetGlyphId(code),
        _ => null,
    };

    /// <summary>The advance width of a glyph of the embedded program, in thousandths of text space.</summary>
    public static double ProgramWidth(PdfSimpleFont font, int glyph) =>
        font.Program!.GetMetrics(glyph).AdvanceWidth * font.Program.FontMatrix.A * 1000;
}
