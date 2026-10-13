using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside;

/// <summary>
/// The document's current outline walk, which every outline view reads its shape from (ISO 32000-2 §12.3.3; ADR 0004): created on
/// first use, walked again when anything the last walk read has changed.
/// </summary>
/// <remarks>
/// A walk is built under a lock, so concurrent readers walk and report once, and published for lock-free reads. The views edits
/// return between two walks are kept for the next walk to hand out again; that list changes only during mutations, which no read
/// runs concurrently with.
/// </remarks>
internal sealed class OutlineViews(PdfDocument document)
{
    private readonly Lock _gate = new();
    private volatile OutlineTree? _tree;
    private Dictionary<CosDictionary, PdfOutlineItem>? _created;

    /// <summary>Returns the current walk of the outline <paramref name="root"/>, walking it again when anything it read has changed.</summary>
    /// <param name="root">The outline dictionary.</param>
    /// <param name="reference">Its reference, or <see langword="null"/> when it is direct.</param>
    /// <returns>The walk.</returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation a new walk finds.</exception>
    public OutlineTree Tree(CosDictionary root, CosReference? reference)
    {
        OutlineTree? tree = _tree;
        if (tree is not null && tree.IsCurrent(root, reference))
        {
            return tree;
        }

        lock (_gate)
        {
            tree = _tree;
            if (tree is null || !tree.IsCurrent(root, reference))
            {
                tree = OutlineTree.Build(document, root, reference, tree, _created);
                _created = null;
                _tree = tree;
            }

            return tree;
        }
    }

    /// <summary>Returns the shape of the item <paramref name="item"/> is the dictionary of, in the outline the catalog names now.</summary>
    /// <param name="item">The item dictionary.</param>
    /// <returns><see cref="OutlineTree.Outside"/> when the document has no outline or its outline does not reach the item.</returns>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation a new walk finds.</exception>
    public OutlineTree.Node Find(CosDictionary item) =>
        document.Catalog.TryGetValue(NavigationNames.Outlines, out CosObject? entry) && document.Resolve(entry) is CosDictionary root
            ? Tree(root, entry as CosReference).Find(item)
            : OutlineTree.Outside;

    /// <summary>Records a view an outline edit returns, so that the next walk hands out the same view for that item.</summary>
    /// <param name="item">The new item's view.</param>
    /// <returns><paramref name="item"/>.</returns>
    public PdfOutlineItem Remember(PdfOutlineItem item)
    {
        (_created ??= new(ReferenceEqualityComparer.Instance))[item.Dictionary] = item;
        return item;
    }
}
