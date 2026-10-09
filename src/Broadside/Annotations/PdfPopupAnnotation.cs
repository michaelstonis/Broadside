using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A pop-up annotation: the window that displays the text of its parent markup annotation.</summary>
/// <remarks>ISO 32000-2 §12.5.6.14, Table 186 (PDF 1.3). It has no appearance stream or actions of its own; the parent's <c>Contents</c>, <c>M</c>, <c>C</c> and <c>T</c> are the ones to display.</remarks>
public sealed class PdfPopupAnnotation : PdfAnnotation
{
    internal PdfPopupAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Popup)
    {
    }

    /// <summary>Gets the annotation this pop-up belongs to: its <c>Parent</c>, or, when absent, the markup annotation of the same page whose <c>Popup</c> names it; <see langword="null"/> when there is none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.14, Table 186. A parent that is not a markup annotation records <c>PopupParentInvalid</c>; one whose <c>Popup</c> names another pop-up, <c>PopupLinkMismatch</c>.</remarks>
    public PdfAnnotation? Parent
    {
        get
        {
            if (Dictionary.TryGetValue(AnnotationNames.Parent, out CosObject? value) && Document.Resolve(value) is not CosNull)
            {
                PdfAnnotation? parent = Document.AnnotationIndex.Find(value, this);
                if (parent is null || ReferenceEquals(parent, this))
                {
                    ReportInvalid(AnnotationNames.Parent, "an annotation dictionary");
                    return null;
                }

                if (parent is not PdfMarkupAnnotation)
                {
                    Report(Parsing.DiagnosticCodes.PopupParentInvalid, "The pop-up's Parent is not a markup annotation.");
                }
                else if (parent.Dictionary.TryGetValue(AnnotationNames.Popup, out CosObject? popup)
                    && Document.Resolve(popup) is CosDictionary popupDictionary
                    && !ReferenceEquals(popupDictionary, Dictionary))
                {
                    Report(Parsing.DiagnosticCodes.PopupLinkMismatch, "The pop-up's Parent names another annotation as its Popup.");
                }

                return parent;
            }

            if (Page is null)
            {
                return null;
            }

            foreach (PdfAnnotation candidate in Page.Annotations)
            {
                if (candidate is PdfMarkupAnnotation
                    && candidate.Dictionary.TryGetValue(AnnotationNames.Popup, out CosObject? popup)
                    && ReferenceEquals(Document.Resolve(popup), Dictionary))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    /// <summary>Gets a value indicating whether the pop-up is initially displayed open (<c>Open</c>); default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.14, Table 186.</remarks>
    public bool IsOpen => ReadBoolean(AnnotationNames.Open, fallback: false);
}
