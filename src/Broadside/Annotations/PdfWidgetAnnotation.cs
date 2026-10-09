using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A widget annotation: the appearance of an interactive form field on a page.</summary>
/// <remarks>ISO 32000-2 §12.5.6.19, Table 191 (PDF 1.2). A field with one widget may merge both into one dictionary; the field's entries are read through the form field model. Additional actions (<c>AA</c>) are read through <see cref="PdfAnnotation.Dictionary"/> until the action ticket types them.</remarks>
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

    /// <summary>Gets the field dictionary this widget is a kid of (<c>Parent</c>), as stored, or <see langword="null"/>; required when the field has several widgets.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 191, and §12.7.4.</remarks>
    public CosDictionary? Parent => ReadDictionary(AnnotationNames.Parent);
}
