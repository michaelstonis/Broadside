using Broadside.Forms;
using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A widget annotation: the appearance of an interactive form field on a page.</summary>
/// <remarks>ISO 32000-2 §12.5.6.19, Table 191 (PDF 1.2). A field with one widget may merge both into one dictionary; the field's entries are read through the form field model.</remarks>
public sealed class PdfWidgetAnnotation : PdfAnnotation
{
    internal PdfWidgetAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Widget)
    {
    }

    /// <summary>Gets the visual effect when the widget is activated (<c>H</c>); default invert. <c>T</c> (toggle) reads as push.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 191.</remarks>
    public PdfHighlightMode HighlightMode => ReadChoice(AnnotationNames.H, PdfHighlightMode.Invert, ("N", PdfHighlightMode.None), ("I", PdfHighlightMode.Invert), ("O", PdfHighlightMode.Outline), ("P", PdfHighlightMode.Push), ("T", PdfHighlightMode.Push));

    /// <summary>Gets the appearance characteristics dictionary used to construct the appearance (<c>MK</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 191, and §12.5.6.19, Table 192.</remarks>
    public PdfAppearanceCharacteristics? AppearanceCharacteristics => ReadDictionary(AnnotationNames.MK) is { } characteristics ? new PdfAppearanceCharacteristics(this, characteristics) : null;

    /// <summary>Gets the action performed when the annotation is activated (<c>A</c>, PDF 1.1), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 191, and §12.6.</remarks>
    public PdfAction? Action => ReadAction(AnnotationNames.A);

    /// <summary>Gets the actions performed on the annotation's trigger events (<c>AA</c>, PDF 1.2), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 191, and §12.6.3, Table 197; read through <see cref="PdfDocument.GetAnnotationAdditionalActions"/>.</remarks>
    public PdfAnnotationAdditionalActions? AdditionalActions =>
        Dictionary.TryGetValue(AnnotationNames.AA, out CosObject? value) ? Document.GetAnnotationAdditionalActions(value, DiagnosticReference) : null;

    /// <summary>Gets the field dictionary this widget is a kid of (<c>Parent</c>), as stored, or <see langword="null"/>; required when the field has several widgets.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 191, and §12.7.4.</remarks>
    public CosDictionary? Parent => ReadDictionary(AnnotationNames.Parent);

    /// <summary>
    /// Gets the terminal field the widget belongs to: the field whose <c>Kids</c> lists it, or the field merged with it; <see langword="null"/>
    /// when the document has no interactive form or the widget belongs to no field. The same instance the form's field tree holds, whose
    /// <see cref="PdfTerminalField.Widgets"/> lists this widget.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.2 and §12.5.6.19. A widget the tree from <c>Fields</c> does not reach is resolved through its <c>Parent</c>
    /// chain with a <c>WidgetNotInFieldTree</c> diagnostic.
    /// </remarks>
    public PdfTerminalField? Field => Document.AcroForm?.FieldOf(this);

    /// <summary>Whether <paramref name="dictionary"/>'s subtype (a name, or a string read as one) is <c>Widget</c>.</summary>
    internal static bool IsWidget(PdfDocument document, CosDictionary dictionary) =>
        ReadSubtype(document, dictionary, out _) is { } subtype && string.Equals(subtype.Value, "Widget", StringComparison.Ordinal);
}
