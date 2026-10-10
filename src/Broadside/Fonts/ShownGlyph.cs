using Broadside.Graphics;

namespace Broadside.Fonts;

/// <summary>
/// One character code of a shown string and what the content interpreter needs to place it: the code, its length, the glyph's
/// displacements in text space units (before the font size) and, in vertical writing, its position vector.
/// </summary>
/// <param name="Code">The character code.</param>
/// <param name="Length">The number of bytes the code takes, at least one.</param>
/// <param name="HorizontalDisplacement">w0 (§9.2.4).</param>
/// <param name="VerticalDisplacement">w1, 0 in horizontal writing (§9.2.4, §9.7.4.3).</param>
/// <param name="PositionVector">v, from the glyph's horizontal origin to its vertical origin; zero in horizontal writing (§9.7.4.3).</param>
/// <param name="AppliesWordSpacing">Whether word spacing applies: a single-byte code 32 (§9.3.3).</param>
internal readonly record struct ShownGlyph(uint Code, int Length, double HorizontalDisplacement, double VerticalDisplacement, PathPoint PositionVector, bool AppliesWordSpacing);
