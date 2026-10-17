using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Rendering.WebGL;

/// <summary>
/// Flattened glyph outlines in glyph space, per font program, glyph and size class (a power of two of device pixels per glyph unit),
/// so text is flattened once and later tiles and pages only transform the points. Bounded: cleared when it holds too many points.
/// </summary>
/// <remarks>ISO 32000-2 §9.2.4 (glyph outlines, filled by the non-zero rule), §9.4.4 (text rendering matrix).</remarks>
internal static class GlyphCache
{
    private const int MaxPoints = 1 << 21;

    private static readonly Dictionary<Key, float[]> Entries = [];
    private static int _points;

    public static float[] Get(FontProgram program, int glyph, PathView outline, in Matrix toDevice, Tessellator tessellator)
    {
        double scale = Math.Max(
            Math.Sqrt((toDevice.A * toDevice.A) + (toDevice.B * toDevice.B)),
            Math.Sqrt((toDevice.C * toDevice.C) + (toDevice.D * toDevice.D)));
        int level = scale > 0 && double.IsFinite(scale) ? Math.Clamp((int)Math.Ceiling(Math.Log2(scale)), -40, 40) : 0;
        var key = new Key(program, glyph, level);
        if (Entries.TryGetValue(key, out float[]? saved))
        {
            return saved;
        }

        // Flattened to 0.2 device pixel at the largest scale of the class.
        tessellator.Flatten(outline, Matrix.Identity, 0.2 / Math.Pow(2, level));
        saved = tessellator.SaveContours();
        if (_points + saved.Length > MaxPoints)
        {
            Entries.Clear();
            _points = 0;
        }

        Entries[key] = saved;
        _points += saved.Length;
        return saved;
    }

    private readonly record struct Key(FontProgram Program, int Glyph, int Level);
}
