using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A text markup annotation: highlighted, underlined, squiggly-underlined or struck-out text.</summary>
/// <remarks>ISO 32000-2 §12.5.6.10, Table 182.</remarks>
public abstract class PdfTextMarkupAnnotation : PdfMarkupAnnotation
{
    private protected PdfTextMarkupAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page, PdfAnnotationKind kind)
        : base(document, dictionary, reference, page, kind)
    {
    }

    /// <summary>Gets the quadrilaterals that enclose the marked-up text (<c>QuadPoints</c>, required), as stored; empty when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.10, Table 182. Counterclockwise, with the text along the edge from the first point to the second; written in other orders too (<see cref="PdfQuadrilateral"/>).</remarks>
    public IReadOnlyList<PdfQuadrilateral> QuadPoints => ReadQuadPoints(required: true, "Table 182");
}
