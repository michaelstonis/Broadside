namespace Broadside.Content;

/// <summary>Text objects (Table 105), text state (Table 103), positioning (Table 106), showing (Table 107) and Type 3 glyph operators (Table 111).</summary>
/// <remarks>
/// ISO 32000-2 §9.3, §9.4, §9.6.4. Issue #55 tracks text objects (<c>BT</c>/<c>ET</c>, for the Figure 9 context rules) and reports
/// them; text state, positioning and glyph events are issue #56's, Type 3 glyph procedures issue #57's. Their operands are already
/// checked by the core.
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private void ExecuteText(ContentOperatorCode code, ContentOperands operands, int offset)
    {
        _ = operands;
        switch (code)
        {
            case ContentOperatorCode.BeginText:
                if (_inText)
                {
                    Report(ContentIssue.TextObjectUnbalanced, offset, "BT inside a text object; ignored.");
                    break;
                }

                _inText = true;
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
        }
    }

    /// <summary>Ends the text object: at <c>ET</c>, or at the end of a stream that left one open.</summary>
    private void EndTextObject()
    {
        _inText = false;
        if ((_events & ContentEvents.Text) != 0)
        {
            _processor.EndText(_context);
        }
    }
}
