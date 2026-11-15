namespace Broadside.Viewer.WinUI;

/// <summary>What <see cref="ViewerController"/> asks of the control that hosts it (the WinUI <c>PdfView</c>, or a test shell).</summary>
/// <remarks>Every member is called on the UI thread.</remarks>
internal interface IViewerHost
{
    /// <summary>Asks for <see cref="ViewerController.Frame"/> to run on the next rendering pass.</summary>
    void RequestFrame();

    /// <summary>Shows a composed frame (valid until the next one is composed).</summary>
    void Present(ViewerFrame frame);

    /// <summary>The page, page count, zoom or loading state changed.</summary>
    void StateChanged(PdfViewState state);

    /// <summary>A link, widget or annotation asks for an action (never performed by the controller beyond in-document navigation).</summary>
    void ActionRequested(ViewerActionEventArgs e);

    /// <summary>Loading a page's text, links or accessibility nodes failed.</summary>
    void Error(Exception exception);

    /// <summary>A form field's editor opens (non-null) or closes (null).</summary>
    void EditorChanged(FieldEditor? editor);

    /// <summary>The open editor's field moved to a rectangle in device-independent units.</summary>
    void EditorMoved(double x, double y, double width, double height);

    /// <summary>The accessible tree of the loaded pages changed, or the reading focus moved.</summary>
    void AccessibilityChanged(AccessibilityChange change);

    /// <summary>Puts text on the clipboard (Ctrl+C).</summary>
    void CopyText(string text);
}

/// <summary>The kind of native editor a focused form field needs.</summary>
internal enum FieldEditorKind
{
    /// <summary>A text field (single or multiple lines, or a password).</summary>
    Text,

    /// <summary>A combo box or list box.</summary>
    Choice,
}

/// <summary>A focused form field's editor: what the host shows over the widget.</summary>
internal sealed record FieldEditor(
    AnnotationTarget Target,
    FieldEditorKind Kind,
    string Text,
    bool IsMultiline,
    bool IsPassword,
    int? MaxLength,
    IReadOnlyList<string> Options,
    int SelectedIndex);
