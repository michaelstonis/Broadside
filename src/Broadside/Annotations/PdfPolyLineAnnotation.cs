using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A polyline annotation: an open polygon.</summary>
/// <remarks>ISO 32000-2 §12.5.6.9, Table 181 (PDF 1.5).</remarks>
public sealed class PdfPolyLineAnnotation : PdfMarkupAnnotation
{
    internal PdfPolyLineAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.PolyLine)
    {
    }

    /// <summary>Gets the vertices in default user space (<c>Vertices</c>); empty when absent. Ignored when <see cref="Path"/> is present.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.9, Table 181: required unless <c>Path</c> is present, and then it shall not be present.</remarks>
    public IReadOnlyList<PathPoint> Vertices => ReadPoints(AnnotationNames.Vertices, required: !Dictionary.ContainsKey(AnnotationNames.Path), "Table 181");

    /// <summary>Gets the path (<c>Path</c>, PDF 2.0): arrays of 2 numbers (the start point, then straight segments) or 6 (a cubic Bézier curve's two control points and end point); <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.9, Table 181.</remarks>
    public IReadOnlyList<IReadOnlyList<double>>? Path => ReadPath();

    /// <summary>Gets the line ending at the first point (<c>LE</c>, PDF 1.4); default none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.9, Table 181, and Table 179.</remarks>
    public PdfLineEnding StartLineEnding => ReadLineEndings().Start;

    /// <summary>Gets the line ending at the last point (<c>LE</c>, PDF 1.4); default none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.9, Table 181, and Table 179.</remarks>
    public PdfLineEnding EndLineEnding => ReadLineEndings().End;

    /// <summary>Gets the interior colour (<c>IC</c>, PDF 1.4), or <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.9, Table 181: it fills the line endings.</remarks>
    public PdfDeviceColor? InteriorColor => ReadColor(AnnotationNames.IC);

    /// <summary>Gets the measure dictionary giving the scale and units of the annotation (<c>Measure</c>, PDF 1.7), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.9, Table 181, and §12.9, Table 266.</remarks>
    public CosDictionary? Measure => ReadDictionary(AnnotationNames.Measure);
}
