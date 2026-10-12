using Broadside.Graphics;

namespace Broadside.Text;

/// <summary>One line of extracted text: consecutive words along one baseline, in one writing direction.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.4.4 (glyph positions) and §9.7.4.3 (vertical writing). Lines follow content order: a line ends where the next glyph
/// is written in another direction (the text space x axis turned by more than a few degrees, or horizontal and vertical writing
/// changing over), lies off the baseline by more than half the font size (a superscript or subscript stays on its line), or moves
/// back along the baseline by more than the font size. Reading order across lines and columns is not worked out here.
/// </para>
/// <para>Members are added as text extraction grows; existing members never change.</para>
/// </remarks>
public readonly record struct TextLine
{
    /// <summary>Gets the index of the line's first glyph (the first glyph of its first word) in the extracted glyphs.</summary>
    /// <remarks>ISO 32000-2 §9.4.3.</remarks>
    public int GlyphStart { get; internal init; }

    /// <summary>
    /// Gets the number of glyphs from the first glyph of the line's first word to the last glyph of its last word, the white-space
    /// glyphs between them included.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.4.3.</remarks>
    public int GlyphCount { get; internal init; }

    /// <summary>Gets the index of the line's first word in the extracted words.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.6.2.</remarks>
    public int WordStart { get; internal init; }

    /// <summary>Gets the number of words of the line; at least one.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.6.2.</remarks>
    public int WordCount { get; internal init; }

    /// <summary>Gets the index of the line's first UTF-16 unit in the plain text (<see cref="TextPage.PlainText"/>).</summary>
    /// <remarks>ISO 32000-2 §9.10.</remarks>
    public int PlainTextStart { get; internal init; }

    /// <summary>Gets the number of UTF-16 units of the line in the plain text: its words separated by single spaces.</summary>
    /// <remarks>ISO 32000-2 §9.10.</remarks>
    public int PlainTextLength { get; internal init; }

    /// <summary>
    /// Gets the line's box in page space: the smallest box holding the quads of its words' glyphs, with its bottom edge along the
    /// first glyph's baseline, so the box turns with the text. For vertical writing the box is a column whose bottom edge lies
    /// across it, as for <see cref="TextGlyph.Quad"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.4.4.</remarks>
    public Quad Quad { get; internal init; }

    /// <summary>Gets a value indicating whether the line is written vertically, from top to bottom.</summary>
    /// <remarks>ISO 32000-2 §9.7.4.3.</remarks>
    public bool IsVertical { get; internal init; }
}
