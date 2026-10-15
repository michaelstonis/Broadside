using Broadside.Graphics;

namespace Broadside.Fonts;

/// <summary>
/// Text shaped in a canvas font: the glyphs it shows, in visual order (left to right as drawn), each with the text it came from and
/// its advance. Get one with <see cref="PdfCanvasFont.Shape"/> and show it with <see cref="PdfCanvas.ShowText(ShapedText)"/>.
/// </summary>
/// <remarks>
/// OpenType specification, GSUB (substitution) and the positioning step; ISO 32000-2 §9.4.3 (<c>TJ</c>) and §9.10.2 (mapping
/// glyphs back to text: <c>ToUnicode</c>, and <c>ActualText</c>, §14.9.4, where a glyph stands for text <c>ToUnicode</c> cannot
/// give it).
/// </remarks>
public sealed class ShapedText
{
    private readonly ShapedGlyph[] _glyphs;

    internal ShapedText(PdfCanvasFont font, string text, bool rightToLeft, CanvasGlyph[] canvasGlyphs, ShapedGlyph[] glyphs)
    {
        Font = font;
        Text = text;
        IsRightToLeft = rightToLeft;
        CanvasGlyphs = canvasGlyphs;
        _glyphs = glyphs;
    }

    /// <summary>Gets the font the text was shaped in; the canvas shows it only in that font.</summary>
    public PdfCanvasFont Font { get; }

    /// <summary>Gets the text that was shaped, in logical order.</summary>
    public string Text { get; }

    /// <summary>Gets a value indicating whether the run is right to left: its glyphs are in the reverse of the text's order.</summary>
    public bool IsRightToLeft { get; }

    /// <summary>Gets the glyphs in visual order.</summary>
    public ReadOnlySpan<ShapedGlyph> Glyphs => _glyphs;

    /// <summary>Gets the canvas glyphs, in the order of <see cref="Glyphs"/>.</summary>
    internal CanvasGlyph[] CanvasGlyphs { get; }

    /// <summary>Measures the advance of the shaped text at <paramref name="fontSize"/>.</summary>
    /// <param name="fontSize">The font size (<c>Tf</c>'s size operand).</param>
    /// <returns>The sum of the glyph advances in unscaled text space units, without character spacing, word spacing or horizontal scaling.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontSize"/> is not finite.</exception>
    /// <remarks>ISO 32000-2 §9.4.4 with <c>Tc</c> = <c>Tw</c> = 0 and <c>Th</c> = 1.</remarks>
    public double MeasureWidth(double fontSize)
    {
        if (!double.IsFinite(fontSize))
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "The font size shall be a finite number.");
        }

        double units = 0;
        foreach (ShapedGlyph glyph in _glyphs)
        {
            units += glyph.Advance;
        }

        return units * fontSize / 1000;
    }
}

/// <summary>One glyph of a <see cref="ShapedText"/>.</summary>
/// <param name="Code">
/// The character code the canvas writes for the glyph: for an embedded font (<c>Identity-H</c>) its glyph id in the font program, which
/// is also its CID; for a Standard 14 font, its one-byte code.
/// </param>
/// <param name="Cluster">The UTF-16 index in <see cref="ShapedText.Text"/> of the first character the glyph shows (a ligature shows several, from here).</param>
/// <param name="Advance">The glyph's advance after positioning, in thousandths of the font size (its width plus kerning).</param>
/// <remarks>OpenType specification, GSUB; ISO 32000-2 §9.2.4 (glyph widths in thousandths of text space).</remarks>
public readonly record struct ShapedGlyph(int Code, int Cluster, double Advance);
