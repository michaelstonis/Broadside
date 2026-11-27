using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Text;

/// <summary>
/// Pairs glyphs seen by two runs over the same content: an extracted <see cref="TextPage"/> (any order), a hit-test index (content
/// order) and the redaction editor all record one glyph per shown code with the same box (§9.4.4), so a glyph is found by its code
/// and the bottom-left corner of its box.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4.3 and §9.4.4. Compiled into the core and, as a linked source, into the viewer, which cannot see the core's
/// internals.
/// </remarks>
internal static class GlyphMatching
{
    /// <summary>The corner is compared on a 1/32-unit grid: the index keeps it in single precision, the extractor in double.</summary>
    private const double Grid = 32;

    /// <summary>For each glyph of <paramref name="text"/>, the index glyph it is (-1 for none); each index glyph is used once.</summary>
    public static int[] Match(HitTestIndex index, TextPage text)
    {
        int count = index.GlyphCount;
        var heads = new Dictionary<GlyphKey, int>(count);
        int[] next = new int[count];
        for (int glyph = count - 1; glyph >= 0; glyph--)
        {
            HitTestGlyph hit = index.GetGlyph(glyph);
            GlyphKey key = Key(hit.CharacterCode, hit.Quad.BottomLeft);
            next[glyph] = heads.TryGetValue(key, out int head) ? head : -1;
            heads[key] = glyph;
        }

        IReadOnlyList<TextGlyph> glyphs = text.Glyphs;
        int[] matches = new int[glyphs.Count];
        for (int i = 0; i < matches.Length; i++)
        {
            TextGlyph glyph = glyphs[i];
            GlyphKey key = Key(glyph.CharacterCode, glyph.Quad.BottomLeft);
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

    /// <summary>The identity of a glyph: its character code and the bottom-left corner of its box in page space, on the grid.</summary>
    /// <param name="code">The character code (§9.4.3).</param>
    /// <param name="corner">The box's bottom-left corner: its origin at the font's descent (§9.4.4).</param>
    /// <returns>The key.</returns>
    public static GlyphKey Key(uint code, PathPoint corner) =>
        new(code, (long)Math.Round((float)corner.X * Grid), (long)Math.Round((float)corner.Y * Grid));

    /// <summary>A glyph's identity across runs (<see cref="Key"/>).</summary>
    /// <param name="Code">The character code.</param>
    /// <param name="X">The corner's x on the grid.</param>
    /// <param name="Y">The corner's y on the grid.</param>
    internal readonly record struct GlyphKey(uint Code, long X, long Y);
}
