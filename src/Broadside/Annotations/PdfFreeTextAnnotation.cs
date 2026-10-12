using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A free text annotation: text displayed directly on the page, without a pop-up window.</summary>
/// <remarks>ISO 32000-2 §12.5.6.6, Table 177 (PDF 1.3). The default appearance string and the rich text are exposed as text, not parsed.</remarks>
public sealed class PdfFreeTextAnnotation : PdfMarkupAnnotation
{
    internal PdfFreeTextAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.FreeText)
    {
    }

    /// <summary>Gets the default appearance string used to format the text (<c>DA</c>, required), or <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.6, Table 177, and §12.7.4.3.</remarks>
    public string? DefaultAppearance
    {
        get
        {
            if (!Dictionary.ContainsKey(AnnotationNames.DA))
            {
                ReportMissing(AnnotationNames.DA, "Table 177");
            }

            return ReadByteString(AnnotationNames.DA);
        }
    }

    /// <summary>Gets the justification of the text (<c>Q</c>, PDF 1.4); default left.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.6, Table 177.</remarks>
    public PdfTextJustification Justification => ReadInteger(AnnotationNames.Q) switch
    {
        1 => PdfTextJustification.Centered,
        2 => PdfTextJustification.Right,
        _ => PdfTextJustification.Left,
    };

    /// <summary>Gets the default style string for the rich text (<c>DS</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.6, Table 177, and §12.7.4.4.</remarks>
    public string? DefaultStyle => ReadText(AnnotationNames.DS);

    /// <summary>Gets the callout line (<c>CL</c>, PDF 1.6): two or three points, from the point the callout points at; empty when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.6, Table 177: used only with the intent <c>FreeTextCallout</c>.</remarks>
    public IReadOnlyList<PathPoint> CalloutLine => ReadPoints(AnnotationNames.CL, required: false, "Table 177");

    /// <summary>Gets the border effect (<c>BE</c>, PDF 1.5), or <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.6, Table 177, and §12.5.4, Table 169.</remarks>
    public PdfBorderEffect? BorderEffect => ReadBorderEffect();

    /// <summary>Gets the differences between the annotation rectangle and the drawn rectangle (<c>RD</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.6, Table 177.</remarks>
    public PdfRectangleDifferences? RectangleDifferences => ReadRectangleDifferences();

    /// <summary>Gets the line ending at the start of the callout line (<c>LE</c>, PDF 1.6); default none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.6, Table 177, and Table 179.</remarks>
    public PdfLineEnding LineEnding => ParseLineEnding(Get(AnnotationNames.LE));
}
