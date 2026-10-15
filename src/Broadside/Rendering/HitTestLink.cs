using Broadside.Annotations;

namespace Broadside.Rendering;

/// <summary>A link annotation as a <see cref="HitTestIndex"/> holds it: the annotation, what activating it does, and its region.</summary>
/// <remarks>
/// ISO 32000-2 §12.5.6.5, Table 176: the region is <c>QuadPoints</c> when present and inside <c>Rect</c>, else <c>Rect</c>
/// (<see cref="PdfLinkAnnotation.ActivationRegion"/>), in default user space. A point hits the link when it is inside one of the
/// region's quadrilaterals (their convex hull, so writers' corner orders all work). Links that are hidden (<c>Hidden</c> or
/// <c>NoView</c>, §12.5.3) or whose optional content is off (§8.11.3.3) are not in the index.
/// </remarks>
public sealed class HitTestLink
{
    internal HitTestLink(PdfLinkAnnotation annotation, IReadOnlyList<PdfQuadrilateral> region, PdfRectangle bounds)
    {
        Annotation = annotation;
        Region = region;
        Bounds = bounds;
    }

    /// <summary>Gets the link annotation, a live view over its dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5.</remarks>
    public PdfLinkAnnotation Annotation { get; }

    /// <summary>Gets the action performed when the link is activated, read from the annotation (<c>A</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176, and §12.6.</remarks>
    public PdfAction? Action => Annotation.Action;

    /// <summary>Gets the destination displayed when the link is activated, read from the annotation (<c>Dest</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176, and §12.3.2.</remarks>
    public PdfDestination? Destination => Annotation.Destination;

    /// <summary>Gets the quadrilaterals that activate the link, as when the index was built.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.5, Table 176 (<c>QuadPoints</c>, else <c>Rect</c>).</remarks>
    public IReadOnlyList<PdfQuadrilateral> Region { get; }

    /// <summary>Gets the upright rectangle around <see cref="Region"/>.</summary>
    /// <remarks>ISO 32000-2 §7.9.5.</remarks>
    public PdfRectangle Bounds { get; }
}
