using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// The glyph id of each character code of a simple TrueType font, chosen as ISO 32000-2 §9.6.5.4 describes, through the font
/// program's "cmap" subtables and "post" names. Each code is worked out on first use and kept.
/// </summary>
/// <remarks>
/// <para>
/// §9.6.5.4: when the encoding is <c>MacRomanEncoding</c> or <c>WinAnsiEncoding</c>, or the font is nonsymbolic, a code becomes a
/// glyph name through the encoding, then a Unicode value through the Adobe Glyph List and a glyph through the (3, 1) subtable; or,
/// without one, a Mac OS Roman code (MacRomanEncoding plus Table 113) and a glyph through the (1, 0) subtable; failing both, the
/// glyph of that name in "post". When the font has no encoding or is symbolic, the code itself selects the glyph: through the (3, 0)
/// subtable with the high byte 0x00, 0xF0, 0xF1 or 0xF2 (§9.6.5.4 allows those four ranges), else through the (1, 0) subtable.
/// </para>
/// <para>
/// When the prescribed route finds nothing, "a PDF processor may supply a mapping of its choosing" (§9.6.5.4, last paragraph):
/// the other route, then the code as a Unicode value in a Unicode subtable (as pdf.js and PDFBox do for symbolic fonts that carry
/// a Unicode "cmap"), then, for a symbolic font whose program has no usable "cmap", the code as the glyph id (as pdf.js does).
/// Each such choice is recorded as an information diagnostic. The (0, 3), (0, 0), (0, 1), (3, 10) and (0, 4) subtables stand in
/// for (3, 1) as Unicode subtables.
/// </para>
/// </remarks>
internal sealed class TrueTypeGlyphSelector
{
    private static readonly (int Platform, int Encoding)[] UnicodeSubtables = [(3, 1), (0, 3), (0, 0), (0, 1), (3, 10), (0, 4)];

    private readonly PdfTrueTypeFont _font;
    private readonly int[] _glyphs = new int[256];
    private readonly FontCharacterMap[] _unicode;
    private readonly FontCharacterMap? _symbol;
    private readonly FontCharacterMap? _macintosh;
    private readonly bool _byName;
    private readonly bool _encodingGiven;

    public TrueTypeGlyphSelector(PdfTrueTypeFont font, SimpleFontMetrics metrics, FontProgram? program)
    {
        _font = font;
        Metrics = metrics;
        Program = program;
        Array.Fill(_glyphs, -1);
        IReadOnlyList<FontCharacterMap> maps = program?.CharacterMaps ?? [];
        _unicode = [.. UnicodeSubtables.Select(id => Find(maps, id.Platform, id.Encoding)).OfType<FontCharacterMap>()];
        _symbol = Find(maps, 3, 0);
        _macintosh = Find(maps, 1, 0);

        CosObject? encoding = font.Get(FontNames.Encoding);
        bool winOrMac = FontNames.WinAnsiEncoding.Equals(encoding) || FontNames.MacRomanEncoding.Equals(encoding);
        bool dictionary = encoding is CosDictionary;
        _encodingGiven = winOrMac || dictionary;

        // §9.8.2: a processor checks the Symbolic flag. Without Flags, a font with no encoding is taken as symbolic (PDFBox).
        CosDictionary? descriptor = font.DescriptorDictionary;
        bool hasFlags = descriptor is not null && font.GetFrom(descriptor, FontNames.Flags) is CosInteger;
        PdfFontFlags flags = descriptor is null ? PdfFontFlags.None : PdfFontDescriptor.ReadFlags(descriptor, font.Document);
        bool symbolic = hasFlags ? flags.HasFlag(PdfFontFlags.Symbolic) : encoding is null;
        if (symbolic && flags.HasFlag(PdfFontFlags.Nonsymbolic) && encoding is CosDictionary encodingDictionary
            && font.GetFrom(encodingDictionary, FontNames.Differences) is CosArray)
        {
            // Both flags and a Differences array: the producer meant the names (pdf.js and PDFBox read it as nonsymbolic).
            symbolic = false;
            font.Report(
                DiagnosticCodes.FontGlyphMappingFallback,
                DiagnosticSeverity.Information,
                "The font descriptor sets both the Symbolic and Nonsymbolic flags (ISO 32000-2 §9.8.2, Table 121) and the encoding has a Differences array; glyphs are selected by name as for a nonsymbolic font.");
        }

        _byName = !symbolic && (winOrMac || dictionary || flags.HasFlag(PdfFontFlags.Nonsymbolic));
    }

    /// <summary>Gets the names and widths the selector was built from: a new set means the font changed.</summary>
    public SimpleFontMetrics Metrics { get; }

    /// <summary>Gets the program the selector was built from.</summary>
    public FontProgram? Program { get; }

    /// <summary>Gets the glyph id of a character code; 0 (the missing glyph) when none applies.</summary>
    public int GetGlyphId(byte code)
    {
        int glyph = Volatile.Read(ref _glyphs[code]);
        if (glyph < 0)
        {
            glyph = Program is null ? 0 : Select(code);
            Volatile.Write(ref _glyphs[code], glyph);
        }

        return glyph;
    }

    private static FontCharacterMap? Find(IReadOnlyList<FontCharacterMap> maps, int platform, int encoding)
    {
        foreach (FontCharacterMap map in maps)
        {
            if (map.PlatformId == platform && map.EncodingId == encoding)
            {
                return map;
            }
        }

        return null;
    }

    private int Select(byte code)
    {
        if (_byName)
        {
            int glyph = ByName(code);
            if (glyph == 0 && (glyph = ByCode(code)) != 0)
            {
                ReportFallback(code, "its glyph name selects no glyph, so the code itself is looked up in the (3, 0) or (1, 0) \"cmap\" subtable");
            }

            return glyph;
        }

        int selected = ByCode(code);
        if (selected != 0)
        {
            return selected;
        }

        if (_encodingGiven && (selected = ByName(code)) != 0)
        {
            ReportFallback(code, "the code selects no glyph through the (3, 0) or (1, 0) \"cmap\" subtable, so its glyph name from the encoding is used");
            return selected;
        }

        foreach (FontCharacterMap map in _unicode)
        {
            if ((selected = map.GetGlyphId(code)) != 0)
            {
                ReportFallback(code, string.Create(CultureInfo.InvariantCulture, $"the code selects no glyph through the (3, 0) or (1, 0) \"cmap\" subtable, so it is looked up as a Unicode value in the ({map.PlatformId}, {map.EncodingId}) subtable"));
                return selected;
            }
        }

        if (_symbol is null && _macintosh is null && _unicode.Length == 0 && code < Program!.GlyphCount)
        {
            ReportFallback(code, "the font program has no usable \"cmap\" subtable, so the code is used as the glyph id");
            return code;
        }

        return 0;
    }

    /// <summary>The name route: encoding name, then (3, 1) by Unicode or (1, 0) by Mac OS Roman code, then "post".</summary>
    private int ByName(byte code)
    {
        string name = Metrics.Names[code];
        if (name == GlyphNameTable.NotDef)
        {
            return 0;
        }

        int glyph;
        if (_unicode.Length > 0)
        {
            if (AdobeGlyphList.TryGetScalar(name, zapfDingbats: false, out int scalar))
            {
                foreach (FontCharacterMap map in _unicode)
                {
                    if ((glyph = map.GetGlyphId(scalar)) != 0)
                    {
                        return glyph;
                    }
                }
            }
        }
        else if (_macintosh is not null && MacOSRomanCode(name) is >= 0 and var macCode && (glyph = _macintosh.GetGlyphId(macCode)) != 0)
        {
            return glyph;
        }

        return Program!.TryGetGlyphId(name, out glyph) ? glyph : 0;
    }

    /// <summary>The code route: (3, 0) with each of the four high bytes, then (1, 0).</summary>
    private int ByCode(byte code)
    {
        int glyph;
        if (_symbol is not null)
        {
            foreach (int high in (ReadOnlySpan<int>)[0x00, 0xF0, 0xF1, 0xF2])
            {
                if ((glyph = _symbol.GetGlyphId((high << 8) | code)) != 0)
                {
                    return glyph;
                }
            }
        }

        if (_macintosh is null || (glyph = _macintosh.GetGlyphId(code)) == 0)
        {
            return 0;
        }

        if (_symbol is not null)
        {
            ReportFallback(code, "the (3, 0) \"cmap\" subtable does not map the code, so the (1, 0) subtable is used");
        }

        return glyph;
    }

    /// <summary>The Mac OS Roman code of a glyph name: MacRomanEncoding with the Table 113 additions (§9.6.5.4).</summary>
    private static int MacOSRomanCode(string name)
    {
        int index = GlyphNameTable.IndexOf(name);
        if (index < 0)
        {
            return -1;
        }

        ReadOnlySpan<short> table = GlyphNameTable.Table(BuiltInEncoding.MacOSRoman);
        for (int code = 0; code < table.Length; code++)
        {
            if (table[code] == index)
            {
                return code;
            }
        }

        return -1;
    }

    private void ReportFallback(byte code, string what) => _font.Report(
        DiagnosticCodes.FontGlyphMappingFallback,
        DiagnosticSeverity.Information,
        string.Create(CultureInfo.InvariantCulture, $"Character code {code} of the TrueType font: {what} (ISO 32000-2 §9.6.5.4 lets a processor choose a mapping)."));
}
