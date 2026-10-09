using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A screen annotation: a region of the page where media clips may be played.</summary>
/// <remarks>ISO 32000-2 §12.5.6.18, Table 190 (PDF 1.5). Media are parse-and-preserve: the annotation's actions are exposed and never performed. Additional actions (<c>AA</c>) are read through <see cref="PdfAnnotation.Dictionary"/> until the action ticket types them.</remarks>
public sealed class PdfScreenAnnotation : PdfAnnotation
{
    internal PdfScreenAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Screen)
    {
    }

    /// <summary>Gets the title of the annotation (<c>T</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.18, Table 190.</remarks>
    public string? ScreenTitle => ReadText(AnnotationNames.T);

    /// <summary>Gets the appearance characteristics dictionary used to construct the appearance (<c>MK</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.18, Table 190, and §12.5.6.19, Table 192.</remarks>
    public PdfAppearanceCharacteristics? AppearanceCharacteristics => ReadDictionary(AnnotationNames.MK) is { } characteristics ? new PdfAppearanceCharacteristics(this, characteristics) : null;

    /// <summary>Gets the action performed when the annotation is activated (<c>A</c>, PDF 1.1), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.18, Table 190, and §12.6.</remarks>
    public PdfAction? Action => ReadAction(AnnotationNames.A);
}
