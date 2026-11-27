using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Sanitizing;

/// <summary>
/// The content pass of sanitizing: one run per page, form XObject and annotation appearance removes what optional content hides
/// and, when asked, text in rendering mode 3, keeping every effect hidden content has on the graphics state (ISO 32000-2 §8.11.3.1).
/// </summary>
/// <remarks>
/// <para>
/// Hidden content (inside a <c>BDC /OC … EMC</c> sequence that is off in the default configuration, §8.11.3.2): a text-showing
/// operator becomes a <c>TJ</c> that moves as far (<see cref="TextAdvance"/>), so the text position stays (§8.11.3.1 requires it to
/// be updated); a path-painting operator becomes <c>n</c>, which ends the path without painting and keeps a pending clip; <c>sh</c>,
/// inline images and <c>Do</c> are removed. A <c>Do</c> of an XObject whose own <c>OC</c> is off is removed wherever it is
/// (§8.11.3.3). The markers of a hidden sequence go with it, and with <c>flatten</c> every <c>OC</c> sequence's markers go.
/// </para>
/// <para>
/// Invisible text (§9.3.6, Table 104, mode 3): the whole operator is replaced the same way, since the rendering mode applies to the
/// whole operator. Mode 7 adds to the clip and is kept. Nested content (forms, Type 3 glyphs) is not entered: each form is
/// rewritten in a run of its own. A reused editor allocates nothing per operator.
/// </para>
/// </remarks>
internal sealed class SanitizeContentEditor : ContentEditor
{
    private static readonly CosName XObjectKey = new("XObject");
    private static readonly CosName OptionalContentKey = new("OC");
    private static readonly CosName AlternatesKey = new("Alternates");

    private readonly ContentWriter _writer = new();
    private readonly Dictionary<CosStream, bool> _hiddenObjects = new(ReferenceEqualityComparer.Instance);
    private readonly PdfDocument _document;
    private readonly PdfOptionalContentProperties? _properties;
    private readonly PdfOptionalContentState? _state;
    private readonly bool _removeHidden;
    private readonly bool _flatten;
    private readonly bool _removeInvisible;

    private int _textOperator = -1;
    private ContentOperatorCode _textCode;
    private Matrix _textStart;
    private bool _textVertical;
    private bool _textHidden;

    private (int Operator, bool Remove)[] _marked = new (int, bool)[8];
    private int _markedCount;

    public SanitizeContentEditor(PdfDocument document, PdfOptionalContentProperties? properties, PdfSanitizeOptionalContent mode, bool removeInvisible)
    {
        _document = document;
        _properties = properties;
        _state = properties?.GetDefaultStates();
        _removeHidden = properties is not null && mode != PdfSanitizeOptionalContent.Keep;
        _flatten = properties is not null && mode == PdfSanitizeOptionalContent.FlattenVisible;
        _removeInvisible = removeInvisible;
    }

    public override ContentEvents Events => ContentEvents.Operators | ContentEvents.MarkedContent | ContentEvents.Forms;

    /// <summary>Gets the number of hidden drawing operators removed or made to draw nothing.</summary>
    public int HiddenOperators { get; private set; }

    /// <summary>Gets the number of optional-content marked-content sequences whose markers were removed.</summary>
    public int Markers { get; private set; }

    /// <summary>Gets the number of text-showing operators in rendering mode 3 removed.</summary>
    public int InvisibleOperators { get; private set; }

    /// <summary>Whether an XObject's own <c>OC</c> is off in the default configuration (§8.11.3.3); an image with alternates is left to them (§8.9.5.4).</summary>
    public bool IsHidden(CosStream stream)
    {
        if (!_removeHidden)
        {
            return false;
        }

        if (!_hiddenObjects.TryGetValue(stream, out bool hidden))
        {
            CosDictionary dictionary = stream.Dictionary;
            hidden = dictionary.TryGetValue(OptionalContentKey, out CosObject? group)
                && !dictionary.ContainsKey(AlternatesKey)
                && !_properties!.IsVisible(group, _state!);
            _hiddenObjects[stream] = hidden;
        }

        return hidden;
    }

    public override void BeginRun(ContentContext context)
    {
        _textOperator = -1;
        _markedCount = 0;
    }

    public override void EndRun(ContentContext context)
    {
        FlushText(context);
        while (_markedCount > 0)
        {
            // A sequence the stream never closes (§14.6: it should not happen): its opening marker goes alone.
            (int start, bool remove) = _marked[--_markedCount];
            if (remove)
            {
                Remove(start);
                Markers++;
            }
        }
    }

    public override ContentVisit BeginForm(in FormEvent form, ContentContext context) => ContentVisit.Skip;

    public override void VisitOperator(in ContentOperator op, ContentContext context)
    {
        if (context.Depth != 0)
        {
            return;
        }

        int index = CurrentOperator;
        if (_textOperator >= 0 && _textOperator != index)
        {
            FlushText(context);
        }

        bool hidden = _removeHidden && context.IsHidden;
        switch (op.Code)
        {
            case ContentOperatorCode.ShowText or ContentOperatorCode.ShowTextAdjusted or ContentOperatorCode.NextLineShowText
                or ContentOperatorCode.NextLineShowTextWithSpacing:
                ref readonly GraphicsState state = ref context.State;
                if (hidden || (_removeInvisible && state.TextRenderingMode == TextRenderingMode.Invisible))
                {
                    _textOperator = index;
                    _textCode = op.Code;
                    _textStart = context.TextMatrix;
                    _textVertical = state.Font?.IsVertical ?? false;
                    _textHidden = hidden;
                }

                break;
            case ContentOperatorCode.Stroke or ContentOperatorCode.CloseAndStroke or ContentOperatorCode.Fill or ContentOperatorCode.FillObsolete
                or ContentOperatorCode.FillEvenOdd or ContentOperatorCode.FillAndStroke or ContentOperatorCode.FillEvenOddAndStroke
                or ContentOperatorCode.CloseFillAndStroke or ContentOperatorCode.CloseFillEvenOddAndStroke when hidden:
                Replace(index, "n"u8);
                HiddenOperators++;
                break;
            case ContentOperatorCode.PaintShading or ContentOperatorCode.BeginInlineImage when hidden:
                Remove(index);
                HiddenOperators++;
                break;
            case ContentOperatorCode.PaintXObject when hidden || HidesXObject(op, context):
                Remove(index);
                HiddenOperators++;
                break;
        }
    }

    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        if (context.Depth != 0)
        {
            return;
        }

        if (_markedCount == _marked.Length)
        {
            Array.Resize(ref _marked, _marked.Length * 2);
        }

        bool remove = mc.IsOptionalContent && (_flatten || (_removeHidden && mc.IsHidden));
        _marked[_markedCount++] = (CurrentOperator, remove);
    }

    public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        if (context.Depth != 0 || _markedCount == 0)
        {
            return;
        }

        (int start, bool remove) = _marked[--_markedCount];
        if (remove)
        {
            Remove(start);
            Remove(CurrentOperator);
            Markers++;
        }
    }

    private bool HidesXObject(in ContentOperator op, ContentContext context)
    {
        ContentOperands operands = op.Operands;
        if (!_removeHidden || operands.Count == 0 || operands[operands.Count - 1].Kind != ContentOperandKind.Name)
        {
            return false;
        }

        CosObject? value = _document.ColorSpaces.FindResource(context.Resources, XObjectKey, operands[operands.Count - 1].Bytes, out _);
        return value is not null && _document.Resolve(value) is CosStream stream && IsHidden(stream);
    }

    private void FlushText(ContentContext context)
    {
        if (_textOperator < 0)
        {
            return;
        }

        TextAdvance.Write(_writer, _textCode, _textStart, _textVertical, context);
        Replace(_textOperator, _writer.Written);
        if (_textHidden)
        {
            HiddenOperators++;
        }
        else
        {
            InvisibleOperators++;
        }

        _textOperator = -1;
    }
}
