using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A caret annotation: a symbol marking where text is to be inserted.</summary>
/// <remarks>ISO 32000-2 §12.5.6.11, Table 183 (PDF 1.5).</remarks>
public sealed class PdfCaretAnnotation : PdfMarkupAnnotation
{
    internal PdfCaretAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Caret)
    {
    }

    /// <summary>Gets the differences between the annotation rectangle and the drawn rectangle (<c>RD</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.11, Table 183.</remarks>
    public PdfRectangleDifferences? RectangleDifferences => ReadRectangleDifferences();

    /// <summary>Gets the symbol associated with the caret (<c>Sy</c>); default none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.11, Table 183.</remarks>
    public PdfCaretSymbol Symbol => ReadChoice(AnnotationNames.Sy, PdfCaretSymbol.None, ("P", PdfCaretSymbol.Paragraph), ("None", PdfCaretSymbol.None));
}
