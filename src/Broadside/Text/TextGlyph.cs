using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Text;

/// <summary>
/// One glyph extracted from a page: the Unicode text it stands for, where it lies on the page, and the font, size and rendering
/// mode it was shown with.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.4.4 and §9.10. Positions are in page space: default user space (§8.3.2.3), before the page's crop box, rotation
/// and user unit are applied, the space every content stream of the page is drawn in. The glyph's text is the range
/// <see cref="TextStart"/>, <see cref="TextLength"/> of the extracted text (<see cref="TextPage.GetGlyphText"/>), so a page's glyphs
/// share one string.
/// </para>
/// <para>Members are added as text extraction grows; existing members never change.</para>
/// </remarks>
public readonly record struct TextGlyph
{
    /// <summary>
    /// Gets the glyph's box in page space: from its origin to its horizontal displacement w0 along the baseline, and from the font's
    /// descent to its ascent across it, mapped by the text rendering matrix and the CTM.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §9.4.4 (<c>T_rm × CTM</c>), §9.2.4 (w0) and §9.8.1 (Table 120, <c>Ascent</c> and <c>Descent</c>; <c>FontBBox</c>
    /// when they are missing; for a Type 3 font the <c>FontBBox</c> through the <c>FontMatrix</c>, §9.6.4). A font with neither uses
    /// 0.8 above and 0.2 below the baseline, as other processors do. The box is the font's, not the glyph's outline.
    /// </remarks>
    public Quad Quad { get; internal init; }

    /// <summary>Gets the glyph's origin in page space: where text space's origin lands (§9.4.4); for vertical writing, its horizontal origin (§9.7.4.3).</summary>
    /// <remarks>ISO 32000-2 §9.4.4.</remarks>
    public PathPoint Origin { get; internal init; }

    /// <summary>
    /// Gets the glyph's advance in page space, character and word spacing included: the next glyph's origin is this one's plus the
    /// advance, unless a <c>TJ</c> number or a text positioning operator comes between.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.4.4.</remarks>
    public PathPoint Advance { get; internal init; }

    /// <summary>Gets the font the glyph was shown in; Helvetica's Standard 14 metrics when text was shown without a font.</summary>
    /// <remarks>ISO 32000-2 §9.5.</remarks>
    public PdfFont Font { get; internal init; }

    /// <summary>Gets the text font size <c>T_fs</c> set by <c>Tf</c>, in text space units; the size on the page also depends on the text matrix and the CTM (see <see cref="Quad"/>).</summary>
    /// <remarks>ISO 32000-2 §9.3.1, Table 103.</remarks>
    public double FontSize { get; internal init; }

    /// <summary>Gets the text rendering mode the glyph was shown in.</summary>
    /// <remarks>ISO 32000-2 §9.3.6, Table 104.</remarks>
    public TextRenderingMode RenderingMode { get; internal init; }

    /// <summary>Gets the character code the glyph was shown with: one byte for a simple font, one to four for a composite font.</summary>
    /// <remarks>ISO 32000-2 §9.4.3.</remarks>
    public uint CharacterCode { get; internal init; }

    /// <summary>Gets the number of bytes the character code takes.</summary>
    /// <remarks>ISO 32000-2 §9.4.3 and §9.7.6.2.</remarks>
    public int CodeLength { get; internal init; }

    /// <summary>Gets which method of ISO 32000-2 §9.10.2 mapped the character code to Unicode; <see cref="UnicodeSource.Unmapped"/> when its text is U+FFFD.</summary>
    /// <remarks>ISO 32000-2 §9.10.2. Describes the font's mapping even when <c>ActualText</c> replaced the text (§14.9.4).</remarks>
    public UnicodeSource UnicodeSource { get; internal init; }

    /// <summary>Gets the index of the glyph's first UTF-16 unit in the extracted text.</summary>
    /// <remarks>ISO 32000-2 §9.10.</remarks>
    public int TextStart { get; internal init; }

    /// <summary>
    /// Gets the number of UTF-16 units of the glyph's text: usually one, two for a surrogate pair, more for a ligature, 0 for a code
    /// mapped to no text or for a glyph whose text an <c>ActualText</c> replacement on the sequence's first glyph stands for.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10.2 and §14.9.4.</remarks>
    public int TextLength { get; internal init; }

    /// <summary>Gets the <c>TJ</c> number that came right before the glyph, in thousandths of text space units; 0 when none.</summary>
    /// <remarks>ISO 32000-2 §9.4.3, Table 107 (<c>TJ</c>).</remarks>
    public double Adjustment { get; internal init; }

    /// <summary>Gets a value indicating whether the font writes vertically.</summary>
    /// <remarks>ISO 32000-2 §9.7.4.3.</remarks>
    public bool IsVertical { get; internal init; }

    /// <summary>Gets what sets the glyph apart: invisible, hidden, an artifact, replaced by <c>ActualText</c>.</summary>
    /// <remarks>ISO 32000-2 §9.3.6, §8.11.3.1, §14.8.2.2 and §14.9.4.</remarks>
    public TextGlyphFlags Flags { get; internal init; }
}
