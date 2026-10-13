using Broadside.Annotations;

namespace Broadside;

/// <content>Flattening form fields and annotations into page content (issue #170): ISO 32000-2 §12.5.5.</content>
public sealed partial class PdfDocument
{
    /// <summary>Flattens the form's fields into the page content and removes the form, as <see cref="Flatten(PdfFlattenOptions)"/> does with default options.</summary>
    /// <returns><see langword="true"/> when anything changed.</returns>
    /// <exception cref="InvalidOperationException">The form has an XFA resource; nothing is changed.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a signed form or the first deviation found in the content; nothing is changed.</exception>
    /// <remarks>ISO 32000-2 §12.5.5 and §12.7.</remarks>
    public bool Flatten() => Flatten(new PdfFlattenOptions());

    /// <summary>
    /// Flattens the form's fields, and optionally every annotation, into the page content: each one's normal appearance becomes part
    /// of its page, and the widgets, the annotations and the interactive form are removed.
    /// </summary>
    /// <param name="options">Whether annotations other than widgets are flattened too, and for which intent.</param>
    /// <returns><see langword="true"/> when anything changed: an annotation was removed or the form was.</returns>
    /// <exception cref="InvalidOperationException">
    /// The form has an XFA resource (Annex K, reported at open as <c>XfaFormPresent</c>): its fields are XFA's to lay out, so the
    /// AcroForm appearances may not be what the form shows; nothing is changed.
    /// </exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a signed form or the first deviation found in the content; nothing is changed.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.5.5. Each annotation's normal appearance (for its appearance state, <c>AS</c>) is referenced from the page's
    /// resources as the form XObject it is, and drawn after the page's own content, in <c>Annots</c> order, with the matrix <c>A</c> of
    /// Algorithm "Appearance streams" (the form's own <c>Matrix</c> is applied by <c>Do</c>, making <c>AA</c>). The page's content is
    /// kept byte for byte inside <c>q</c> … <c>Q</c> (balanced against any unmatched <c>q</c> or <c>Q</c> it has), so the appearances
    /// start from the default graphics state (<see cref="PdfPage.RewriteContent(Content.ContentEditor)"/>). An annotation with
    /// optional content (<c>OC</c>, §8.11.3.3) is drawn inside an <c>/OC</c> marked-content sequence, so it stays optional; one with
    /// an opacity (<c>CA</c>, <c>ca</c>) or blend mode (<c>BM</c>) is composited as §12.5.5 says, as a transparency group with those
    /// values.
    /// </para>
    /// <para>
    /// Annotations with the Hidden flag, and with the NoView flag (<see cref="PdfFlattenIntent.View"/>) or without the Print flag
    /// (<see cref="PdfFlattenIntent.Print"/>), are removed without being drawn, as are those without an appearance. The
    /// appearances are flattened as they are: a form whose <c>NeedAppearances</c> is set may need its appearances regenerated first
    /// (<see cref="Forms.PdfTextField.GenerateAppearances"/>). A signed form records a <c>FlattenInvalidatesSignatures</c> warning,
    /// since removing the signature fields changes what the signatures cover.
    /// </para>
    /// <para>
    /// A page's <c>Annots</c> then lists only the annotations it keeps, and goes when it keeps none, unless a page tree node above
    /// the page has an <c>Annots</c> of its own (§7.7.3.3, Table 31: not inheritable, but read as viewers read it when the page has
    /// none): the page then keeps an empty one, so the node's annotations do not show on it instead.
    /// </para>
    /// <para>
    /// Removed annotations and the form's fields stay objects of the document until a save with
    /// <see cref="PdfSaveOptions.RemoveUnreachableObjects"/> drops them. NoZoom and NoRotate cannot be kept in page content: the
    /// appearance is placed as at 100% zoom on an unrotated page. A mutation: no read may run concurrently with it.
    /// </para>
    /// </remarks>
    public bool Flatten(PdfFlattenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return AnnotationFlattening.Flatten(this, options);
    }
}
