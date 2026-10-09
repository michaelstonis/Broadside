using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A file attachment annotation: a file embedded in or referred to by the page, shown as an icon.</summary>
/// <remarks>ISO 32000-2 §12.5.6.15, Table 187 (PDF 1.3). Its description is <see cref="PdfAnnotation.Contents"/>, not the file specification's.</remarks>
public sealed class PdfFileAttachmentAnnotation : PdfMarkupAnnotation
{
    internal PdfFileAttachmentAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.FileAttachment)
    {
    }

    /// <summary>Gets the file associated with the annotation (<c>FS</c>, required), or <see langword="null"/> when absent or not a file specification.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.15, Table 187, and §7.11.</remarks>
    public PdfFileSpecification? FileSpecification
    {
        get
        {
            if (!Dictionary.TryGetValue(AnnotationNames.FS, out CosObject? value) || Document.Resolve(value) is CosNull)
            {
                ReportMissing(AnnotationNames.FS, "Table 187");
                return null;
            }

            return Document.GetFileSpecification(value);
        }
    }

    /// <summary>Gets the name of the icon (<c>Name</c>): <c>Graph</c>, <c>PushPin</c> (the default), <c>Paperclip</c>, <c>Tag</c> or another name.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.15, Table 187.</remarks>
    public string IconName => ReadIconName("PushPin");
}
