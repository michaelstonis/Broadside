using Broadside.Annotations;

namespace Broadside.Viewer;

/// <summary>
/// An action the user triggered by activating a link or a widget: the viewer performs none of them itself. The shell navigates
/// (<see cref="AnnotationLayer.GetDestinationViewport"/> for a go-to), asks before opening a URI or another file, and never runs a
/// script (<see cref="IsScript"/> events are informational).
/// </summary>
/// <remarks>ISO 32000-2 §12.6 (actions, Table 201), §12.6.4.2 (go-to), §12.6.4.3 (remote go-to), §12.6.4.8 (URI), §12.6.4.12
/// (named actions); spec #33 viewer capability 6.</remarks>
public sealed class ViewerActionEventArgs : EventArgs
{
    internal ViewerActionEventArgs(PdfAnnotation? annotation, PdfAction? action, PdfExplicitDestination? destination)
    {
        Annotation = annotation;
        Action = action;
        Destination = destination;
    }

    /// <summary>Gets the link or widget activated, or <see langword="null"/> when the action came from elsewhere.</summary>
    public PdfAnnotation? Annotation { get; }

    /// <summary>
    /// Gets the action, one of the activated annotation's action tree in execution order (§12.6.2: <c>Next</c>), or
    /// <see langword="null"/> for a link with a destination (<c>Dest</c>) and no action.
    /// </summary>
    public PdfAction? Action { get; }

    /// <summary>Gets the action's kind; <see cref="PdfActionKind.GoTo"/> for a link's destination.</summary>
    public PdfActionKind Kind => Action?.Kind ?? PdfActionKind.GoTo;

    /// <summary>Gets the resolved destination in this document of a go-to (named destinations looked up), or <see langword="null"/>.</summary>
    public PdfExplicitDestination? Destination { get; }

    /// <summary>Gets the zero-based page a go-to leads to, or <see langword="null"/> when it leads nowhere in this document.</summary>
    public int? PageIndex => Destination?.PageIndex;

    /// <summary>Gets the absolute URI of a URI action (resolved against the document's base URI), or <see langword="null"/>.</summary>
    /// <remarks>Untrusted data from the document: the viewer never opens it. The stored text is <see cref="PdfUriAction.Uri"/>.</remarks>
    public Uri? Uri => (Action as PdfUriAction)?.ResolveUri();

    /// <summary>Gets the operation of a named action (§12.6.4.12, Table 216), or <see langword="null"/>.</summary>
    public PdfNamedOperation? NamedOperation => (Action as PdfNamedAction)?.Operation;

    /// <summary>
    /// Gets whether the action is a script (a JavaScript action, §12.6.4.17). The viewer never runs scripts: the event only says one
    /// was asked for.
    /// </summary>
    public bool IsScript => Kind == PdfActionKind.JavaScript;
}
