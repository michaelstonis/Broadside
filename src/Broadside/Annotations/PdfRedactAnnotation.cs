using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A redaction annotation: content marked for removal, and how the removed region looks afterwards.</summary>
/// <remarks>ISO 32000-2 §12.5.6.23, Table 195 (PDF 1.7). Reading never applies the redaction.</remarks>
public sealed class PdfRedactAnnotation : PdfMarkupAnnotation
{
    internal PdfRedactAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Redact)
    {
    }

    /// <summary>Gets the quadrilaterals of the content to remove (<c>QuadPoints</c>), as stored; empty when absent, in which case the rectangle is the region.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23, Table 195.</remarks>
    public IReadOnlyList<PdfQuadrilateral> QuadPoints => ReadQuadPoints(required: false, "Table 195");

    /// <summary>Gets the interior colour (<c>IC</c>, PDF 1.4), or <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23, Table 195: the fill of the redacted region (DeviceRGB); ignored when <see cref="Overlay"/> is present.</remarks>
    public PdfDeviceColor? InteriorColor => ReadColor(AnnotationNames.IC);

    /// <summary>Gets the form XObject drawn over the redacted region (<c>RO</c>), or <see langword="null"/>; it takes precedence over the colour, text and justification entries.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23, Table 195.</remarks>
    public PdfFormXObject? Overlay => ReadForm(AnnotationNames.RO);

    /// <summary>Gets the text drawn over the redacted region (<c>OverlayText</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23, Table 195.</remarks>
    public string? OverlayText => ReadText(AnnotationNames.OverlayText);

    /// <summary>Gets a value indicating whether the overlay text repeats to fill the region (<c>Repeat</c>); default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23, Table 195.</remarks>
    public bool RepeatsOverlayText => ReadBoolean(AnnotationNames.Repeat, fallback: false);

    /// <summary>Gets the default appearance string of the overlay text (<c>DA</c>, required with <c>OverlayText</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23, Table 195, and §12.7.4.3.</remarks>
    public string? DefaultAppearance
    {
        get
        {
            if (!Dictionary.ContainsKey(AnnotationNames.DA) && Dictionary.ContainsKey(AnnotationNames.OverlayText))
            {
                ReportMissing(AnnotationNames.DA, "Table 195 (with OverlayText)");
            }

            return ReadByteString(AnnotationNames.DA);
        }
    }

    /// <summary>Gets the justification of the text (<c>Q</c>, PDF 1.4); default left.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23, Table 195.</remarks>
    public PdfTextJustification Justification => ReadInteger(AnnotationNames.Q) switch
    {
        1 => PdfTextJustification.Centered,
        2 => PdfTextJustification.Right,
        _ => PdfTextJustification.Left,
    };
}
