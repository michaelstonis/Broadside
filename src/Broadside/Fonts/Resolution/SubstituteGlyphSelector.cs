namespace Broadside.Fonts.Resolution;

/// <summary>
/// The glyph id of each character code of a simple font in its substitute program, chosen by glyph name: each code is worked out
/// on first use and kept.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.5.2 (a Type 1 font selects glyphs by the encoding's glyph name; a name the program lacks selects
/// <c>.notdef</c>) and §9.6.5.4 (a TrueType program is reached through its "cmap": a glyph name's Unicode value from the Adobe Glyph
/// List in a (3, 1) subtable, its Mac OS Roman code in a (1, 0) subtable, the "post" names, and for a symbolic font the code in a
/// (3, 0) subtable with the high byte 0xF0). A substitute's program is unrelated to the font's encoding, so names are the common
/// ground: the code's name from the font's encoding, then the program's own names (a CFF charset, Type 1 CharStrings, TrueType
/// "post"), its Unicode value in the Unicode subtables, its Mac OS Roman code in the (1, 0) subtable. A symbolic font tries the code
/// first, through (3, 0) as 0xF000 plus the code and (1, 0) as the code, as PDFBox does for SymbolMT.
/// </para>
/// </remarks>
internal sealed class SubstituteGlyphSelector
{
    private static readonly (int Platform, int Encoding)[] UnicodeSubtables = [(3, 1), (0, 3), (0, 0), (0, 1), (3, 10), (0, 4)];

    private readonly int[] _glyphs = new int[256];
    private readonly FontCharacterMap[] _unicode;
    private readonly FontCharacterMap? _symbol;
    private readonly FontCharacterMap? _macintosh;

    public SubstituteGlyphSelector(SimpleFontMetrics metrics, FontSubstitute substitute)
    {
        Metrics = metrics;
        Substitute = substitute;
        Array.Fill(_glyphs, -1);
        IReadOnlyList<FontCharacterMap> maps = substitute.Program.CharacterMaps;
        _unicode = [.. UnicodeSubtables.Select(id => Find(maps, id.Platform, id.Encoding)).OfType<FontCharacterMap>()];
        _symbol = Find(maps, 3, 0);
        _macintosh = Find(maps, 1, 0);
    }

    /// <summary>Gets the names the selector was built from.</summary>
    public SimpleFontMetrics Metrics { get; }

    /// <summary>Gets the substitute the selector was built for.</summary>
    public FontSubstitute Substitute { get; }

    /// <summary>Gets the glyph id of a code; 0 when the substitute has no glyph for it.</summary>
    public int GetGlyphId(byte code)
    {
        int cached = _glyphs[code];
        if (cached >= 0)
        {
            return cached;
        }

        int glyph = Select(code);
        _glyphs[code] = glyph;
        return glyph;
    }

    private int Select(byte code)
    {
        if (Substitute.IsSymbolic && ByCode(code) is var byCode and > 0)
        {
            return byCode;
        }

        return ByName(Metrics.Names[code]);
    }

    private int ByCode(byte code)
    {
        if (_symbol is not null)
        {
            foreach (int high in (ReadOnlySpan<int>)[0xF000, 0x0000, 0xF100, 0xF200])
            {
                if (_symbol.GetGlyphId(high | code) is var glyph and > 0)
                {
                    return glyph;
                }
            }
        }

        return _macintosh?.GetGlyphId(code) ?? 0;
    }

    private int ByName(string name)
    {
        if (name == GlyphNameTable.NotDef)
        {
            return 0;
        }

        FontProgram program = Substitute.Program;
        if (program.TryGetGlyphId(name, out int glyph) && glyph > 0)
        {
            return glyph;
        }

        if (AdobeGlyphList.TryGetScalar(name, Substitute.IsZapfDingbats, out int scalar))
        {
            foreach (FontCharacterMap map in _unicode)
            {
                if (map.GetGlyphId(scalar) is var byUnicode and > 0)
                {
                    return byUnicode;
                }
            }
        }

        if (_macintosh is not null)
        {
            ReadOnlySpan<short> macOsRoman = GlyphNameTable.Table(BuiltInEncoding.MacOSRoman);
            int index = GlyphNameTable.IndexOf(name);
            if (index >= 0 && macOsRoman.IndexOf((short)index) is var macCode and >= 0 && _macintosh.GetGlyphId(macCode) is var byMac and > 0)
            {
                return byMac;
            }
        }

        return 0;
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
}
