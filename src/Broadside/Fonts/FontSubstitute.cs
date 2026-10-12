namespace Broadside.Fonts;

/// <summary>
/// The font program a font that is not embedded is drawn with, found by the engine's font resolvers: what
/// <see cref="PdfSimpleFont.Substitute"/> returns.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.2.2 (the Standard 14 fonts "or their font metrics and suitable substitution fonts"), §9.8 and §9.2.4. A
/// substitute supplies glyph shapes only: text is positioned by the font's own widths (<see cref="PdfSimpleFont.GetWidth"/>), which
/// may differ from the substitute's (<see cref="GetWidth"/>). A renderer that wants the glyph to fill the space the PDF gives it
/// scales it horizontally by the ratio of the two.
/// </para>
/// <para>The glyph a character code selects in <see cref="Program"/> is <see cref="PdfType1Font.GetGlyphId"/> or <see cref="PdfTrueTypeFont.GetGlyphId"/>.</para>
/// </remarks>
public sealed class FontSubstitute
{
    internal FontSubstitute(FontProgram program, string name, string? requestedName, FontMatchKind matchKind, bool isSymbolic, bool isZapfDingbats)
    {
        Program = program;
        Name = name;
        RequestedName = requestedName;
        MatchKind = matchKind;
        IsSymbolic = isSymbolic;
        IsZapfDingbats = isZapfDingbats;
    }

    /// <summary>Gets the substitute font program, shared by every font of the document that uses the same program.</summary>
    public FontProgram Program { get; }

    /// <summary>Gets the name of the substitute font, as the resolver that found it gave it (such as <c>LiberationSans</c>).</summary>
    public string Name { get; }

    /// <summary>Gets the name of the font it stands in for: its <c>BaseFont</c>; <see langword="null"/> when the font has none.</summary>
    public string? RequestedName { get; }

    /// <summary>Gets how the substitute relates to the font it stands in for.</summary>
    public FontMatchKind MatchKind { get; }

    /// <summary>Gets a value indicating whether the font is symbolic, which selects glyphs by code before glyph names.</summary>
    internal bool IsSymbolic { get; }

    /// <summary>Gets a value indicating whether the glyph names are ZapfDingbats names (a1, a2, ...), read through its own glyph list.</summary>
    internal bool IsZapfDingbats { get; }

    /// <summary>Gets the advance width of one of the substitute's glyphs, in thousandths of text space.</summary>
    /// <param name="glyphId">The glyph id in <see cref="Program"/>.</param>
    /// <returns>The width; 0 when the program records none for the glyph.</returns>
    /// <remarks>ISO 32000-2 §9.2.4: the program's advance mapped through its font matrix.</remarks>
    public double GetWidth(int glyphId) => Program.GetMetrics(glyphId).AdvanceWidth * Program.FontMatrix.A * 1000;
}
