namespace Broadside.Fonts;

/// <summary>A subset font program (TrueType, OpenType or CFF) and the original id of each of its glyphs.</summary>
/// <param name="Program">The program's bytes.</param>
/// <param name="Glyphs">The original glyph id of each new glyph id, ascending; <c>Glyphs[0]</c> is 0 (<c>.notdef</c>).</param>
/// <remarks>ISO 32000-2 §9.9.2.</remarks>
internal sealed record SubsetProgram(byte[] Program, int[] Glyphs)
{
    /// <summary>The new glyph id of original glyph <paramref name="glyph"/>, or 0 when the subset lacks it.</summary>
    public int NewId(int glyph) => Math.Max(Array.BinarySearch(Glyphs, glyph), 0);
}
