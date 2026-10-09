using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A watermark annotation: graphics printed at a fixed size and position relative to the media.</summary>
/// <remarks>ISO 32000-2 §12.5.6.22, Table 193 (PDF 1.6). It has no pop-up window or other interactive elements.</remarks>
public sealed class PdfWatermarkAnnotation : PdfAnnotation
{
    internal PdfWatermarkAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Watermark)
    {
    }

    /// <summary>Gets the fixed print dictionary (<c>FixedPrint</c>), or <see langword="null"/> when the annotation is drawn without regard to the media.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.22, Tables 193 and 194.</remarks>
    public PdfFixedPrint? FixedPrint => ReadDictionary(AnnotationNames.FixedPrint) is { } fixedPrint ? new PdfFixedPrint(this, fixedPrint) : null;
}
