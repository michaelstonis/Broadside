using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A <c>Highlight</c> text markup annotation (PDF 1.3).</summary>
/// <remarks>ISO 32000-2 §12.5.6.10, Table 182.</remarks>
public sealed class PdfHighlightAnnotation : PdfTextMarkupAnnotation
{
    internal PdfHighlightAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Highlight)
    {
    }
}
