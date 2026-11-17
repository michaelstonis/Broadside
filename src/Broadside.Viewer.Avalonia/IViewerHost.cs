namespace Broadside.Viewer.Avalonia;

/// <summary>What the framework-neutral <see cref="ViewerController"/> asks of the view that hosts it (the Avalonia <see cref="PdfView"/>, or a test).</summary>
internal interface IViewerHost
{
    /// <summary>Asks for <see cref="ViewerController.Frame"/> to run on the next animation frame (UI thread).</summary>
    void RequestFrame();

    /// <summary>The frame changed: render the control again.</summary>
    void RequestPaint();

    void StateChanged(PdfViewState state);

    void ActionRequested(ViewerActionEventArgs e);

    void Error(Exception exception);

    /// <summary>The form-field editor opens, changes field, or closes (<see langword="null"/>).</summary>
    void EditorChanged(FieldEditor? editor);

    /// <summary>The open editor's widget moved on screen (device-independent units from the view's top-left).</summary>
    void EditorMoved(double x, double y, double width, double height);

    /// <summary>The accessibility tree changed: rebuild (pages loaded, children changed) or move the semantic focus.</summary>
    void AccessibilityChanged(AccessibilityChange change);

    /// <summary>Puts copied text on the clipboard.</summary>
    void CopyText(string text);
}

/// <summary>The kinds of form-field editor the view opens over a focused widget.</summary>
internal enum FieldEditorKind
{
    Text,
    Choice,
}

/// <summary>A form-field editor to show over the focused widget (ISO 32000-2 §12.7.5.3 text fields, §12.7.5.4 choice fields).</summary>
/// <remarks>Built from the core form filling's state; the native control only takes input (<c>IsEditable</c>: an editable combo box also takes typed text).</remarks>
internal sealed record FieldEditor(
    AnnotationTarget Target,
    FieldEditorKind Kind,
    string Text,
    bool IsMultiline,
    bool IsPassword,
    int? MaxLength,
    IReadOnlyList<string> Options,
    int SelectedIndex,
    bool IsEditable = false,
    bool IsMultiSelect = false)
{
    /// <summary>Gets the options picked, ascending: one at most unless <see cref="IsMultiSelect"/> (§12.7.5.4, MultiSelect).</summary>
    public IReadOnlyList<int> SelectedIndices { get; init; } = SelectedIndex >= 0 ? [SelectedIndex] : [];
}
