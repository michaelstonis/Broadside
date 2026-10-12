using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A printer's mark annotation: a production symbol such as a registration target, colour bar or cut mark.</summary>
/// <remarks>ISO 32000-2 §12.5.6.20 and §14.11.3, Table 398 (PDF 1.4).</remarks>
public sealed class PdfPrinterMarkAnnotation : PdfAnnotation
{
    internal PdfPrinterMarkAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.PrinterMark)
    {
    }

    /// <summary>Gets the name of the kind of mark (<c>MN</c>), such as <c>ColorBar</c> or <c>RegistrationTarget</c>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.11.3, Table 398.</remarks>
    public CosName? MarkName => ReadName(AnnotationNames.MN);
}
