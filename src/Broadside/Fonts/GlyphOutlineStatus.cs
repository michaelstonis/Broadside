namespace Broadside.Fonts;

/// <summary>What <see cref="FontProgram.GetOutline"/> found for a glyph.</summary>
/// <remarks>ISO 32000-2 §9.9; the glyph description formats of the font program (OpenType "glyf", CFF charstrings, Type 1 charstrings).</remarks>
public enum GlyphOutlineStatus
{
    /// <summary>The glyph has an outline, now in the <see cref="GlyphOutline"/>.</summary>
    Complete,

    /// <summary>The glyph is valid and draws nothing, such as a space; the outline is empty.</summary>
    Empty,

    /// <summary>
    /// The glyph cannot be drawn: its glyph id is outside the program, or its data is damaged and was dropped with a diagnostic.
    /// The outline is empty.
    /// </summary>
    Invalid,
}
