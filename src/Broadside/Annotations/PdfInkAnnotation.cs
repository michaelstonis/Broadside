using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>An ink annotation: one or more freehand paths.</summary>
/// <remarks>ISO 32000-2 §12.5.6.13, Table 185 (PDF 1.3).</remarks>
public sealed class PdfInkAnnotation : PdfMarkupAnnotation
{
    internal PdfInkAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Ink)
    {
    }

    /// <summary>Gets the paths (<c>InkList</c>, required): each a list of points in default user space; empty when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.13, Table 185.</remarks>
    public IReadOnlyList<IReadOnlyList<PathPoint>> InkList => ReadPointLists(AnnotationNames.InkList, required: true, "Table 185");

    /// <summary>Gets the path (<c>Path</c>, PDF 2.0): arrays of 2 numbers (the start point, then straight segments) or 6 (a cubic Bézier curve's two control points and end point); <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.13, Table 185.</remarks>
    public IReadOnlyList<IReadOnlyList<double>>? Path => ReadPath();
}
