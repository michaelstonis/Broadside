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
/// <para>
/// A name none of those find is taken apart by the Adobe Glyph List specification's rules (§2, which ISO 32000-2 §9.10.2 refers to
/// for the Unicode value of a name): everything from the first period is dropped (<c>H.sc</c> selects <c>H</c>), the rest splits at
/// underscores into components selected one by one (<c>f_i</c> draws <c>f</c> then <c>i</c>, not U+FB01), and a <c>uni</c> component
/// of several groups of four hexadecimal digits stands for each group (<c>uni00410042</c> draws <c>A</c> then <c>B</c>). A component
/// the substitute has no glyph for is left out; a name with no component left selects nothing.
/// </para>
/// </remarks>
internal sealed class SubstituteGlyphSelector(SimpleFontMetrics metrics, FontSubstitute substitute)
{
    private readonly GlyphIdCache _glyphs = new();

    // The component glyphs of each code whose name decomposes into several, written before the code's entry in _glyphs (which is 0
    // for such a code), so a reader that finds the entry finds them.
    private readonly int[]?[] _components = new int[]?[256];
    private readonly CharacterMapSelection _maps = substitute.Program.CharacterMapSelection;

    /// <summary>Gets the names the selector was built from.</summary>
    public SimpleFontMetrics Metrics { get; } = metrics;

    /// <summary>Gets the substitute the selector was built for.</summary>
    public FontSubstitute Substitute { get; } = substitute;

    /// <summary>
    /// Gets the glyph id of a code; 0 when the substitute has no glyph for it, or draws it as several (see <paramref name="components"/>).
    /// </summary>
    /// <param name="code">The character code.</param>
    /// <param name="components">
    /// The glyphs the code draws one after the other when its name decomposes into several components, else <see langword="null"/>.
    /// Shared: do not change it.
    /// </param>
    public int GetGlyphId(byte code, out int[]? components)
    {
        if (!_glyphs.TryGet(code, out int glyph))
        {
            glyph = Select(code, out components);
            Volatile.Write(ref _components[code], components);
            _glyphs.Set(code, glyph);
            return glyph;
        }

        components = glyph == 0 ? Volatile.Read(ref _components[code]) : null;
        return glyph;
    }

    private int Select(byte code, out int[]? components)
    {
        components = null;
        if (Substitute.IsSymbolic && ByCode(code) is var byCode and > 0)
        {
            return byCode;
        }

        string name = Metrics.Names[code];
        int glyph = ByName(name);
        return glyph > 0 ? glyph : ByComponents(name, out components);
    }

    /// <summary>The Adobe Glyph List specification's decomposition: drop the suffix, split at underscores, expand multi-group <c>uni</c> names.</summary>
    private int ByComponents(string name, out int[]? components)
    {
        components = null;
        ReadOnlySpan<char> stem = name.AsSpan();
        int dot = stem.IndexOf('.');
        if (dot >= 0)
        {
            stem = stem[..dot];
        }

        if (stem.IsEmpty || (dot < 0 && !stem.Contains('_') && !IsMultipleUni(stem)))
        {
            return 0;
        }

        List<int> glyphs = [];
        foreach (Range range in stem.Split('_'))
        {
            ReadOnlySpan<char> component = stem[range];
            if (IsMultipleUni(component))
            {
                for (int group = 3; group < component.Length; group += 4)
                {
                    Add(glyphs, ByName(string.Concat("uni", component.Slice(group, 4))));
                }
            }
            else if (!component.IsEmpty)
            {
                Add(glyphs, ByName(component.ToString()));
            }
        }

        switch (glyphs.Count)
        {
            case 0:
                return 0;
            case 1:
                return glyphs[0];
            default:
                components = [.. glyphs];
                return 0;
        }

        static void Add(List<int> glyphs, int glyph)
        {
            if (glyph > 0)
            {
                glyphs.Add(glyph);
            }
        }
    }

    /// <summary>Whether a component is <c>uni</c> followed by more than one group of four characters (the digits are checked when each group is looked up).</summary>
    private static bool IsMultipleUni(ReadOnlySpan<char> component) =>
        component.Length > 7 && (component.Length - 3) % 4 == 0 && component.StartsWith("uni", StringComparison.Ordinal);

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
