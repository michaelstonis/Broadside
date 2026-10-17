using Broadside.Structure;

namespace Broadside.Layout;

/// <summary>A heading of a layout tree, with its level.</summary>
/// <remarks>ISO 32000-2 §14.8.4.5, Table 366.</remarks>
public sealed class LayoutHeading : LayoutNode
{
    internal LayoutHeading(PdfStructureElement? element, int level)
        : base(LayoutNodeKind.Heading, element)
    {
        Level = level;
    }

    /// <summary>
    /// Gets the heading's level: n for <c>Hn</c>; for <c>H</c>, the number of <c>Sect</c> elements it is nested in within its
    /// <c>Document</c> or <c>DocumentFragment</c>, at least 1; 0 for a <c>Title</c>, the title of a document or high-level division,
    /// above every numbered level.
    /// </summary>
    /// <remarks>ISO 32000-2 Table 366 (<c>Hn</c>, <c>H</c>: "derived from the nesting of the logical structure", <c>Title</c>).</remarks>
    public int Level { get; }
}
