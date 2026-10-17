using Broadside.Annotations;

namespace Broadside.Viewer;

/// <summary>
/// What a shell shows in a note or popup for an annotation: its text, author and subject, the popup's rectangle and state, and the
/// replies to it. A model only; the shell draws it.
/// </summary>
/// <param name="Annotation">The annotation the note belongs to.</param>
/// <param name="Contents">The text (<c>Contents</c>).</param>
/// <param name="RichText">The rich text (<c>RC</c>) of a markup annotation, as stored.</param>
/// <param name="Title">The author (<c>T</c>) of a markup annotation.</param>
/// <param name="Subject">The subject (<c>Subj</c>) of a markup annotation.</param>
/// <param name="Modified">The modification date (<c>M</c>) as stored.</param>
/// <param name="PopupRectangle">Where the popup shows (its <c>Rect</c> in default user space), or <see langword="null"/> without one.</param>
/// <param name="IsOpen">Whether the popup is open initially (<c>Open</c>).</param>
/// <param name="Replies">Markup annotations on the same page replying to this one (<c>IRT</c> without <c>RT /Group</c>), in page order.</param>
/// <remarks>ISO 32000-2 §12.5.6.2 (markup annotations, Table 172) and §12.5.6.14 (popup annotations); spec #33 viewer capability 6.</remarks>
public sealed record AnnotationNote(
    PdfAnnotation Annotation,
    string? Contents,
    string? RichText,
    string? Title,
    string? Subject,
    string? Modified,
    PdfRectangle? PopupRectangle,
    bool IsOpen,
    IReadOnlyList<PdfMarkupAnnotation> Replies);
