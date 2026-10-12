using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A line annotation: a single straight line, optionally a dimension line with leader lines and a caption.</summary>
/// <remarks>ISO 32000-2 §12.5.6.7, Table 178 (PDF 1.3).</remarks>
public sealed class PdfLineAnnotation : PdfMarkupAnnotation
{
    internal PdfLineAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Line)
    {
    }

    /// <summary>Gets the start point of the line (<c>L</c>, required), in default user space; the origin when <c>L</c> is missing or malformed.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178: the endpoints, or the leader line endpoints when <c>LL</c> is present.</remarks>
    public PathPoint Start => ReadLine().Start;

    /// <summary>Gets the end point of the line (<c>L</c>, required); the origin when <c>L</c> is missing or malformed.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178.</remarks>
    public PathPoint End => ReadLine().End;

    /// <summary>Gets the line ending at the first point (<c>LE</c>, PDF 1.4); default none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178, and Table 179.</remarks>
    public PdfLineEnding StartLineEnding => ReadLineEndings().Start;

    /// <summary>Gets the line ending at the last point (<c>LE</c>, PDF 1.4); default none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178, and Table 179.</remarks>
    public PdfLineEnding EndLineEnding => ReadLineEndings().End;

    /// <summary>Gets the interior colour (<c>IC</c>, PDF 1.4), or <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178: it fills the line endings.</remarks>
    public PdfDeviceColor? InteriorColor => ReadColor(AnnotationNames.IC);

    /// <summary>Gets the length of the leader lines (<c>LL</c>, PDF 1.6); default 0. Positive values extend above the line, negative below.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178.</remarks>
    public double LeaderLineLength => ReadNumber(AnnotationNames.LL, 0);

    /// <summary>Gets the length of the leader line extensions (<c>LLE</c>, PDF 1.6); default 0.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178.</remarks>
    public double LeaderLineExtension => ReadNumber(AnnotationNames.LLE, 0);

    /// <summary>Gets the length of the leader line offset (<c>LLO</c>, PDF 1.7); default 0.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178.</remarks>
    public double LeaderLineOffset => ReadNumber(AnnotationNames.LLO, 0);

    /// <summary>Gets a value indicating whether the text is shown as a caption with the line (<c>Cap</c>, PDF 1.6); default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178.</remarks>
    public bool HasCaption => ReadBoolean(AnnotationNames.Cap, fallback: false);

    /// <summary>Gets where the caption is placed (<c>CP</c>, PDF 1.7); default inline.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178.</remarks>
    public PdfLineCaptionPosition CaptionPosition => ReadChoice(AnnotationNames.CP, PdfLineCaptionPosition.Inline, ("Inline", PdfLineCaptionPosition.Inline), ("Top", PdfLineCaptionPosition.Top));

    /// <summary>Gets the offset of the caption from its position (<c>CO</c>, PDF 1.7); default (0, 0).</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178.</remarks>
    public PathPoint CaptionOffset
    {
        get
        {
            IReadOnlyList<PathPoint> offset = ReadPoints(AnnotationNames.CO, required: false, "Table 178");
            return offset.Count == 1 ? offset[0] : new PathPoint(0, 0);
        }
    }

    /// <summary>Gets the measure dictionary giving the scale and units of the annotation (<c>Measure</c>, PDF 1.7), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.7, Table 178, and §12.9, Table 266.</remarks>
    public CosDictionary? Measure => ReadDictionary(AnnotationNames.Measure);

    private (PathPoint Start, PathPoint End) ReadLine()
    {
        IReadOnlyList<PathPoint> points = ReadPoints(AnnotationNames.L, required: true, "Table 178");
        if (points.Count == 2)
        {
            return (points[0], points[1]);
        }

        if (points.Count != 0)
        {
            ReportInvalid(AnnotationNames.L, "an array of four numbers");
        }

        return (new PathPoint(0, 0), new PathPoint(0, 0));
    }
}
