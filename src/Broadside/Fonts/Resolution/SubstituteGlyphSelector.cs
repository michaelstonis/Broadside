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
/// first, through (3, 0) with the high bytes of §9.6.5.4 and (1, 0) as the code, as PDFBox does for SymbolMT. Subtables are ranked by
/// <see cref="CharacterMapSelection"/>, as for embedded programs.
/// </para>
/// </remarks>
internal sealed class SubstituteGlyphSelector(SimpleFontMetrics metrics, FontSubstitute substitute)
{
    private readonly GlyphIdCache _glyphs = new();
    private readonly CharacterMapSelection _maps = substitute.Program.CharacterMapSelection;

    /// <summary>Gets the names the selector was built from.</summary>
    public SimpleFontMetrics Metrics { get; } = metrics;

    /// <summary>Gets the substitute the selector was built for.</summary>
    public FontSubstitute Substitute { get; } = substitute;

    /// <summary>Gets the glyph id of a code; 0 when the substitute has no glyph for it.</summary>
    public int GetGlyphId(byte code)
    {
        if (!_glyphs.TryGet(code, out int glyph))
        {
            glyph = Select(code);
            _glyphs.Set(code, glyph);
        }

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
        int glyph = _maps.GetGlyphIdForSymbolCode(code);
        return glyph > 0 ? glyph : _maps.GetGlyphIdForMacintoshCode(code);
    }

    private int ByName(string name)
    {
        if (name == GlyphNameTable.NotDef)
        {
            return 0;
        }

        if (Substitute.Program.TryGetGlyphId(name, out int glyph) && glyph > 0)
        {
            return glyph;
        }

        if (AdobeGlyphList.TryGetScalar(name, Substitute.IsZapfDingbats, out int scalar) && _maps.GetGlyphIdForUnicode(scalar, out _) is var byUnicode and > 0)
        {
            return byUnicode;
        }

        return Math.Max(0, _maps.GetGlyphIdForMacOSRomanName(name));
    }
}
