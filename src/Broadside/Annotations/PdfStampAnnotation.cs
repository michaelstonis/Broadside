using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A rubber stamp annotation: text or graphics that look as if stamped on the page.</summary>
/// <remarks>ISO 32000-2 §12.5.6.12, Table 184 (PDF 1.3).</remarks>
public sealed class PdfStampAnnotation : PdfMarkupAnnotation
{
    internal PdfStampAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Stamp)
    {
    }

    /// <summary>Gets the name of the stamp's icon (<c>Name</c>), such as <c>Approved</c> or <c>Draft</c> (the default); <see langword="null"/> when the intent is <c>StampImage</c> or <c>StampSnapshot</c> and no name is given.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.12, Table 184: <c>Name</c> shall not be present when <c>IT</c> is present and not <c>Stamp</c> (PDF 2.0).</remarks>
    public string? IconName
    {
        get
        {
            CosName? name = ReadName(AnnotationNames.Name);
            if (name is not null)
            {
                return name.Value;
            }

            return Intent is { } intent && intent.Value != "Stamp" ? null : "Draft";
        }
    }
}
