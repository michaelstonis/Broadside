using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// One glyph shown by a text-showing operator, with everything needed to place it: the single source of glyph positions for
/// rendering, extraction and hit testing. Valid only during the callback that received it.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.4.3 (Table 107) and §9.4.4. The glyph is drawn in text space mapped by <see cref="TextMatrix"/> and then
/// <see cref="Ctm"/>: <c>T_rm = [T_fs·T_h 0 0 T_fs 0 T_rise] × T_m × CTM</c>.
/// </para>
/// <para>
/// Declared with the content interpreter's processor surface (issue #55); text showing reports it from issue #56, which adds the
/// resolved font. Members are added, never changed.
/// </para>
/// </remarks>
public readonly ref struct GlyphEvent
{
    /// <summary>Gets the font dictionary named by <c>Tf</c>.</summary>
    /// <remarks>ISO 32000-2 §9.5, Table 109.</remarks>
    public CosDictionary? FontDictionary { get; internal init; }

    /// <summary>
    /// Gets the font the glyph is shown in: the one <c>Tf</c> (or the <c>Font</c> entry of a graphics state parameter dictionary)
    /// selected; Helvetica's Standard 14 metrics when text is shown before any font is selected or the font is missing.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.3.1 and §9.5. Glyph selection and Unicode mapping are the font's, on demand.</remarks>
    public PdfFont? Font { get; internal init; }

    /// <summary>Gets the character code (§9.4.3): one byte for a simple font, one to four for a composite font's CMap.</summary>
    public uint CharacterCode { get; internal init; }

    /// <summary>Gets the bytes of the string the code was read from.</summary>
    public ReadOnlySpan<byte> SourceBytes { get; internal init; }

    /// <summary>Gets the offset of the code's first byte in <see cref="SourceBytes"/>.</summary>
    public int SourceIndex { get; internal init; }

    /// <summary>Gets the number of bytes the code takes.</summary>
    public int CodeLength { get; internal init; }

    /// <summary>Gets the matrix from text space to user space for this glyph: <c>[T_fs·T_h 0 0 T_fs 0 T_rise] × T_m</c>.</summary>
    public Matrix TextMatrix { get; internal init; }

    /// <summary>Gets the CTM.</summary>
    public Matrix Ctm { get; internal init; }

    /// <summary>Gets the glyph's horizontal displacement w0, in text space units (§9.2.4).</summary>
    public double HorizontalDisplacement { get; internal init; }

    /// <summary>Gets the glyph's vertical displacement w1, in text space units (§9.2.4).</summary>
    public double VerticalDisplacement { get; internal init; }

    /// <summary>Gets the horizontal advance t_x applied to the text matrix after the glyph, spacing included (§9.4.4).</summary>
    public double AdvanceX { get; internal init; }

    /// <summary>Gets the vertical advance t_y applied to the text matrix after the glyph, spacing included (§9.4.4).</summary>
    public double AdvanceY { get; internal init; }

    /// <summary>Gets the <c>TJ</c> number that came right before the glyph, in thousandths of text space units; 0 when none (§9.4.3).</summary>
    public double Adjustment { get; internal init; }

    /// <summary>Gets a value indicating whether word spacing was applied (a single-byte code 32, §9.3.3).</summary>
    public bool WordSpacingApplied { get; internal init; }

    /// <summary>Gets a value indicating whether the font writes vertically (§9.7.4.3).</summary>
    public bool IsVertical { get; internal init; }

    /// <summary>Gets the text rendering mode (§9.3.6).</summary>
    public TextRenderingMode RenderingMode { get; internal init; }

    /// <summary>Gets a value indicating whether optional content hides the glyph (§8.11.3.1).</summary>
    public bool IsHidden { get; internal init; }
}
