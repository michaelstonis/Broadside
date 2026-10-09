using System.Collections.Frozen;

namespace Broadside.Fonts;

/// <summary>Lookups over the generated Standard 14 metrics (<c>Standard14Data.g.cs</c>) and the font names that select them.</summary>
/// <remarks>ISO 32000-2 §9.6.2.1 (the paragraph after Table 109) and §9.6.2.2. Every lookup allocates nothing.</remarks>
internal static partial class Standard14Data
{
    /// <summary>The width Acrobat gives <c>.notdef</c> in a Standard 14 font other than Courier (PDFBox follows it; PDFBOX-2334).</summary>
    private const short NotDefWidth = 250;

    /// <summary>The PostScript names of the fourteen fonts, in <see cref="Standard14Font"/> order.</summary>
    private static readonly string[] PostScriptNames =
    [
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Symbol", "ZapfDingbats",
    ];

    /// <summary>The fourteen names exactly as §9.6.2.2 lists them.</summary>
    private static readonly FrozenDictionary<string, Standard14Font> CanonicalNames =
        PostScriptNames.Select((name, index) => KeyValuePair.Create(name, (Standard14Font)index)).ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Other names producers write for the fourteen fonts, after <see cref="Normalize"/>, compared ignoring case: the union of pdf.js
    /// (<c>getStdFontMap</c>) and PDFBox (<c>Standard14Fonts</c>), without the Arial Narrow, Arial Black and Arial Unicode MS entries,
    /// whose metrics differ from Helvetica's.
    /// </summary>
    private static readonly FrozenDictionary<string, Standard14Font> AliasNames = new Dictionary<string, Standard14Font>(StringComparer.OrdinalIgnoreCase)
    {
        ["Courier"] = Standard14Font.Courier,
        ["CourierNew"] = Standard14Font.Courier,
        ["CourierNewPSMT"] = Standard14Font.Courier,
        ["CourierCourierNew"] = Standard14Font.Courier,
        ["Courier-Bold"] = Standard14Font.CourierBold,
        ["CourierNew-Bold"] = Standard14Font.CourierBold,
        ["CourierNewPS-BoldMT"] = Standard14Font.CourierBold,
        ["Courier-Oblique"] = Standard14Font.CourierOblique,
        ["Courier-Italic"] = Standard14Font.CourierOblique,
        ["CourierNew-Italic"] = Standard14Font.CourierOblique,
        ["CourierNewPS-ItalicMT"] = Standard14Font.CourierOblique,
        ["Courier-BoldOblique"] = Standard14Font.CourierBoldOblique,
        ["Courier-BoldItalic"] = Standard14Font.CourierBoldOblique,
        ["CourierNew-BoldItalic"] = Standard14Font.CourierBoldOblique,
        ["CourierNewPS-BoldItalicMT"] = Standard14Font.CourierBoldOblique,
        ["Helvetica"] = Standard14Font.Helvetica,
        ["Arial"] = Standard14Font.Helvetica,
        ["ArialMT"] = Standard14Font.Helvetica,
        ["Helvetica-Bold"] = Standard14Font.HelveticaBold,
        ["Arial-Bold"] = Standard14Font.HelveticaBold,
        ["Arial-BoldMT"] = Standard14Font.HelveticaBold,
        ["Arial-BoldMT-Bold"] = Standard14Font.HelveticaBold,
        ["HelveticaLTStd-Bold"] = Standard14Font.HelveticaBold,
        ["Helvetica-Oblique"] = Standard14Font.HelveticaOblique,
        ["Helvetica-Italic"] = Standard14Font.HelveticaOblique,
        ["Arial-Italic"] = Standard14Font.HelveticaOblique,
        ["Arial-ItalicMT"] = Standard14Font.HelveticaOblique,
        ["Arial-ItalicMT-Italic"] = Standard14Font.HelveticaOblique,
        ["Helvetica-BoldOblique"] = Standard14Font.HelveticaBoldOblique,
        ["Helvetica-BoldItalic"] = Standard14Font.HelveticaBoldOblique,
        ["Arial-BoldItalic"] = Standard14Font.HelveticaBoldOblique,
        ["Arial-BoldItalicMT"] = Standard14Font.HelveticaBoldOblique,
        ["Arial-BoldItalicMT-BoldItalic"] = Standard14Font.HelveticaBoldOblique,
        ["Times-Roman"] = Standard14Font.TimesRoman,
        ["Times"] = Standard14Font.TimesRoman,
        ["TimesNewRoman"] = Standard14Font.TimesRoman,
        ["TimesNewRomanPS"] = Standard14Font.TimesRoman,
        ["TimesNewRomanPSMT"] = Standard14Font.TimesRoman,
        ["Times-Bold"] = Standard14Font.TimesBold,
        ["TimesNewRoman-Bold"] = Standard14Font.TimesBold,
        ["TimesNewRomanPS-Bold"] = Standard14Font.TimesBold,
        ["TimesNewRomanPS-BoldMT"] = Standard14Font.TimesBold,
        ["TimesNewRomanPSMT-Bold"] = Standard14Font.TimesBold,
        ["Times-Italic"] = Standard14Font.TimesItalic,
        ["TimesNewRoman-Italic"] = Standard14Font.TimesItalic,
        ["TimesNewRomanPS-Italic"] = Standard14Font.TimesItalic,
        ["TimesNewRomanPS-ItalicMT"] = Standard14Font.TimesItalic,
        ["TimesNewRomanPSMT-Italic"] = Standard14Font.TimesItalic,
        ["Times-BoldItalic"] = Standard14Font.TimesBoldItalic,
        ["TimesNewRoman-BoldItalic"] = Standard14Font.TimesBoldItalic,
        ["TimesNewRomanPS-BoldItalic"] = Standard14Font.TimesBoldItalic,
        ["TimesNewRomanPS-BoldItalicMT"] = Standard14Font.TimesBoldItalic,
        ["TimesNewRomanPSMT-BoldItalic"] = Standard14Font.TimesBoldItalic,
        ["Symbol"] = Standard14Font.Symbol,
        ["Symbol-Bold"] = Standard14Font.Symbol,
        ["Symbol-Italic"] = Standard14Font.Symbol,
        ["Symbol-BoldItalic"] = Standard14Font.Symbol,
        ["ZapfDingbats"] = Standard14Font.ZapfDingbats,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the PostScript name of a Standard 14 font, as §9.6.2.2 spells it.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The name, such as <c>Helvetica-Bold</c>.</returns>
    public static string PostScriptName(Standard14Font font) => PostScriptNames[(int)font];

    /// <summary>Gets a value indicating whether the font is one of the two symbolic Standard 14 fonts.</summary>
    /// <param name="font">The font.</param>
    /// <returns><see langword="true"/> for Symbol and ZapfDingbats.</returns>
    public static bool IsSymbolic(Standard14Font font) => font is Standard14Font.Symbol or Standard14Font.ZapfDingbats;

    /// <summary>Gets the built-in encoding of a Standard 14 font program (§9.6.5.1): its own for Symbol and ZapfDingbats, StandardEncoding otherwise.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The encoding.</returns>
    public static BuiltInEncoding EncodingOf(Standard14Font font) => font switch
    {
        Standard14Font.Symbol => BuiltInEncoding.Symbol,
        Standard14Font.ZapfDingbats => BuiltInEncoding.ZapfDingbats,
        _ => BuiltInEncoding.Standard,
    };

    /// <summary>Gets one font-wide metric of a Standard 14 font, from the AFM file.</summary>
    /// <param name="font">The font.</param>
    /// <param name="metric">Which metric.</param>
    /// <returns>The value, in glyph space units (thousandths of text space); the italic angle in degrees.</returns>
    public static double Metric(Standard14Font font, Standard14Metric metric)
    {
        short value = FontMetrics[((int)font * MetricsStride) + (int)metric];
        return metric == Standard14Metric.ItalicAngle ? value / 10.0 : value;
    }

    /// <summary>Gets the font descriptor flags of a Standard 14 font, synthesized from its AFM file.</summary>
    /// <param name="font">The font.</param>
    /// <returns>FixedPitch for Courier, Serif for Times, Italic for a non-zero italic angle, and Symbolic or Nonsymbolic from the encoding scheme.</returns>
    public static PdfFontFlags Flags(Standard14Font font) => (PdfFontFlags)FontMetrics[((int)font * MetricsStride) + (int)Standard14Metric.Flags];

    /// <summary>Gets the font bounding box of a Standard 14 font.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The AFM <c>FontBBox</c>.</returns>
    public static PdfRectangle FontBBox(Standard14Font font) => new(
        Metric(font, Standard14Metric.BBoxLeft),
        Metric(font, Standard14Metric.BBoxBottom),
        Metric(font, Standard14Metric.BBoxRight),
        Metric(font, Standard14Metric.BBoxTop));

    /// <summary>
    /// Gets the width of a glyph of a Standard 14 font. <c>.notdef</c>, which the AFM files do not list, is 250 (600 in Courier),
    /// as in Acrobat. A name the font does not use is looked up by its Unicode value (Adobe Glyph List, <c>uniXXXX</c>), with the
    /// no-break space read as the space and the soft hyphen as the hyphen, as Annex D notes 5 and 6 allow.
    /// </summary>
    /// <param name="font">The font.</param>
    /// <param name="glyphName">The glyph name.</param>
    /// <param name="width">The width in glyph space units (thousandths of text space).</param>
    /// <returns><see langword="false"/> when the font has no glyph for the name.</returns>
    public static bool TryGetWidth(Standard14Font font, string glyphName, out double width)
    {
        int stride = (int)font * NameCount;
        int index = GlyphNameTable.IndexOf(glyphName);
        if (index >= 0 && Widths[stride + index] is var direct and >= 0)
        {
            width = direct;
            return true;
        }

        if (glyphName == GlyphNameTable.NotDef)
        {
            width = font <= Standard14Font.CourierBoldOblique ? 600 : NotDefWidth;
            return true;
        }

        if (AdobeGlyphList.TryGetScalar(glyphName, font == Standard14Font.ZapfDingbats, out int scalar))
        {
            scalar = scalar switch
            {
                0x00A0 => 0x0020,
                0x00AD => 0x002D,
                _ => scalar,
            };
            index = FindByUnicode(font, scalar);
            if (index >= 0 && Widths[stride + index] is var byUnicode and >= 0)
            {
                width = byUnicode;
                return true;
            }
        }

        width = 0;
        return false;
    }

    /// <summary>Gets a value indicating whether a Standard 14 font has a glyph of this name (its AFM file lists it).</summary>
    /// <param name="font">The font.</param>
    /// <param name="glyphName">The glyph name.</param>
    /// <returns><see langword="true"/> when the AFM file has the glyph.</returns>
    public static bool HasGlyph(Standard14Font font, string glyphName) =>
        GlyphNameTable.IndexOf(glyphName) is var index and >= 0 && Widths[((int)font * NameCount) + index] >= 0;

    /// <summary>Finds the Standard 14 font a <c>BaseFont</c> name selects.</summary>
    /// <param name="baseFont">The name, possibly with a subset tag.</param>
    /// <param name="font">The font.</param>
    /// <param name="isAlias">Whether the name is not one of the fourteen names of §9.6.2.2 but another name for the same font.</param>
    /// <returns><see langword="true"/> when the name selects a Standard 14 font.</returns>
    public static bool TryMatch(string baseFont, out Standard14Font font, out bool isAlias)
    {
        string name = WithoutSubsetTag(baseFont);
        if (CanonicalNames.TryGetValue(name, out font))
        {
            isAlias = name.Length != baseFont.Length;
            return true;
        }

        isAlias = true;
        return AliasNames.TryGetValue(Normalize(name), out font);
    }

    /// <summary>Removes a subset tag, six uppercase letters and a plus sign (§9.9.2).</summary>
    private static string WithoutSubsetTag(string name)
    {
        if (name.Length > 7 && name[6] == '+')
        {
            for (int index = 0; index < 6; index++)
            {
                if (!char.IsAsciiLetterUpper(name[index]))
                {
                    return name;
                }
            }

            return name[7..];
        }

        return name;
    }

    /// <summary>pdf.js's normalization: commas and underscores become hyphens, white space is removed (<c>Arial,Bold</c> is <c>Arial-Bold</c>).</summary>
    private static string Normalize(string name)
    {
        Span<char> buffer = name.Length <= 128 ? stackalloc char[name.Length] : new char[name.Length];
        int length = 0;
        foreach (char ch in name)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            buffer[length++] = ch is ',' or '_' ? '-' : ch;
        }

        return new string(buffer[..length]);
    }

    private static int FindByUnicode(Standard14Font font, int scalar)
    {
        if (scalar > ushort.MaxValue)
        {
            return -1;
        }

        int position;
        switch (font)
        {
            case Standard14Font.Symbol:
                position = SymbolUnicodes.BinarySearch((ushort)scalar);
                return position >= 0 ? SymbolUnicodeNames[position] : -1;
            case Standard14Font.ZapfDingbats:
                position = ZapfDingbatsUnicodes.BinarySearch((ushort)scalar);
                return position >= 0 ? ZapfDingbatsUnicodeNames[position] : -1;
            default:
                position = LatinUnicodes.BinarySearch((ushort)scalar);
                return position >= 0 ? LatinUnicodeNames[position] : -1;
        }
    }
}

/// <summary>The font-wide metrics of <see cref="Standard14Data.FontMetrics"/>, in stored order.</summary>
internal enum Standard14Metric
{
    /// <summary>FontBBox lower-left x.</summary>
    BBoxLeft,

    /// <summary>FontBBox lower-left y.</summary>
    BBoxBottom,

    /// <summary>FontBBox upper-right x.</summary>
    BBoxRight,

    /// <summary>FontBBox upper-right y.</summary>
    BBoxTop,

    /// <summary>ItalicAngle, stored times ten.</summary>
    ItalicAngle,

    /// <summary>CapHeight; for Symbol and ZapfDingbats, the font box top.</summary>
    CapHeight,

    /// <summary>XHeight; 0 for Symbol and ZapfDingbats.</summary>
    XHeight,

    /// <summary>Ascender; for Symbol and ZapfDingbats, the font box top.</summary>
    Ascent,

    /// <summary>Descender; for Symbol and ZapfDingbats, the font box bottom.</summary>
    Descent,

    /// <summary>StdVW, the font descriptor's StemV.</summary>
    StemV,

    /// <summary>StdHW, the font descriptor's StemH.</summary>
    StemH,

    /// <summary>The synthesized Table 121 flags.</summary>
    Flags,
}
