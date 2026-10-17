namespace Broadside.Viewer.Maui;

/// <summary>What the framework-neutral <see cref="ViewerController"/> asks of the view that hosts it (the MAUI <see cref="PdfView"/>, or a test).</summary>
internal interface IViewerHost
{
    /// <summary>Asks for <see cref="ViewerController.Frame"/> to run soon (on the UI thread).</summary>
    void RequestFrame();

    /// <summary>A new frame was published: redraw the canvas.</summary>
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
internal sealed record FieldEditor(
    AnnotationTarget Target,
    FieldEditorKind Kind,
    string Text,
    bool IsMultiline,
    bool IsPassword,
    int? MaxLength,
    IReadOnlyList<string> Options,
    int SelectedIndex);
