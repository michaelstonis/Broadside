using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A circle annotation: an ellipse inscribed in the annotation rectangle (inset by <see cref="RectangleDifferences"/>).</summary>
/// <remarks>ISO 32000-2 §12.5.6.8, Table 180 (PDF 1.3). The border is <see cref="PdfAnnotation.Border"/>.</remarks>
public sealed class PdfCircleAnnotation : PdfMarkupAnnotation
{
    internal PdfCircleAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Circle)
    {
    }

    /// <summary>Gets the interior colour (<c>IC</c>, PDF 1.4), or <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.8, Table 180: it fills the shape.</remarks>
    public PdfDeviceColor? InteriorColor => ReadColor(AnnotationNames.IC);

    /// <summary>Gets the border effect (<c>BE</c>, PDF 1.5), or <see langword="null"/> for none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.8, Table 180, and §12.5.4, Table 169.</remarks>
    public PdfBorderEffect? BorderEffect => ReadBorderEffect();

    /// <summary>Gets the differences between the annotation rectangle and the drawn rectangle (<c>RD</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.8, Table 180.</remarks>
    public PdfRectangleDifferences? RectangleDifferences => ReadRectangleDifferences();
}
