using Broadside.Structure;

namespace Broadside.Layout;

/// <summary>
/// A figure of a layout tree: an image or drawing, with the text inside it (labels, or the invisible text of a recognised scan)
/// as its own text and its caption, when it has one, as a <see cref="LayoutNodeKind.Caption"/> child (first when it is above the
/// figure, last when below). Formulas are figures too (<see cref="LayoutNodeKind.Figure"/>).
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.4.8.5, Tables 373 and 374 (<c>Figure</c>, <c>Formula</c>), §14.8.4.8.4, Table 372 (<c>Caption</c>), and §14.8.5.4.3 and
/// Table 379 (the <c>BBox</c> layout attribute, which <see cref="BoundingBox"/> gives).
/// </remarks>
public sealed class LayoutFigure : LayoutNode
{
    private readonly PdfRectangle? _boundingBox;

    internal LayoutFigure(PdfStructureElement? element, PdfRectangle? boundingBox)
        : base(LayoutNodeKind.Figure, element)
    {
        _boundingBox = boundingBox;
    }

    /// <summary>
    /// Gets the box enclosing the figure's graphic on its page, in default user space: for a detected figure, its images, paths
    /// and the text inside them, without the caption; for a tagged one, its element's <c>BBox</c> attribute, else the bounds of
    /// its first extent. <see langword="null"/> when a tagged figure has neither.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.5.4.3, Table 379 (<c>BBox</c>: required for <c>Figure</c>, <c>Formula</c> and <c>Table</c> in PDF/UA).</remarks>
    public PdfRectangle? BoundingBox => _boundingBox ?? (Extents.Count > 0 ? Extents[0].Bounds : null);
}
