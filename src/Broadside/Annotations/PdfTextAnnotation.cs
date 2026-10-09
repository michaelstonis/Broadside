using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A text annotation: a sticky note attached to a point on the page, shown as an icon.</summary>
/// <remarks>ISO 32000-2 §12.5.6.4, Table 175 (PDF 1.0). It behaves as if the NoZoom and NoRotate flags were always set. A reply with <see cref="State"/> and <see cref="StateModel"/> records a review state of the annotation it replies to (§12.5.6.3, Table 174).</remarks>
public sealed class PdfTextAnnotation : PdfMarkupAnnotation
{
    internal PdfTextAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Text)
    {
    }

    /// <summary>Gets a value indicating whether the annotation is initially displayed open (<c>Open</c>); default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.4, Table 175.</remarks>
    public bool IsOpen => ReadBoolean(AnnotationNames.Open, fallback: false);

    /// <summary>Gets the name of the icon (<c>Name</c>): <c>Comment</c>, <c>Key</c>, <c>Note</c> (the default), <c>Help</c>, <c>NewParagraph</c>, <c>Paragraph</c>, <c>Insert</c> or another name.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.4, Table 175.</remarks>
    public string IconName => ReadIconName("Note");

    /// <summary>Gets the state this annotation sets on the one it replies to (<c>State</c>, PDF 1.5), such as <c>Accepted</c>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.4, Table 175, and §12.5.6.3, Table 174. Without a <c>StateModel</c>, which is then required, <c>StateModelMissing</c> is recorded.</remarks>
    public string? State
    {
        get
        {
            string? state = ReadText(AnnotationNames.State);
            if (state is not null && !Dictionary.ContainsKey(AnnotationNames.StateModel))
            {
                Report(Parsing.DiagnosticCodes.StateModelMissing, "The text annotation has a State but no StateModel, which Table 175 requires with it.");
            }

            return state;
        }
    }

    /// <summary>Gets the state model <see cref="State"/> belongs to (<c>StateModel</c>, PDF 1.5): <c>Marked</c>, <c>Review</c> or another, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.4, Table 175, and §12.5.6.3, Table 174.</remarks>
    public string? StateModel => ReadText(AnnotationNames.StateModel);
}
