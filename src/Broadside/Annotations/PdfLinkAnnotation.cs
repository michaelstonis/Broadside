using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A link annotation: a hypertext link to a destination or an action.</summary>
/// <remarks>ISO 32000-2 §12.5.6.5, Table 176 (PDF 1.0). A link needs no appearance stream (§12.5.2). <c>Dest</c> is not permitted with <c>A</c>; when a file has both, both are exposed, <c>LinkActionAndDest</c> is recorded, and <see cref="Action"/> is the one to perform.</remarks>
public sealed class PdfLinkAnnotation : PdfAnnotation
{
    internal PdfLinkAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Link)
    {
    }

    /// <summary>Gets the action performed when the link is activated (<c>A</c>, PDF 1.1), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176, and §12.6.</remarks>
    public PdfAction? Action
    {
        get
        {
            CheckActionAndDest();
            return ReadAction(AnnotationNames.A);
        }
    }

    /// <summary>Gets the destination displayed when the link is activated (<c>Dest</c>): explicit or named; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176, and §12.3.2.</remarks>
    public PdfDestination? Destination
    {
        get
        {
            CheckActionAndDest();
            return Dictionary.TryGetValue(AnnotationNames.Dest, out CosObject? value) ? PdfDestination.Create(Document, value, isRemote: false, DiagnosticReference) : null;
        }
    }

    /// <summary>Gets the visual effect when the link is activated (<c>H</c>, PDF 1.2); default invert.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176.</remarks>
    public PdfHighlightMode HighlightMode => ReadChoice(AnnotationNames.H, PdfHighlightMode.Invert, ("N", PdfHighlightMode.None), ("I", PdfHighlightMode.Invert), ("O", PdfHighlightMode.Outline), ("P", PdfHighlightMode.Push));

    /// <summary>Gets the URI action formerly associated with the link, for reordering the actions of a link (<c>PA</c>, PDF 1.3), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176.</remarks>
    public PdfAction? PreviousUriAction => ReadAction(AnnotationNames.PA);

    /// <summary>Gets the quadrilaterals of the link's region (<c>QuadPoints</c>, PDF 1.6), as stored; empty when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176.</remarks>
    public IReadOnlyList<PdfQuadrilateral> QuadPoints => ReadQuadPoints(required: false, "Table 176");

    /// <summary>Gets the region that activates the link: <see cref="QuadPoints"/> when present and inside the rectangle, else the rectangle as one quadrilateral.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176: when QuadPoints is absent or any of its coordinates lie outside Rect, Rect is used.</remarks>
    public IReadOnlyList<PdfQuadrilateral> ActivationRegion
    {
        get
        {
            PdfRectangle rect = Rect;
            IReadOnlyList<PdfQuadrilateral> quads = QuadPoints;
            bool inside = quads.Count > 0;
            foreach (PdfQuadrilateral quad in quads)
            {
                PdfRectangle bounds = quad.Bounds;
                inside &= bounds.Left >= rect.Left && bounds.Right <= rect.Right && bounds.Bottom >= rect.Bottom && bounds.Top <= rect.Top;
            }

            return inside
                ? quads
                : [new PdfQuadrilateral(new PathPoint(rect.Left, rect.Bottom), new PathPoint(rect.Right, rect.Bottom), new PathPoint(rect.Right, rect.Top), new PathPoint(rect.Left, rect.Top))];
        }
    }

    private void CheckActionAndDest()
    {
        if (Dictionary.ContainsKey(AnnotationNames.A) && Dictionary.ContainsKey(AnnotationNames.Dest))
        {
            Report(Parsing.DiagnosticCodes.LinkActionAndDest, "The link has both A and Dest, which Table 176 does not permit; the action is the one to perform.");
        }
    }
}
