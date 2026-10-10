namespace Broadside.Fonts;

/// <summary>
/// The "cmap" subtables of one font program that PDF glyph selection uses, chosen once per program: the Unicode subtables in order
/// of preference, the (3, 0) symbol subtable, the (1, 0) Macintosh subtable, and the glyph-to-Unicode reverse map built from them on
/// first use. Shared by the embedded TrueType glyph selection, substitute glyph selection and Unicode mapping, so all three rank
/// subtables alike.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.5.4: the (3, 1) subtable maps Unicode values, (3, 0) maps the code with the high byte 0x00, 0xF0, 0xF1 or
/// 0xF2, (1, 0) maps Mac OS Roman codes. The other Unicode subtables stand in for (3, 1), after it: (3, 10), then the platform 0
/// subtables (0, 4), (0, 6), (0, 3), (0, 2), (0, 1), (0, 0). Format 14 variation sequences (0, 5) map nothing alone and are never
/// used.
/// </para>
/// <para>
/// The reverse map is the last resort of Unicode mapping when the methods of §9.10.2 fail (PDFBox does the same for TrueType CIDFonts,
/// PDFBOX-5324): every code point of the Basic Multilingual Plane is looked up in the first Unicode subtable, and those of planes 1 and
/// 2 in the first format 12 or 13 subtable; a glyph reached by several code points takes the smallest. Per program, never
/// process-wide (ADR 0009): it lives and dies with the program. Thread-safe: immutable once published.
/// </para>
/// </remarks>
internal sealed class CharacterMapSelection
{
    /// <summary>The high bytes of the (3, 0) code ranges, in the order §9.6.5.4 lists them. An array: a span literal of ints allocates in unoptimized code.</summary>
    private static readonly int[] SymbolHighBytes = [0x00, 0xF0, 0xF1, 0xF2];

    private readonly int _glyphCount;
    private int[]? _reverse;

    /// <summary>Initializes a new instance of the <see cref="CharacterMapSelection"/> class from a program's subtables.</summary>
    public CharacterMapSelection(IReadOnlyList<FontCharacterMap> maps, int glyphCount)
    {
        _glyphCount = glyphCount;
        Unicode = [.. maps.Where(map => UnicodeRank(map) >= 0).OrderBy(UnicodeRank)];
        Symbol = Find(maps, 3, 0);
        Macintosh = Find(maps, 1, 0);
    }

    /// <summary>Gets the Unicode subtables, most preferred first.</summary>
    public FontCharacterMap[] Unicode { get; }

    /// <summary>Gets the (3, 0) subtable, or <see langword="null"/>.</summary>
    public FontCharacterMap? Symbol { get; }

    /// <summary>Gets the (1, 0) subtable, or <see langword="null"/>.</summary>
    public FontCharacterMap? Macintosh { get; }

    /// <summary>Gets a value indicating whether the program has none of the subtables glyph selection uses.</summary>
    public bool IsEmpty => Unicode.Length == 0 && Symbol is null && Macintosh is null;

    /// <summary>Looks a Unicode value up in the Unicode subtables, in order of preference.</summary>
    /// <param name="scalar">The Unicode scalar value.</param>
    /// <param name="map">The subtable that maps it.</param>
    /// <returns>The glyph id; 0 when no Unicode subtable maps it.</returns>
    public int GetGlyphIdForUnicode(int scalar, out FontCharacterMap? map)
    {
        foreach (FontCharacterMap candidate in Unicode)
        {
            int glyph = candidate.GetGlyphId(scalar);
            if (glyph != 0)
            {
                map = candidate;
                return glyph;
            }
        }

        map = null;
        return 0;
    }

    /// <summary>Looks a one-byte code up in the (3, 0) subtable with the high bytes 0x00, 0xF0, 0xF1 and 0xF2, in that order (§9.6.5.4).</summary>
    /// <returns>The glyph id; 0 when there is no (3, 0) subtable or it maps none of the four.</returns>
    public int GetGlyphIdForSymbolCode(byte code)
    {
        if (Symbol is null)
        {
            return 0;
        }

        foreach (int high in SymbolHighBytes)
        {
            int glyph = Symbol.GetGlyphId((high << 8) | code);
            if (glyph != 0)
            {
                return glyph;
            }
        }

        return 0;
    }

    /// <summary>Looks a code up in the (1, 0) subtable.</summary>
    /// <returns>The glyph id; 0 when there is no (1, 0) subtable or it does not map the code.</returns>
    public int GetGlyphIdForMacintoshCode(int code) => Macintosh?.GetGlyphId(code) ?? 0;

    /// <summary>Looks a glyph name up in the (1, 0) subtable through its Mac OS Roman code (MacRomanEncoding plus Table 113, §9.6.5.4).</summary>
    /// <returns>The glyph id; 0 when there is no (1, 0) subtable, the name has no Mac OS Roman code, or the subtable does not map it.</returns>
    public int GetGlyphIdForMacOSRomanName(string name)
    {
        if (Macintosh is null)
        {
            return 0;
        }

        int index = GlyphNameTable.IndexOf(name);
        if (index < 0)
        {
            return 0;
        }

        int code = GlyphNameTable.Table(BuiltInEncoding.MacOSRoman).IndexOf((short)index);
        return code < 0 ? 0 : Macintosh.GetGlyphId(code);
    }

    /// <summary>Looks up the code point of a glyph through the reverse map.</summary>
    /// <param name="glyphId">The glyph id; 0 (the missing glyph) never maps.</param>
    /// <param name="codePoint">The smallest code point mapped to the glyph.</param>
    /// <returns>Whether the program maps a code point to the glyph.</returns>
    public bool TryGetCodePoint(int glyphId, out int codePoint)
    {
        int[] map = Volatile.Read(ref _reverse) ?? BuildReverse();
        codePoint = glyphId > 0 && glyphId < map.Length ? map[glyphId] : 0;
        return codePoint != 0;
    }

    private int[] BuildReverse()
    {
        int[] map = [];
        if (Unicode.Length > 0 && _glyphCount > 1)
        {
            map = new int[_glyphCount];
            Fill(map, Unicode[0], 1, 0xFFFF);
            FontCharacterMap? full = Array.Find(Unicode, subtable => subtable.Format is 12 or 13);
            if (full is not null)
            {
                Fill(map, full, 0x10000, 0x2FFFF);
            }
        }

        return Interlocked.CompareExchange(ref _reverse, map, null) ?? map;
    }

    private static void Fill(int[] map, FontCharacterMap subtable, int first, int last)
    {
        for (int codePoint = first; codePoint <= last; codePoint++)
        {
            if (codePoint == 0xD800)
            {
                codePoint = 0xE000;
            }

            int glyphId = subtable.GetGlyphId(codePoint);
            if (glyphId > 0 && glyphId < map.Length && map[glyphId] == 0)
            {
                map[glyphId] = codePoint;
            }
        }
    }

    /// <summary>The preference of a Unicode subtable (lower first), or −1 for a subtable that does not map Unicode values alone.</summary>
    private static int UnicodeRank(FontCharacterMap map) => (map.PlatformId, map.EncodingId) switch
    {
        (3, 1) => 0,
        (3, 10) => 1,
        (0, 4) => 2,
        (0, 6) => 3,
        (0, 3) => 4,
        (0, 2) => 5,
        (0, 1) => 6,
        (0, 0) => 7,
        _ => -1,
    };

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
