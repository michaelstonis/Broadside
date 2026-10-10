using System.Runtime.CompilerServices;

namespace Broadside.Fonts;

/// <summary>
/// Glyph id to Unicode code point through a font program's Unicode "cmap" subtable: the last resort of Unicode mapping when the
/// methods of ISO 32000-2 §9.10.2 fail (PDFBox does the same for TrueType CIDFonts, PDFBOX-5324).
/// </summary>
/// <remarks>
/// Built once per program, on first use, by looking up every code point of the Basic Multilingual Plane (and of planes 1 and 2 for a
/// format 12 or 13 subtable) in the preferred subtable: (3, 10), (0, 4), (0, 6), (3, 1), then any other (0, x). A glyph reached by
/// several code points takes the smallest. Shared by every font and thread that uses the program; immutable once published.
/// </remarks>
internal static class ReverseCharacterMap
{
    private static readonly ConditionalWeakTable<FontProgram, int[]> Maps = [];

    /// <summary>Looks up the code point of a glyph.</summary>
    /// <param name="program">The font program.</param>
    /// <param name="glyphId">The glyph id; 0 (the missing glyph) never maps.</param>
    /// <param name="codePoint">The smallest code point mapped to the glyph.</param>
    /// <returns>Whether the program maps a code point to the glyph.</returns>
    public static bool TryGetCodePoint(FontProgram program, int glyphId, out int codePoint)
    {
        if (!Maps.TryGetValue(program, out int[]? map))
        {
            map = Maps.GetValue(program, Build);
        }

        codePoint = glyphId > 0 && glyphId < map.Length ? map[glyphId] : 0;
        return codePoint != 0;
    }

    private static int[] Build(FontProgram program)
    {
        FontCharacterMap? subtable = Select(program.CharacterMaps);
        if (subtable is null || program.GlyphCount <= 1)
        {
            return [];
        }

        var map = new int[program.GlyphCount];
        int last = subtable.Format is 12 or 13 ? 0x2FFFF : 0xFFFF;
        for (int codePoint = 1; codePoint <= last; codePoint++)
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

        return map;
    }

    private static FontCharacterMap? Select(IReadOnlyList<FontCharacterMap> subtables)
    {
        FontCharacterMap? best = null;
        int bestRank = int.MaxValue;
        foreach (FontCharacterMap subtable in subtables)
        {
            int rank = (subtable.PlatformId, subtable.EncodingId) switch
            {
                (3, 10) => 0,
                (0, 4) => 1,
                (0, 6) => 2,
                (3, 1) => 3,
                (0, _) => 4,
                _ => int.MaxValue,
            };
            if (rank < bestRank)
            {
                best = subtable;
                bestRank = rank;
            }
        }

        return best;
    }
}
