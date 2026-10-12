using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A projection annotation: a markup annotation that projects, for example, a measurement onto 3D or geospatial content.</summary>
/// <remarks>ISO 32000-2 §12.5.6.24 (PDF 2.0). It has the entries of Tables 166 and 172 only; with a zero-size rectangle it shall have no appearance.</remarks>
public sealed class PdfProjectionAnnotation : PdfMarkupAnnotation
{
    internal PdfProjectionAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Projection)
    {
    }
}
