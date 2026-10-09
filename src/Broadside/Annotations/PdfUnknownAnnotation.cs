using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>An annotation of a type Table 171 does not list, or without a subtype: its common entries only.</summary>
/// <remarks>ISO 32000-2 §12.5.2 and §12.5.6.1: the set of subtypes is extensible. <see cref="PdfAnnotation.Subtype"/> gives the raw name; a processor without a handler for it displays the normal appearance unless the Invisible flag is set (§12.5.3).</remarks>
public sealed class PdfUnknownAnnotation : PdfAnnotation
{
    internal PdfUnknownAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Unknown)
    {
    }
}
