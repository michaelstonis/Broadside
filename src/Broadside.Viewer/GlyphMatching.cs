using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Text;

namespace Broadside.Viewer;

/// <summary>
/// Pairs the glyphs of an extracted <see cref="TextPage"/> (any order) with those of a hit-test index (content order): both record
/// one glyph per shown code with the same box (§9.4.4), so a glyph is found by its code and the bottom-left corner of its box.
/// </summary>
internal static class GlyphMatching
{
    /// <summary>The corner is compared on a 1/32-unit grid: the index keeps it in single precision, the extractor in double.</summary>
    private const double Grid = 32;

    /// <summary>For each glyph of <paramref name="text"/>, the index glyph it is (-1 for none); each index glyph is used once.</summary>
    public static int[] Match(HitTestIndex index, TextPage text)
    {
        int count = index.GlyphCount;
        var heads = new Dictionary<(uint Code, long X, long Y), int>(count);
        int[] next = new int[count];
        for (int glyph = count - 1; glyph >= 0; glyph--)
        {
            HitTestGlyph hit = index.GetGlyph(glyph);
            var key = Key(hit.CharacterCode, hit.Quad.BottomLeft);
            next[glyph] = heads.TryGetValue(key, out int head) ? head : -1;
            heads[key] = glyph;
        }

        IReadOnlyList<TextGlyph> glyphs = text.Glyphs;
        int[] matches = new int[glyphs.Count];
        for (int i = 0; i < matches.Length; i++)
        {
            TextGlyph glyph = glyphs[i];
            var key = Key(glyph.CharacterCode, glyph.Quad.BottomLeft);
            if (heads.TryGetValue(key, out int head))
            {
                matches[i] = head;
                if (next[head] >= 0)
                {
                    heads[key] = next[head];
                }
                else
                {
                    heads.Remove(key);
                }
            }
            else
            {
                matches[i] = -1;
            }
        }

        return matches;
    }

    private static (uint Code, long X, long Y) Key(uint code, PathPoint corner) =>
        (code, (long)Math.Round((float)corner.X * Grid), (long)Math.Round((float)corner.Y * Grid));
}
