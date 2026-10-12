namespace Broadside.Structure;

/// <summary>
/// One child of a structure element (an entry of its <c>K</c>): another <see cref="PdfStructureElement"/>, a marked-content sequence
/// (<see cref="PdfMarkedContentReference"/>) or a whole PDF object (<see cref="PdfObjectReference"/>).
/// </summary>
/// <remarks>ISO 32000-2 §14.7.2, Table 355 (<c>K</c>), and §14.7.5.1.1: the last two are content items, the leaves of the tree.</remarks>
public abstract class PdfStructureItem
{
    private protected PdfStructureItem()
    {
    }

    /// <summary>Gets the structure element whose <c>K</c> holds this item, or <see langword="null"/> for a top-level element (a child of the root).</summary>
    public abstract PdfStructureElement? Parent { get; }
}
