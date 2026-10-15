namespace Broadside.Layout;

/// <summary>
/// The logical content of a document or a page: a tree of sections, paragraphs, headings, lists, tables, figures and their inline
/// content in reading order, each node placed on its pages with its bounds and the range of their text it holds.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.4 and ISO/TS 32005. For a tagged file the tree comes from the structure tree (§14.7): each structure element
/// whose type resolves through the role maps to a standard structure type becomes the node kind <see cref="LayoutNodeKind"/> lists,
/// in the order of the hierarchy, which is logical content order (§14.8.2.5.1). Pages that are not tagged contribute no nodes.
/// </para>
/// <para>
/// Immutable once built and safe to share between threads; it does not change when the document does. Build a new one after an
/// edit.
/// </para>
/// </remarks>
public sealed class LayoutTree
{
    internal LayoutTree(LayoutNode root, LayoutPage[] pages)
    {
        Root = root;
        Pages = pages;
    }

    /// <summary>Gets the root, a <see cref="LayoutNodeKind.Document"/> node.</summary>
    /// <remarks>
    /// ISO 32000-2 §14.8.4.3. When the structure tree has a single top-level element of type <c>Document</c>, the root stands for
    /// it (<see cref="LayoutNode.StructureElement"/>); otherwise the top-level elements are its children.
    /// </remarks>
    public LayoutNode Root { get; }

    /// <summary>Gets the pages the tree was built from, in document order.</summary>
    public IReadOnlyList<LayoutPage> Pages { get; }
}
