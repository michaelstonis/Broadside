namespace Broadside.Viewer;

/// <summary>
/// One undoable edit in an <see cref="EditHistory"/>: an app's own edit recorded with <see cref="EditHistory.Record"/>, or one of the
/// built-in steps an <see cref="AnnotationLayer"/> records for its annotation and form field edits.
/// </summary>
/// <remarks>
/// <para>
/// Not a spec feature. The history calls <see cref="Undo"/> and <see cref="Redo"/> on the UI thread inside
/// <see cref="DocumentSession.BeginEdit"/>, so a step changes the document directly, through the <c>Pdf*</c> or <c>Cos*</c> API, and
/// returns what to draw again. The history calls them alternately: <see cref="Undo"/> first, then <see cref="Redo"/>, and so on.
/// </para>
/// <para>
/// A step that cannot be applied, because the document was changed outside the history since it was recorded, throws: the history
/// then returns <see langword="false"/>, reports a <see cref="Diagnostics.Diagnostic"/> through <see cref="PdfDocument.ReportDiagnostic"/>, drops
/// the step and clears the redo stack. A step should check before it changes anything, so a failure leaves the document as it was.
/// </para>
/// </remarks>
public interface IEditStep
{
    /// <summary>Gets the step's name for an Undo or Redo menu item, such as "Move annotation".</summary>
    string Name { get; }

    /// <summary>Reverts the edit.</summary>
    /// <param name="document">The session's document.</param>
    /// <returns>The areas to draw again; empty when nothing visible changed.</returns>
    IReadOnlyList<PageInvalidation> Undo(PdfDocument document);

    /// <summary>Applies the edit again after <see cref="Undo"/>.</summary>
    /// <param name="document">The session's document.</param>
    /// <returns>The areas to draw again; empty when nothing visible changed.</returns>
    IReadOnlyList<PageInvalidation> Redo(PdfDocument document);
}
