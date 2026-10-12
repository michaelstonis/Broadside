using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A sound annotation: a sound recorded or attached at a point on the page. Deprecated in PDF 2.0.</summary>
/// <remarks>ISO 32000-2 §12.5.6.16, Table 188 (PDF 1.2). Parse-and-preserve: the sound is exposed as data and never played.</remarks>
public sealed class PdfSoundAnnotation : PdfMarkupAnnotation
{
    internal PdfSoundAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Sound)
    {
    }

    /// <summary>Gets the sound stream (<c>Sound</c>, required), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.16, Table 188, and §13.3.</remarks>
    public CosStream? Sound
    {
        get
        {
            if (!Dictionary.ContainsKey(AnnotationNames.Sound))
            {
                ReportMissing(AnnotationNames.Sound, "Table 188");
            }

            return ReadStream(AnnotationNames.Sound);
        }
    }

    /// <summary>Gets the name of the icon (<c>Name</c>): <c>Speaker</c> (the default), <c>Mic</c> or another name.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.16, Table 188.</remarks>
    public string IconName => ReadIconName("Speaker");
}
