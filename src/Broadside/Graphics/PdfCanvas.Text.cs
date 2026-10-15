using System.Text;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>Text objects, text state, positioning and showing (issue #149).</summary>
public sealed partial class PdfCanvas
{
    private readonly Stack<FontState> _savedFonts = new();
    private FontState _font;
    private bool _inText;

    /// <summary>Gets a value indicating whether a text object is open (between <see cref="BeginText"/> and <see cref="EndText"/>).</summary>
    public bool IsInTextObject => _inText;

    /// <summary>Gets the font set with <see cref="SetFont"/>, or <see langword="null"/> before any (text state, saved and restored with the graphics state).</summary>
    public PdfCanvasFont? Font => _font.Font;

    /// <summary>Gets the font size set with <see cref="SetFont"/>, or 0 before any.</summary>
    public double FontSize => _font.Size;

    /// <summary>Begins a text object (<c>BT</c>): the text matrix and the text line matrix start as the identity.</summary>
    /// <exception cref="InvalidOperationException">A path is being built, a text object is already open, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.4.1, Table 105, and §8.2, Figure 9: text objects do not nest; end it with <see cref="EndText"/>.</remarks>
    public void BeginText()
    {
        ThrowIfNotAtPageLevel();
        _writer.Operator("BT");
        _inText = true;
    }

    /// <summary>Ends the text object (<c>ET</c>); glyphs shown in a clipping rendering mode then narrow the clipping path.</summary>
    /// <exception cref="InvalidOperationException">No text object is open, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.4.1, Table 105, and §9.3.6 (text rendering modes 4 to 7 clip at the end of the text object).</remarks>
    public void EndText()
    {
        ThrowIfNotInTextObject();
        _writer.Operator("ET");
        _inText = false;
    }

    /// <summary>Sets the font and the font size (<c>Tf</c>).</summary>
    /// <param name="font">A font of this document (<see cref="PdfDocument.GetStandard14Font"/>).</param>
    /// <param name="size">The font size: the scale from glyph space (thousandths) to text space units.</param>
    /// <exception cref="ArgumentException"><paramref name="font"/> belongs to another document.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §9.3.1, Table 103 (<c>Tf</c> and <c>Tfs</c>), and §9.2.2. Allowed outside and inside a text object; part of the
    /// graphics state, so <see cref="RestoreState"/> restores the previous font. The font is named in the <c>Font</c> resources.
    /// </remarks>
    public void SetFont(PdfCanvasFont font, double size)
    {
        ArgumentNullException.ThrowIfNull(font);
        ThrowIfPathOpen();
        if (!ReferenceEquals(font.Document, _document))
        {
            throw new ArgumentException("The font belongs to another document.", nameof(font));
        }

        if (!double.IsFinite(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The font size shall be a finite number.");
        }

        _writer.Name(_resources.Name(CanvasResources.Font, "F", font.Reference)).Number(size).Operator("Tf");
        _font = new FontState(font, size);
    }

    /// <summary>Sets the character spacing (<c>Tc</c>), added to every glyph's displacement.</summary>
    /// <param name="spacing">The spacing in unscaled text space units (scaled by the horizontal scaling); 0 by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="spacing"/> is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.3.2 and Table 103.</remarks>
    public void SetCharacterSpacing(double spacing) => SetTextState(spacing, nameof(spacing), "Tc");

    /// <summary>Sets the word spacing (<c>Tw</c>), added to the displacement of every single-byte code 32.</summary>
    /// <param name="spacing">The spacing in unscaled text space units; 0 by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="spacing"/> is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.3.3 and Table 103. Applies only to fonts whose space is the one-byte code 32 (<see cref="PdfCanvasFont.AppliesWordSpacing"/>).</remarks>
    public void SetWordSpacing(double spacing) => SetTextState(spacing, nameof(spacing), "Tw");

    /// <summary>Sets the horizontal scaling (<c>Tz</c>), which stretches glyphs and spacing horizontally.</summary>
    /// <param name="percent">The scaling in percent of the normal width; 100 by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percent"/> is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.3.4 and Table 103.</remarks>
    public void SetHorizontalScaling(double percent) => SetTextState(percent, nameof(percent), "Tz");

    /// <summary>Sets the text leading (<c>TL</c>), the distance <see cref="MoveToNextLine"/> moves down.</summary>
    /// <param name="leading">The leading in unscaled text space units; 0 by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leading"/> is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.3.5 and Table 103.</remarks>
    public void SetTextLeading(double leading) => SetTextState(leading, nameof(leading), "TL");

    /// <summary>Sets the text rise (<c>Ts</c>), which moves the baseline up (superscript) or down (subscript).</summary>
    /// <param name="rise">The rise in unscaled text space units; 0 by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rise"/> is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.3.7 and Table 103.</remarks>
    public void SetTextRise(double rise) => SetTextState(rise, nameof(rise), "Ts");

    /// <summary>Sets the text rendering mode (<c>Tr</c>): fill, stroke, invisible, or add the glyphs to the clipping path.</summary>
    /// <param name="mode">The mode.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined mode.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.3.6, Table 104: in the clipping modes the glyphs of the text object narrow the clip at <see cref="EndText"/>.</remarks>
    public void SetTextRenderingMode(TextRenderingMode mode)
    {
        ThrowIfPathOpen();
        ThrowIfUndefined(mode);
        _writer.Number((int)mode).Operator("Tr");
    }

    /// <summary>Moves to the start of the next line, offset from the start of the current one by (<paramref name="x"/>, <paramref name="y"/>) (<c>Td</c>).</summary>
    /// <param name="x">The horizontal offset, in unscaled text space units.</param>
    /// <param name="y">The vertical offset, in unscaled text space units.</param>
    /// <exception cref="ArgumentException">An offset is not finite.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.4.2, Table 106: <c>Tm = Tlm = [1 0 0 1 x y] × Tlm</c>.</remarks>
    public void MoveText(double x, double y)
    {
        ThrowIfNotInTextObject();
        ThrowIfNotFinite(x, y);
        _writer.Number(x).Number(y).Operator("Td");
    }

    /// <summary>As <see cref="MoveText"/>, and sets the leading to −<paramref name="y"/> (<c>TD</c>).</summary>
    /// <param name="x">The horizontal offset, in unscaled text space units.</param>
    /// <param name="y">The vertical offset, in unscaled text space units.</param>
    /// <exception cref="ArgumentException">An offset is not finite.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.4.2, Table 106: <c>−y TL x y Td</c>.</remarks>
    public void MoveTextAndSetLeading(double x, double y)
    {
        ThrowIfNotInTextObject();
        ThrowIfNotFinite(x, y);
        _writer.Number(x).Number(y).Operator("TD");
    }

    /// <summary>Sets the text matrix and the text line matrix (<c>Tm</c>).</summary>
    /// <param name="matrix">The matrix, from text space to user space; it replaces the current one (it is not concatenated).</param>
    /// <exception cref="ArgumentException">A component is not finite.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.4.2, Table 106.</remarks>
    public void SetTextMatrix(Matrix matrix)
    {
        ThrowIfNotInTextObject();
        ThrowIfNotFinite(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);
        _writer.Number(matrix.A).Number(matrix.B).Number(matrix.C).Number(matrix.D).Number(matrix.E).Number(matrix.F).Operator("Tm");
    }

    /// <summary>Moves to the start of the next line, the leading below the current one (<c>T*</c>).</summary>
    /// <exception cref="InvalidOperationException">No text object is open, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.4.2, Table 106: <c>0 −TL Td</c>.</remarks>
    public void MoveToNextLine()
    {
        ThrowIfNotInTextObject();
        _writer.Operator("T*");
    }

    /// <summary>Shows text in the current font at the current text position, without kerning (<c>Tj</c>).</summary>
    /// <param name="text">The text; every character shall have a code in the font (<see cref="PdfCanvasFont.CanEncode"/>).</param>
    /// <exception cref="ArgumentException">A character of <paramref name="text"/> has no code in the font; the message names it and the alternative (an embedded font). Nothing is written.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, no font is set, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.4.3, Table 107 (<c>Tj</c>). As <see cref="ShowText(ReadOnlySpan{char}, bool)"/> without kerning.</remarks>
    public void ShowText(ReadOnlySpan<char> text) => ShowText(text, kerning: false);

    /// <summary>Shows text in the current font at the current text position (<c>Tj</c>, or <c>TJ</c> when it is kerned).</summary>
    /// <param name="text">The text; every character shall have a code in the font (<see cref="PdfCanvasFont.CanEncode"/>).</param>
    /// <param name="kerning">Whether to apply the font's kerning between neighbouring glyphs, written as <c>TJ</c> adjustments (<see cref="PdfCanvasFont.MeasureWidth"/> measures the same advance).</param>
    /// <exception cref="ArgumentException">A character of <paramref name="text"/> has no code in the font; the message names it and the alternative (an embedded font). Nothing is written.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, no font is set, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §9.4.3, Table 107 (<c>Tj</c>, <c>TJ</c>), and §9.4.4: each glyph advances the text matrix by its width at the font
    /// size plus the character spacing (and the word spacing for code 32), times the horizontal scaling. A kerning amount <c>k</c>
    /// (thousandths of the font size) is written as the <c>TJ</c> number <c>−k</c>.
    /// </remarks>
    public void ShowText(ReadOnlySpan<char> text, bool kerning)
    {
        PdfCanvasFont font = ThrowIfCannotShow();
        List<(CanvasGlyph Glyph, double Adjustment)> glyphs = Encode(font, text, nameof(text));
        if (kerning)
        {
            for (int index = 0; index + 1 < glyphs.Count; index++)
            {
                glyphs[index] = glyphs[index] with { Adjustment = -font.GetKerning(glyphs[index].Glyph, glyphs[index + 1].Glyph) };
            }
        }

        WriteShown(font, glyphs, "Tj");
    }

    /// <summary>Shows text with an explicit adjustment after each character (<c>TJ</c>): positive moves the next glyph left (closer), negative right.</summary>
    /// <param name="text">The text; every character shall have a code in the font.</param>
    /// <param name="adjustments">One number per Unicode scalar value of <paramref name="text"/>, in thousandths of the font size, subtracted from the position after that character's glyph; 0 for none.</param>
    /// <exception cref="ArgumentException">A character has no code in the font (the message names it), there is not one adjustment per character, or an adjustment is not finite. Nothing is written.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, no font is set, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.4.3, Table 107 (<c>TJ</c>), and §9.4.4: <c>tx = (w0 − Tj/1000) × Tfs + Tc + Tw</c>, times the horizontal scaling.</remarks>
    public void ShowText(ReadOnlySpan<char> text, ReadOnlySpan<double> adjustments)
    {
        PdfCanvasFont font = ThrowIfCannotShow();
        List<(CanvasGlyph Glyph, double Adjustment)> glyphs = Encode(font, text, nameof(text));
        if (adjustments.Length != glyphs.Count)
        {
            throw new ArgumentException($"There shall be one adjustment per character of the text ({glyphs.Count}), not {adjustments.Length}.", nameof(adjustments));
        }

        for (int index = 0; index < glyphs.Count; index++)
        {
            if (!double.IsFinite(adjustments[index]))
            {
                throw new ArgumentException("Adjustments shall be finite numbers.", nameof(adjustments));
            }

            glyphs[index] = glyphs[index] with { Adjustment = adjustments[index] };
        }

        WriteShown(font, glyphs, "Tj");
    }

    /// <summary>Moves to the next line and shows text there (<c>'</c>, the same as <see cref="MoveToNextLine"/> then <see cref="ShowText(ReadOnlySpan{char}, bool)"/>).</summary>
    /// <param name="text">The text; every character shall have a code in the font.</param>
    /// <exception cref="ArgumentException">A character has no code in the font; the message names it. Nothing is written.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, no font is set, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.4.3, Table 107 (<c>'</c>).</remarks>
    public void ShowTextOnNextLine(ReadOnlySpan<char> text)
    {
        PdfCanvasFont font = ThrowIfCannotShow();
        WriteShown(font, Encode(font, text, nameof(text)), "'");
    }

    /// <summary>Sets the word and character spacing, moves to the next line and shows text there (<c>"</c>).</summary>
    /// <param name="text">The text; every character shall have a code in the font.</param>
    /// <param name="wordSpacing">The new word spacing (<see cref="SetWordSpacing"/>).</param>
    /// <param name="characterSpacing">The new character spacing (<see cref="SetCharacterSpacing"/>).</param>
    /// <exception cref="ArgumentException">A character has no code in the font (the message names it), or a spacing is not finite. Nothing is written.</exception>
    /// <exception cref="InvalidOperationException">No text object is open, no font is set, or the canvas is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.4.3, Table 107 (<c>"</c>: <c>aw Tw ac Tc string '</c>).</remarks>
    public void ShowTextOnNextLine(ReadOnlySpan<char> text, double wordSpacing, double characterSpacing)
    {
        PdfCanvasFont font = ThrowIfCannotShow();
        ThrowIfNotFinite(wordSpacing, characterSpacing);
        List<(CanvasGlyph Glyph, double Adjustment)> glyphs = Encode(font, text, nameof(text));
        _writer.Number(wordSpacing).Number(characterSpacing);
        WriteShown(font, glyphs, "\"");
    }

    /// <summary>Every glyph of <paramref name="text"/>, with no adjustment; throws for the first character the font has no code for.</summary>
    private static List<(CanvasGlyph Glyph, double Adjustment)> Encode(PdfCanvasFont font, ReadOnlySpan<char> text, string parameterName)
    {
        var glyphs = new List<(CanvasGlyph, double)>(text.Length);
        int index = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            glyphs.Add((font.GlyphOrThrow(rune, index, parameterName), 0));
            index += rune.Utf16SequenceLength;
        }

        return glyphs;
    }

    /// <summary>Writes the glyphs as one string and <paramref name="op"/>, or, when any adjustment is not 0, as a <c>TJ</c> array.</summary>
    private void WriteShown(PdfCanvasFont font, List<(CanvasGlyph Glyph, double Adjustment)> glyphs, string op)
    {
        bool adjusted = false;
        foreach ((CanvasGlyph _, double adjustment) in glyphs)
        {
            adjusted |= Math.Round(adjustment, 4) != 0;
        }

        bool hexadecimal = font.CodeLength > 1;
        if (!adjusted || op != "Tj")
        {
            _writer.String(Bytes(font, glyphs, 0, glyphs.Count), hexadecimal).Operator(op);
        }
        else
        {
            _writer.BeginArray();
            int start = 0;
            for (int index = 0; index < glyphs.Count; index++)
            {
                if (Math.Round(glyphs[index].Adjustment, 4) != 0)
                {
                    _writer.String(Bytes(font, glyphs, start, index + 1 - start), hexadecimal).Number(glyphs[index].Adjustment);
                    start = index + 1;
                }
            }

            if (start < glyphs.Count)
            {
                _writer.String(Bytes(font, glyphs, start, glyphs.Count - start), hexadecimal);
            }

            _writer.EndArray().Operator("TJ");
        }

        foreach ((CanvasGlyph glyph, double _) in glyphs)
        {
            font.OnShown(glyph);
        }
    }

    /// <summary>The character codes of a run of glyphs, each in the font's code length, high byte first.</summary>
    private static byte[] Bytes(PdfCanvasFont font, List<(CanvasGlyph Glyph, double Adjustment)> glyphs, int start, int count)
    {
        int length = font.CodeLength;
        byte[] bytes = new byte[count * length];
        for (int index = 0; index < count; index++)
        {
            uint code = glyphs[start + index].Glyph.Code;
            for (int shift = 0; shift < length; shift++)
            {
                bytes[(index * length) + length - 1 - shift] = (byte)(code >> (8 * shift));
            }
        }

        return bytes;
    }

    private void SetTextState(double value, string name, string op)
    {
        ThrowIfPathOpen();
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value shall be a finite number.");
        }

        _writer.Number(value).Operator(op);
    }

    private PdfCanvasFont ThrowIfCannotShow()
    {
        ThrowIfNotInTextObject();
        return _font.Font ?? throw new InvalidOperationException("No font is set; call SetFont before showing text (ISO 32000-2 §9.3.1: the font has no initial value).");
    }

    private void ThrowIfNotInTextObject()
    {
        ThrowIfDisposed();
        if (!_inText)
        {
            throw new InvalidOperationException("No text object is open; begin one with BeginText (ISO 32000-2 §9.4.1, §8.2 Figure 9).");
        }
    }

    private void ThrowIfInTextObject()
    {
        if (_inText)
        {
            throw new InvalidOperationException(
                "A text object is open: only general graphics state, colour, text state, text positioning and text showing calls may follow until EndText (ISO 32000-2 §8.2, Figure 9).");
        }
    }

    /// <summary>The font and size of the text state (§9.3.1), saved and restored with the graphics state.</summary>
    private readonly record struct FontState(PdfCanvasFont? Font, double Size);
}
