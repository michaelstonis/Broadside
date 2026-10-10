using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>Text objects (Table 105), text state (Table 103), positioning (Table 106), showing (Table 107) and Type 3 glyph operators (Table 111).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.3, §9.4. The text matrix T<sub>m</sub> and line matrix T<sub>lm</sub> belong to the text object, not the graphics
/// state (§9.4.1): <c>q</c> and <c>Q</c> neither save nor restore them. Each character code of a shown string is one
/// <see cref="GlyphEvent"/> placed by <c>[T_fs·T_h 0 0 T_fs 0 T_rise] × T_m</c> and followed by the advance of §9.4.4:
/// <c>t_x = ((w0 − T_j/1000) × T_fs + T_c + T_w) × T_h</c> horizontally, <c>t_y = (w1 − T_j/1000) × T_fs + T_c + T_w</c> vertically.
/// Word spacing applies only to a single-byte code 32 (§9.3.3). The <c>TJ</c> numbers move the text matrix between strings and are
/// reported on the next glyph.
/// </para>
/// <para>
/// Clipping rendering modes (Table 104, 4 to 7) collect the glyphs of the text object; at <c>ET</c>, after the glyphs are painted,
/// they become one text clip node intersected under the nonzero rule. Type 3 glyphs never clip.
/// </para>
/// <para>
/// Repairs: text shown before <c>Tf</c>, or with a font name the resources lack, is measured with Helvetica's Standard 14 metrics
/// (as PDFBox does; the size stays 0 when no <c>Tf</c> set one, so only T<sub>c</sub> and T<sub>w</sub> advance) with
/// <c>ContentFontMissing</c>; text positioned or shown outside a text object starts from the identity matrix (pdf.js keeps it, PDFBox
/// drops it); a <c>TJ</c> element that is neither a string nor a number is skipped. <c>d0</c> and <c>d1</c> are issue #57's.
/// </para>
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private TextClipGlyph[] _clipGlyphs = [];
    private int _clipGlyphCount;
    private int _textClipStart;
    private Matrix _textMatrix = Matrix.Identity;
    private Matrix _lineMatrix = Matrix.Identity;
    private bool _textMatricesValid;
    private double _pendingAdjustment;

    /// <summary>Gets the text matrix T<sub>m</sub>.</summary>
    public Matrix TextMatrix => _textMatrix;

    /// <summary>Gets the text line matrix T<sub>lm</sub>.</summary>
    public Matrix TextLineMatrix => _lineMatrix;

    private void ExecuteText(ContentOperatorCode code, ContentOperands operands, int offset)
    {
        switch (code)
        {
            case ContentOperatorCode.BeginText:
                if (_inText)
                {
                    Report(ContentIssue.TextObjectUnbalanced, offset, "BT inside a text object; ignored.");
                    break;
                }

                _inText = true;
                _textMatrix = _lineMatrix = Matrix.Identity;
                _textMatricesValid = true;
                _textClipStart = _clipGlyphCount;
                if ((_events & ContentEvents.Text) != 0)
                {
                    _processor.BeginText(_context);
                }

                break;
            case ContentOperatorCode.EndText:
                if (!_inText)
                {
                    Report(ContentIssue.TextObjectUnbalanced, offset, "ET without a text object; ignored.");
                    break;
                }

                EndTextObject();
                break;
            case ContentOperatorCode.SetCharacterSpacing:
                State.CharacterSpacing = operands[0].Number;
                break;
            case ContentOperatorCode.SetWordSpacing:
                State.WordSpacing = operands[0].Number;
                break;
            case ContentOperatorCode.SetHorizontalScaling:
                State.HorizontalScaling = operands[0].Number / 100;
                break;
            case ContentOperatorCode.SetLeading:
                State.Leading = operands[0].Number;
                break;
            case ContentOperatorCode.SetFont:
                SetFont(operands[0].Bytes, operands[1].Number, offset);
                break;
            case ContentOperatorCode.SetTextRenderingMode:
                double mode = operands[0].Number;
                if (mode is 0 or 1 or 2 or 3 or 4 or 5 or 6 or 7)
                {
                    State.TextRenderingMode = (TextRenderingMode)(int)mode;
                }
                else
                {
                    Report(ContentIssue.GraphicsStateRange, offset, "A text rendering mode that is not an integer from 0 to 7; fill (0) is used.");
                    State.TextRenderingMode = TextRenderingMode.Fill;
                }

                break;
            case ContentOperatorCode.SetTextRise:
                State.TextRise = operands[0].Number;
                break;
            case ContentOperatorCode.MoveText:
                MoveText(operands[0].Number, operands[1].Number);
                break;
            case ContentOperatorCode.MoveTextSetLeading:
                State.Leading = -operands[1].Number;
                MoveText(operands[0].Number, operands[1].Number);
                break;
            case ContentOperatorCode.SetTextMatrix:
                _textMatrix = _lineMatrix = new Matrix(
                    operands[0].Number, operands[1].Number, operands[2].Number, operands[3].Number, operands[4].Number, operands[5].Number);
                _textMatricesValid = true;
                break;
            case ContentOperatorCode.NextLine:
                MoveText(0, -State.Leading);
                break;
            case ContentOperatorCode.ShowText:
                _pendingAdjustment = 0;
                ShowString(operands[0].Bytes);
                break;
            case ContentOperatorCode.ShowTextAdjusted:
                ShowAdjusted(operands[0].Items, offset);
                break;
            case ContentOperatorCode.NextLineShowText:
                MoveText(0, -State.Leading);
                _pendingAdjustment = 0;
                ShowString(operands[0].Bytes);
                break;
            case ContentOperatorCode.NextLineShowTextWithSpacing:
                State.WordSpacing = operands[0].Number;
                State.CharacterSpacing = operands[1].Number;
                MoveText(0, -State.Leading);
                _pendingAdjustment = 0;
                ShowString(operands[2].Bytes);
                break;
        }
    }

    /// <summary>Ends the text object: at <c>ET</c>, or at the end of a stream that left one open. A text clip takes effect here.</summary>
    private void EndTextObject()
    {
        _inText = false;
        _textMatricesValid = false;
        if ((_events & ContentEvents.Text) != 0)
        {
            _processor.EndText(_context);
        }

        if (_clipGlyphCount > _textClipStart)
        {
            ReadOnlySpan<TextClipGlyph> glyphs = _clipGlyphs.AsSpan(_textClipStart, _clipGlyphCount - _textClipStart);
            int parent = State.ClipHandle;
            int handle = Clips.AddText(parent, glyphs, State.Ctm);
            State.ClipHandle = handle;
            var clipEvent = new ClipEvent
            {
                Handle = handle,
                ParentHandle = parent,
                Kind = ClipKind.Text,
                Rule = FillRule.NonZero,
                Ctm = State.Ctm,
                Glyphs = glyphs,
            };
            _processor.IntersectClip(clipEvent, _context);
            _clipGlyphs.AsSpan(_textClipStart, _clipGlyphCount - _textClipStart).Clear();
            _clipGlyphCount = _textClipStart;
        }
    }

    /// <summary>Starts from the identity outside a text object, where the matrices were discarded (§9.4.1).</summary>
    private void EnsureTextMatrices()
    {
        if (!_textMatricesValid)
        {
            _textMatrix = _lineMatrix = Matrix.Identity;
            _textMatricesValid = true;
        }
    }

    /// <summary><c>Td</c>: T<sub>m</sub> = T<sub>lm</sub> = translate(t<sub>x</sub>, t<sub>y</sub>) × T<sub>lm</sub> (Table 106).</summary>
    private void MoveText(double tx, double ty)
    {
        EnsureTextMatrices();
        _lineMatrix = Matrix.CreateTranslation(tx, ty) * _lineMatrix;
        _textMatrix = _lineMatrix;
    }

    /// <summary><c>Tf</c>: the font named in the current resources' <c>Font</c> subdictionary, and the size (Table 103).</summary>
    private void SetFont(ReadOnlySpan<byte> name, double size, int offset)
    {
        PdfDocument document = _context.Document;
        CosObject? value = document.ColorSpaces.FindResource(_context.Resources, FontNames.Font, name, out _);
        PdfFont? font = value is null ? null : document.GetFont(value);
        if (font is null)
        {
            Report(ContentIssue.FontMissing, offset, "Tf names a font that is not in the resources' Font dictionary; Helvetica's metrics are used.");
        }

        State.Font = font;
        State.FontSize = size;
    }

    /// <summary><c>TJ</c>: strings shown, numbers moving the text matrix by −n/1000 text space units (Table 107).</summary>
    private void ShowAdjusted(ContentOperands items, int offset)
    {
        _pendingAdjustment = 0;
        foreach (ContentOperand item in items)
        {
            if (item.Kind == ContentOperandKind.String)
            {
                ShowString(item.Bytes);
            }
            else if (item.IsNumber)
            {
                EnsureTextMatrices();
                double adjustment = item.Number;
                PdfFont font = State.Font ?? _context.Document.FallbackFont;
                double distance = -adjustment / 1000 * State.FontSize;
                _textMatrix = font.IsVertical
                    ? Matrix.CreateTranslation(0, distance) * _textMatrix
                    : Matrix.CreateTranslation(distance * State.HorizontalScaling, 0) * _textMatrix;
                _pendingAdjustment += adjustment;
            }
            else
            {
                Report(ContentIssue.OperandType, offset, "A TJ array element is neither a string nor a number; skipped.");
            }
        }

        _pendingAdjustment = 0;
    }

    /// <summary>Shows one string: one glyph per character code, each placed and then advanced (§9.4.3, §9.4.4).</summary>
    private void ShowString(ReadOnlySpan<byte> text)
    {
        EnsureTextMatrices();
        PdfFont? selected = State.Font;
        if (selected is null)
        {
            Report(ContentIssue.FontMissing, -1, "Text is shown without a font selected by Tf; Helvetica's metrics are used.");
        }

        PdfFont font = selected ?? _context.Document.FallbackFont;
        double size = State.FontSize;
        double scaling = State.HorizontalScaling;
        double characterSpacing = State.CharacterSpacing;
        double wordSpacing = State.WordSpacing;
        var textSpace = new Matrix(size * scaling, 0, 0, size, 0, State.TextRise);
        TextRenderingMode mode = State.TextRenderingMode;
        bool vertical = font.IsVertical;
        bool type3 = font is PdfType3Font;
        bool hidden = _context.IsHidden;
        bool report = (_events & ContentEvents.Glyphs) != 0 && (!hidden || (_events & ContentEvents.HiddenContent) != 0);
        bool clips = mode >= TextRenderingMode.FillClip && !type3 && size != 0 && (_events & ContentEvents.Clips) != 0;
        int position = 0;
        while (position < text.Length)
        {
            ShownGlyph shown = font.ReadShownGlyph(text[position..]);
            uint code = shown.Code;
            int length = shown.Length;
            double w0 = shown.HorizontalDisplacement;
            double w1 = shown.VerticalDisplacement;
            bool space = shown.AppliesWordSpacing;
            double spacing = characterSpacing + (space ? wordSpacing : 0);
            Matrix glyphMatrix = textSpace * _textMatrix;
            double advanceX;
            double advanceY;
            if (vertical)
            {
                // §9.7.4.3: the glyph's horizontal origin is the current point minus the position vector (scaled by the font size).
                glyphMatrix = Matrix.CreateTranslation(-shown.PositionVector.X, -shown.PositionVector.Y) * glyphMatrix;
                advanceX = 0;
                advanceY = (w1 * size) + spacing;
            }
            else
            {
                advanceX = ((w0 * size) + spacing) * scaling;
                advanceY = 0;
            }

            if (report)
            {
                var glyph = new GlyphEvent
                {
                    Font = font,
                    FontDictionary = font.Dictionary,
                    CharacterCode = code,
                    SourceBytes = text,
                    SourceIndex = position,
                    CodeLength = length,
                    TextMatrix = glyphMatrix,
                    Ctm = State.Ctm,
                    HorizontalDisplacement = w0,
                    VerticalDisplacement = w1,
                    AdvanceX = advanceX,
                    AdvanceY = advanceY,
                    Adjustment = _pendingAdjustment,
                    WordSpacingApplied = space,
                    IsVertical = vertical,
                    RenderingMode = mode,
                    IsHidden = hidden,
                };
                _processor.ShowGlyph(glyph, _context);
                if (type3 && (_events & ContentEvents.Type3GlyphContent) != 0 && mode is not (TextRenderingMode.Invisible or TextRenderingMode.Clip))
                {
                    RunType3Glyph(glyph, (PdfType3Font)font, (byte)code, _processor);
                }
            }

            _pendingAdjustment = 0;
            if (clips)
            {
                AddClipGlyph(new TextClipGlyph(font, code, glyphMatrix, State.Ctm));
            }

            _textMatrix = Matrix.CreateTranslation(advanceX, advanceY) * _textMatrix;
            position += length;
        }
    }

    private void AddClipGlyph(in TextClipGlyph glyph)
    {
        if (_clipGlyphCount == _clipGlyphs.Length)
        {
            Array.Resize(ref _clipGlyphs, Math.Max(16, _clipGlyphs.Length * 2));
        }

        _clipGlyphs[_clipGlyphCount++] = glyph;
    }
}
