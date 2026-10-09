using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A rich media annotation: video, audio or interactive content.</summary>
/// <remarks>ISO 32000-2 §12.5.6.25 and §13.7.2, Table 333 (PDF 2.0). Parse-and-preserve (rich media is out of scope): exposed as data, never played.</remarks>
public sealed class PdfRichMediaAnnotation : PdfAnnotation
{
    internal PdfRichMediaAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.RichMedia)
    {
    }

    /// <summary>Gets the rich media content dictionary (<c>RichMediaContent</c>, required), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §13.7.2, Table 333.</remarks>
    public CosDictionary? Content
    {
        get
        {
            if (!Dictionary.ContainsKey(AnnotationNames.RichMediaContent))
            {
                ReportMissing(AnnotationNames.RichMediaContent, "Table 333");
            }

            return ReadDictionary(AnnotationNames.RichMediaContent);
        }
    }

    /// <summary>Gets the rich media settings dictionary (<c>RichMediaSettings</c>), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §13.7.2, Table 333.</remarks>
    public CosDictionary? Settings => ReadDictionary(AnnotationNames.RichMediaSettings);
}
