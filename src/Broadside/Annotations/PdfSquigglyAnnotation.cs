using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A <c>Squiggly</c> text markup annotation (PDF 1.4).</summary>
/// <remarks>ISO 32000-2 §12.5.6.10, Table 182.</remarks>
public sealed class PdfSquigglyAnnotation : PdfTextMarkupAnnotation
{
    internal PdfSquigglyAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Squiggly)
    {
    }
}
