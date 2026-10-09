namespace Broadside.Fonts;

/// <summary>A glyph's metrics for vertical writing, in glyph space units (thousandths of text space).</summary>
/// <param name="VerticalAdvance">The vertical component of the displacement vector w1 (its horizontal component is 0); usually negative.</param>
/// <param name="PositionX">The horizontal component of the position vector v, from the horizontal origin to the vertical origin.</param>
/// <param name="PositionY">The vertical component of the position vector v.</param>
/// <remarks>
/// ISO 32000-2 §9.2.4 (Figure 55) and §9.7.4.3: from the CIDFont's <c>W2</c>, else <c>DW2</c> (default <c>[880 −1000]</c>) with
/// v.x half the glyph's width. In vertical writing the glyph is painted with its horizontal origin at the current point minus v
/// (scaled by the font size), and the pen moves by w1.
/// </remarks>
public readonly record struct CidVerticalMetrics(double VerticalAdvance, double PositionX, double PositionY);
