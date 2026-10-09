namespace Broadside.Fonts;

/// <summary>The horizontal metrics a font program gives one glyph, in its glyph space units.</summary>
/// <remarks>
/// OpenType "hmtx" table. PDF widths (<c>Widths</c>, <c>W</c>) take precedence over these for positioning text (ISO 32000-2
/// §9.2.4, §9.6.2.1); a program's advance is the fallback when the font dictionary gives none.
/// </remarks>
/// <param name="AdvanceWidth">The distance from the glyph's origin to the next glyph's origin.</param>
/// <param name="LeftSideBearing">The distance from the glyph's origin to the left edge of its outline.</param>
public readonly record struct GlyphMetrics(double AdvanceWidth, double LeftSideBearing);
