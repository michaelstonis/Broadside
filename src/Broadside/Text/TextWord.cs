using Broadside.Graphics;

namespace Broadside.Text;

/// <summary>
/// One word of extracted text: consecutive glyphs on one line with no gap, white-space glyph or line change between them.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.2.6.2 (identifying word breaks) and §9.4.4 (glyph positions). A word ends at a white-space glyph, at a gap along
/// the baseline wider than a tenth of the font size (from a <c>TJ</c> number or a text positioning operator; character and word
/// spacing are part of the advance and never break a word), and where its line ends. A negative gap (kerning) never breaks a word.
/// Glyphs of consecutive <c>ActualText</c> sequences are not separated by a word break (§14.9.4). White-space glyphs belong to no word.
/// </para>
/// <para>Members are added as text extraction grows; existing members never change.</para>
/// </remarks>
public readonly record struct TextWord
{
    /// <summary>Gets the index of the word's first glyph in the extracted glyphs.</summary>
    /// <remarks>ISO 32000-2 §9.4.3.</remarks>
    public int GlyphStart { get; internal init; }

    /// <summary>Gets the number of glyphs of the word, glyphs mapped to no text included.</summary>
    /// <remarks>ISO 32000-2 §9.4.3.</remarks>
    public int GlyphCount { get; internal init; }

    /// <summary>Gets the index of the word's first UTF-16 unit in the extracted text (<see cref="TextPage.Text"/>).</summary>
    /// <remarks>ISO 32000-2 §9.10.</remarks>
    public int TextStart { get; internal init; }

    /// <summary>Gets the number of UTF-16 units of the word: the texts of its glyphs, one after the other.</summary>
    /// <remarks>ISO 32000-2 §9.10 and §14.9.4.</remarks>
    public int TextLength { get; internal init; }

    /// <summary>
    /// Gets the word's box in page space: the smallest box holding the quads of its glyphs, with its bottom edge along the first
    /// glyph's baseline (for vertical writing, across the column, as for <see cref="TextGlyph.Quad"/>).
    /// </summary>
    /// <remarks>ISO 32000-2 §9.4.4.</remarks>
    public Quad Quad { get; internal init; }
}
