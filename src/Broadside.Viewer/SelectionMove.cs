namespace Broadside.Viewer;

/// <summary>A keyboard caret move over the selectable text, in reading order (Shift+arrow keys extend by one).</summary>
/// <remarks>Spec #33 viewer capability 4.</remarks>
public enum SelectionMove
{
    /// <summary>One glyph back (Left).</summary>
    CharacterBackward,

    /// <summary>One glyph forward (Right).</summary>
    CharacterForward,

    /// <summary>To the start of the word before the caret (Ctrl/Option+Left).</summary>
    WordBackward,

    /// <summary>To the end of the word after the caret (Ctrl/Option+Right).</summary>
    WordForward,

    /// <summary>To the nearest position on the line before, in reading order (Up).</summary>
    LineUp,

    /// <summary>To the nearest position on the line after, in reading order (Down).</summary>
    LineDown,

    /// <summary>To the start of the caret's line (Home).</summary>
    LineStart,

    /// <summary>To the end of the caret's line (End).</summary>
    LineEnd,

    /// <summary>To the start of the document (Ctrl/Cmd+Home).</summary>
    DocumentStart,

    /// <summary>To the end of the document (Ctrl/Cmd+End).</summary>
    DocumentEnd,
}
