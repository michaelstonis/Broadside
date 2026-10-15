using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// One character code shown on a page as a <see cref="HitTestIndex"/> holds it: its box in page space, the Unicode text it stands for,
/// the code and the rendering mode it was shown in.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.4.4 (the text rendering matrix and the CTM), §9.2.4 (w0), §9.8.1 (Table 120, Ascent and Descent) and §9.10.2
/// (Unicode). Page space is default user space (§8.3.2.3), before the crop box, rotation and user unit; a viewer maps a device point
/// into it through the inverse of its device transform. The box is the same as <see cref="Text.TextGlyph.Quad"/>: from the origin to
/// w0 along the baseline and from the font's descent to its ascent across it, rotated, skewed or reflected with the text.
/// </para>
/// <para>
/// Each shown code is one glyph, in content order (the order the content shows them, §9.4.3), whatever font it is in (Type 3
/// included) and whatever its rendering mode (invisible text, mode 3, included); a code a substitute draws as several glyphs is
/// still one. Members are added, never changed.
/// </para>
/// </remarks>
public readonly record struct HitTestGlyph
{
    /// <summary>Gets the glyph's box in page space; corners named as in <see cref="Graphics.Quad"/> (bottom on the baseline side, left at the origin).</summary>
    /// <remarks>ISO 32000-2 §9.4.4 and §9.8.1. Stored in single precision.</remarks>
    public Quad Quad { get; internal init; }

    /// <summary>Gets where the glyph's text starts in <see cref="HitTestIndex.Text"/>.</summary>
    /// <remarks>ISO 32000-2 §9.10.2.</remarks>
    public int TextStart { get; internal init; }

    /// <summary>Gets how many UTF-16 units the glyph's text takes: 0 for a code mapped to no text, more than one for a ligature or a surrogate pair.</summary>
    /// <remarks>ISO 32000-2 §9.10.2.</remarks>
    public int TextLength { get; internal init; }

    /// <summary>Gets the character code (§9.4.3): one byte for a simple font, one to four for a composite font's CMap.</summary>
    /// <remarks>ISO 32000-2 §9.4.3 and §9.7.6.</remarks>
    public uint CharacterCode { get; internal init; }

    /// <summary>Gets the number of bytes the code takes.</summary>
    /// <remarks>ISO 32000-2 §9.7.6.2.</remarks>
    public int CodeLength { get; internal init; }

    /// <summary>Gets the text rendering mode the code was shown in; <see cref="TextRenderingMode.Invisible"/> text paints nothing but is selectable.</summary>
    /// <remarks>ISO 32000-2 §9.3.6, Table 104.</remarks>
    public TextRenderingMode RenderingMode { get; internal init; }

    /// <summary>Gets a value indicating whether the font writes vertically.</summary>
    /// <remarks>ISO 32000-2 §9.7.4.3.</remarks>
    public bool IsVertical { get; internal init; }
}
