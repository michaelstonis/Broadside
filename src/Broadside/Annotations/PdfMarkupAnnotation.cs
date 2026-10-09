using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Annotations;

/// <summary>A markup annotation: one that marks up the document and can carry an author, a pop-up window, replies and a review state.</summary>
/// <remarks>
/// ISO 32000-2 §12.5.6.2, Tables 172 and 173. The markup types are those Table 171 marks: text, free text, line, square, circle,
/// polygon, polyline, the text markup types, caret, stamp, ink, file attachment, sound, redaction and projection.
/// </remarks>
public abstract class PdfMarkupAnnotation : PdfAnnotation
{
    private protected PdfMarkupAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page, PdfAnnotationKind kind)
        : base(document, dictionary, reference, page, kind)
    {
    }

    /// <summary>Gets the text label of the pop-up window's title bar, by convention the author (<c>T</c>, PDF 1.1), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, Table 172.</remarks>
    public string? Title => ReadText(AnnotationNames.T);

    /// <summary>Gets the pop-up annotation for entering or editing the text (<c>Popup</c>, PDF 1.3), or <see langword="null"/>.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.5.6.2, Table 172. The same instance as on the page. When the pop-up's own <c>Parent</c> names another
    /// annotation, <c>PopupLinkMismatch</c> is recorded: this entry says which pop-up belongs to this annotation, the pop-up's
    /// <c>Parent</c> which annotation the pop-up belongs to.
    /// </remarks>
    public PdfPopupAnnotation? Popup
    {
        get
        {
            if (!Dictionary.TryGetValue(AnnotationNames.Popup, out CosObject? value) || Document.Resolve(value) is CosNull)
            {
                return null;
            }

            if (Document.AnnotationIndex.Find(value, this) is not PdfPopupAnnotation popup)
            {
                ReportInvalid(AnnotationNames.Popup, "a popup annotation");
                return null;
            }

            if (popup.Dictionary.TryGetValue(AnnotationNames.Parent, out CosObject? parent)
                && Document.Resolve(parent) is CosDictionary parentDictionary
                && !ReferenceEquals(parentDictionary, Dictionary))
            {
                Report(DiagnosticCodes.PopupLinkMismatch, "The annotation's pop-up names another annotation as its Parent.");
            }

            return popup;
        }
    }

    /// <summary>Gets the rich text shown in the pop-up window (<c>RC</c>, PDF 1.5), as stored (XHTML), or <see langword="null"/>. Not parsed.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, Table 172, and §12.7.4.4: a text string or a text stream.</remarks>
    public string? RichText => ReadTextOrStream(AnnotationNames.RC);

    /// <summary>Gets the date and time the annotation was created (<c>CreationDate</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, Table 172, and §7.9.4.</remarks>
    public PdfDate? CreationDate => ReadDate(AnnotationNames.CreationDate);

    /// <summary>Gets the annotation this one is a reply to or grouped with (<c>IRT</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.5.6.2, Table 172: it shall be on the same page; one on another page records <c>InReplyToOtherPage</c>, a
    /// value that is not an annotation dictionary (or the annotation itself) <c>InReplyToInvalid</c>.
    /// </remarks>
    public PdfAnnotation? InReplyTo
    {
        get
        {
            if (!Dictionary.TryGetValue(AnnotationNames.IRT, out CosObject? value) || Document.Resolve(value) is CosNull)
            {
                return null;
            }

            PdfAnnotation? target = Document.AnnotationIndex.Find(value, this);
            if (target is null || ReferenceEquals(target, this))
            {
                Report(DiagnosticCodes.InReplyToInvalid, "The annotation's IRT entry is not another annotation's dictionary; it is ignored.");
                return null;
            }

            if (Page is not null && !ReferenceEquals(target.Page, Page))
            {
                Report(DiagnosticCodes.InReplyToOtherPage, "The annotation replies to an annotation that is not on its page, which IRT shall name.");
            }

            return target;
        }
    }

    /// <summary>Gets the subject of the annotation (<c>Subj</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, Table 172.</remarks>
    public string? Subject => ReadText(AnnotationNames.Subj);

    /// <summary>Gets how the annotation relates to <see cref="InReplyTo"/> (<c>RT</c>, PDF 1.6): a reply (the default) or a group.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, Table 172: meaningful only with <c>IRT</c>, which is required when <c>RT</c> is present.</remarks>
    public PdfReplyType ReplyType
    {
        get
        {
            if (Dictionary.ContainsKey(AnnotationNames.RT) && !Dictionary.ContainsKey(AnnotationNames.IRT))
            {
                Report(DiagnosticCodes.ReplyTypeWithoutInReplyTo, "The annotation has an RT entry but no IRT entry, which Table 172 requires with it.");
            }

            return ReadChoice(AnnotationNames.RT, PdfReplyType.Reply, ("R", PdfReplyType.Reply), ("Group", PdfReplyType.Group));
        }
    }

    /// <summary>Gets the intent of the annotation (<c>IT</c>, PDF 1.6), such as <c>FreeTextCallout</c>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, Table 172. Absent, or equal to the subtype, means no explicit intent.</remarks>
    public CosName? Intent => ReadName(AnnotationNames.IT);

    /// <summary>Gets the external data dictionary (<c>ExData</c>, PDF 1.7), such as 3D markup, as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, Tables 172 and 173.</remarks>
    public CosDictionary? ExternalData => ReadDictionary(AnnotationNames.ExData);
}
