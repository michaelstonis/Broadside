using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A trap network annotation: the trapping of a page. Deprecated in PDF 2.0.</summary>
/// <remarks>ISO 32000-2 §12.5.6.21 and §14.11.6.2, Table 403 (PDF 1.3). A page shall have at most one, and it shall be the last of its annotations (<c>TrapNetPlacementInvalid</c>). The entries are exposed as data.</remarks>
public sealed class PdfTrapNetAnnotation : PdfAnnotation
{
    internal PdfTrapNetAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.TrapNet)
    {
    }

    /// <summary>Gets the date the trap network was last modified (<c>LastModified</c>), or <see langword="null"/>; required when <c>Version</c> and <c>AnnotStates</c> are absent.</summary>
    /// <remarks>ISO 32000-2 §14.11.6.2, Table 403.</remarks>
    public PdfDate? LastModified
    {
        get
        {
            if (!Dictionary.ContainsKey(AnnotationNames.LastModified) && !(Dictionary.ContainsKey(AnnotationNames.Version) && Dictionary.ContainsKey(AnnotationNames.AnnotStates)))
            {
                ReportMissing(AnnotationNames.LastModified, "Table 403 (without Version and AnnotStates)");
            }

            return ReadDate(AnnotationNames.LastModified);
        }
    }

    /// <summary>Gets the objects the trap network depends on (<c>Version</c>), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.11.6.2, Table 403.</remarks>
    public CosArray? Version => ReadArray(AnnotationNames.Version);

    /// <summary>Gets the appearance states of the page's annotations when the trap network was generated (<c>AnnotStates</c>), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.11.6.2, Table 403.</remarks>
    public CosArray? AnnotationStates => ReadArray(AnnotationNames.AnnotStates);

    /// <summary>Gets the fonts substituted during trapping (<c>FontFauxing</c>), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.11.6.2, Table 403.</remarks>
    public CosArray? FontFauxing => ReadArray(AnnotationNames.FontFauxing);
}
