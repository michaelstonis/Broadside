using Broadside.Fonts.Resolution;

namespace Broadside.Fonts;

/// <summary>
/// What an <see cref="IFontResolver"/> is told about a font it is asked to find: the name as written, the name's parts, the font's
/// type, and the facts of its font descriptor, with the style inferred from them.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.2.1 (Table 109, <c>BaseFont</c>), §9.8.1 (Table 120: <c>FontFamily</c>, <c>FontStretch</c>, <c>FontWeight</c>,
/// <c>ItalicAngle</c>, <c>StemV</c>, the entries that let a processor "synthesise a substitute font or select a similar font"),
/// §9.8.2 (Table 121, the flags, of which Symbolic "may affect ... font substitution strategies") and §9.9.2 (the subset tag,
/// removed from <see cref="PostScriptName"/> and <see cref="FamilyName"/>).
/// </para>
/// <para>
/// The engine builds one for each font that needs a program; a resolver may build its own (in tests, or to forward to another
/// resolver). The inferred properties (<see cref="IsBold"/> and the others) are computed from the other properties when read, so
/// every resolver infers the same way.
/// </para>
/// </remarks>
public sealed class FontQuery
{
    private readonly ParsedFontName _parsed;

    /// <summary>Initializes a new instance of the <see cref="FontQuery"/> class.</summary>
    /// <param name="name">The font's name as written, such as its <c>BaseFont</c>; a subset tag is allowed.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public FontQuery(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        _parsed = FontNameParser.Parse(name);
    }

    /// <summary>Gets the font's name as written, such as <c>ABCDEF+Arial,BoldItalic</c>.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.1, Table 109.</remarks>
    public string Name { get; }

    /// <summary>Gets the name without its subset tag and white space, such as <c>Arial,BoldItalic</c>.</summary>
    /// <remarks>ISO 32000-2 §9.9.2.</remarks>
    public string PostScriptName => _parsed.PostScriptName;

    /// <summary>
    /// Gets the family the name states: the name without style words (Bold, Italic, Oblique, Regular, Roman and the like) and the
    /// vendor tags MT and PS, such as <c>Arial</c> for <c>Arial,BoldItalic</c> and <c>TimesNewRoman</c> for <c>TimesNewRomanPS-BoldMT</c>.
    /// </summary>
    public string FamilyName => _parsed.FamilyName;

    /// <summary>Gets the type of the font; <see cref="PdfFontType.Type1"/> by default.</summary>
    /// <remarks>ISO 32000-2 §9.5, Table 108. <see cref="PdfFontType.Type0"/> for the CIDFont of a composite font.</remarks>
    public PdfFontType FontType { get; init; } = PdfFontType.Type1;

    /// <summary>Gets the type of the CIDFont when <see cref="FontType"/> is <see cref="PdfFontType.Type0"/>; <see langword="null"/> otherwise.</summary>
    /// <remarks>ISO 32000-2 §9.7.4.</remarks>
    public PdfCidFontType? CidFontType { get; init; }

    /// <summary>Gets the character collection of a CIDFont; <see langword="null"/> for a simple font.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, Table 114.</remarks>
    public CidSystemInfo? SystemInfo { get; init; }

    /// <summary>Gets the Standard 14 font the name selects, directly or through another name for it; <see langword="null"/> when none.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.2.</remarks>
    public Standard14Font? Standard14 { get; init; }

    /// <summary>Gets the font descriptor's flags (or the Standard 14 font's, when it has no descriptor).</summary>
    /// <remarks>ISO 32000-2 §9.8.2, Table 121.</remarks>
    public PdfFontFlags Flags { get; init; }

    /// <summary>Gets the font descriptor's <c>FontFamily</c>, such as <c>Times New Roman</c>; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120.</remarks>
    public string? FontFamily { get; init; }

    /// <summary>Gets the font descriptor's <c>FontWeight</c> (100 to 900, 400 normal, 700 bold); <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120.</remarks>
    public int? FontWeight { get; init; }

    /// <summary>Gets the font descriptor's <c>FontStretch</c>; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120.</remarks>
    public PdfFontStretch? FontStretch { get; init; }

    /// <summary>Gets the font descriptor's <c>ItalicAngle</c>, in degrees counter-clockwise from the vertical (negative leans right).</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120.</remarks>
    public double ItalicAngle { get; init; }

    /// <summary>Gets the font descriptor's <c>StemV</c>, the thickness of vertical stems; 0 when unknown.</summary>
    /// <remarks>ISO 32000-2 §9.8.1, Table 120.</remarks>
    public double StemV { get; init; }

    /// <summary>
    /// Gets a value indicating whether the font is bold: the ForceBold flag, a <c>FontWeight</c> of 600 or more, a bold word in the
    /// name (Bold, Semibold, Demi, Black, Heavy), or, when neither weight nor name says, a <c>StemV</c> of 120 or more.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.8.1 and §9.8.2. The StemV threshold is this library's (Helvetica 88, Helvetica-Bold 140, Times-Bold 139).</remarks>
    public bool IsBold => Standard14 is { } standard14
        ? standard14 is Standard14Font.CourierBold or Standard14Font.CourierBoldOblique or Standard14Font.HelveticaBold or Standard14Font.HelveticaBoldOblique or Standard14Font.TimesBold or Standard14Font.TimesBoldItalic
        : Flags.HasFlag(PdfFontFlags.ForceBold) || Weight >= 600;

    /// <summary>Gets a value indicating whether the font is italic or oblique: the Italic flag, a non-zero <c>ItalicAngle</c>, or an italic word in the name.</summary>
    /// <remarks>ISO 32000-2 §9.8.1 and §9.8.2.</remarks>
    public bool IsItalic => Standard14 is { } standard14
        ? standard14 is Standard14Font.CourierOblique or Standard14Font.CourierBoldOblique or Standard14Font.HelveticaOblique or Standard14Font.HelveticaBoldOblique or Standard14Font.TimesItalic or Standard14Font.TimesBoldItalic
        : Flags.HasFlag(PdfFontFlags.Italic) || ItalicAngle != 0 || _parsed.Style.HasFlag(FontStyle.Italic);

    /// <summary>Gets a value indicating whether all glyphs have the same width: the FixedPitch flag.</summary>
    /// <remarks>ISO 32000-2 §9.8.2, Table 121.</remarks>
    public bool IsFixedPitch => Standard14 is { } standard14
        ? standard14 <= Standard14Font.CourierBoldOblique
        : Flags.HasFlag(PdfFontFlags.FixedPitch) || ContainsAny(FamilyKey, "courier", "mono", "consola", "typewriter");

    /// <summary>
    /// Gets a value indicating whether the glyphs have serifs: the Serif flag, or a name of a serif design (Times, Roman, Georgia,
    /// Garamond, a name with Serif but not Sans, and the like).
    /// </summary>
    /// <remarks>ISO 32000-2 §9.8.2, Table 121. The name list follows pdf.js's serif fonts (<c>standard_fonts.js</c>).</remarks>
    public bool IsSerif => Standard14 is { } standard14
        ? standard14 is >= Standard14Font.TimesRoman and <= Standard14Font.TimesBoldItalic
        : Flags.HasFlag(PdfFontFlags.Serif) || IsSerifName(FamilyKey);

    /// <summary>
    /// Gets a value indicating whether the font is symbolic: the Symbolic flag without the Nonsymbolic flag, a name of a symbol font
    /// (Symbol, Dingbats, Wingdings, Webdings), or the Standard 14 Symbol or ZapfDingbats. A symbolic font is never given a Latin
    /// text font in its place.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.8.2, Table 121: the Symbolic flag "may affect ... font substitution strategies".</remarks>
    public bool IsSymbolic => Standard14 is { } standard14
        ? standard14 is Standard14Font.Symbol or Standard14Font.ZapfDingbats
        : (Flags.HasFlag(PdfFontFlags.Symbolic) && !Flags.HasFlag(PdfFontFlags.Nonsymbolic)) || ContainsAny(FamilyKey, "symbol", "dingbat", "wingding", "webding");

    /// <summary>Gets the family key: <see cref="FamilyName"/> in lower case, letters and digits only.</summary>
    internal string FamilyKey => FontNameParser.FamilyKey(FamilyName);

    /// <summary>Gets the weight: <see cref="FontWeight"/>, else the name's, else 700 from <c>StemV</c> of 120 or more, else 400.</summary>
    internal int Weight => FontWeight ?? (_parsed.Weight != 0 ? _parsed.Weight : _parsed.Style.HasFlag(FontStyle.Bold) ? 700 : StemV >= 120 ? 700 : 400);

    /// <summary>Gets a value indicating whether the font is narrower than normal: a <c>FontStretch</c> below Normal, or Narrow or Condensed in the name.</summary>
    internal bool IsCondensed => FontStretch is { } stretch ? stretch < PdfFontStretch.Normal : _parsed.Style.HasFlag(FontStyle.Condensed);

    private static bool IsSerifName(string key) =>
        (key.Contains("serif", StringComparison.Ordinal) && !key.Contains("sans", StringComparison.Ordinal))
        || ContainsAny(key, "times", "roman", "georgia", "garamond", "cambria", "bookman", "palatino", "century", "minion", "baskerville", "caslon", "bodoni", "didot", "schoolbook", "antiqua", "tinos", "constantia", "rockwell", "mincho");

    private static bool ContainsAny(string key, params ReadOnlySpan<string> words)
    {
        foreach (string word in words)
        {
            if (key.Contains(word, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
