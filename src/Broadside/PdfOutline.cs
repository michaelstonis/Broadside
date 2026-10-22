using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside;

/// <summary>The document outline (bookmarks): a tree of outline items. A live view over the outline dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.3.3, Table 150. <see cref="Items"/> walks the outline: each level's items in the order of their
/// <c>First</c>/<c>Next</c> chain, iteratively, with one visited set for the whole outline and a depth cap of 256 levels, so a
/// cycle (a <c>Next</c> back to an earlier sibling, a <c>First</c> back to an ancestor) or a very deep chain ends. The walk is kept
/// until a dictionary it read changes, and every item view (<see cref="PdfOutlineItem.Children"/>, <see cref="PdfOutlineItem.Parent"/>,
/// <see cref="PdfOutlineItem.Level"/>) reads its shape from the current walk, so an edit through any view or straight to the COS
/// objects shows in every view at the next read.
/// </para>
/// <para>
/// The walk checks the outline as it goes and reports, once per item: an item reached twice (<c>OutlineCycle</c>, Error), a level
/// deeper than the cap, a <c>Last</c>, <c>Prev</c> or <c>Parent</c> that disagrees with the chain (the chain wins), a missing or
/// non-string <c>Title</c> (read as empty), an item that is not a dictionary, a malformed <c>C</c> or <c>F</c>, both <c>Dest</c> and
/// <c>A</c>, an item with children but no non-zero <c>Count</c> (read as closed), a negative outline <c>Count</c>, and the
/// diagnostics of each item's destination and action, once per walk. In strict mode the walk throws the first of these.
/// </para>
/// </remarks>
public sealed class PdfOutline
{
    /// <summary>The deepest outline level the walk descends to.</summary>
    internal const int MaxDepth = 256;

    private readonly PdfDocument _document;

    internal PdfOutline(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        _document = document;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the outline dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 150.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the outline dictionary, or <see langword="null"/> when it is direct.</summary>
    public CosReference? Reference { get; private set; }

    /// <summary>Gets the outline's <c>Count</c> as stored: the number of visible items at all levels; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 150.</remarks>
    public int? Count => PdfOutlineItem.ReadCount(_document, Dictionary);

    /// <summary>Gets the outline's top-level items, each with its children.</summary>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation the walk finds.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.3. The items as the outline's links are now: the outline is walked again when a dictionary the last walk
    /// read has changed (through this API or straight in the COS objects), else the last walk is reused and reading allocates nothing.
    /// The items are live views; an item stays the same view while it stays in the outline.
    /// </remarks>
    public IReadOnlyList<PdfOutlineItem> Items => _document.OutlineViews.Tree(Dictionary, Reference).Items;

    /// <summary>Adds a top-level item titled <paramref name="title"/> after the last one.</summary>
    /// <param name="title">The text displayed for the item.</param>
    /// <returns>The new item, an indirect object with no destination yet.</returns>
    /// <remarks>See <see cref="Insert"/>.</remarks>
    public PdfOutlineItem Add(string title) => InsertAt(null, title);

    /// <summary>Inserts a top-level item titled <paramref name="title"/> at <paramref name="index"/>.</summary>
    /// <param name="index">The position among the top-level items, from 0 to their number.</param>
    /// <param name="title">The text displayed for the item.</param>
    /// <returns>The new item, an indirect object with no destination yet.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or past the end of the level.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.3, Tables 150 and 151. The item is linked to its neighbours through <c>Prev</c> and <c>Next</c> and to the
    /// outline through <c>Parent</c>, <c>First</c> and <c>Last</c>, and the outline's <c>Count</c> grows by one. The positions are
    /// those of the outline's links now, not of an earlier <see cref="Items"/> walk. A mutation: no read may run concurrently with it.
    /// </remarks>
    public PdfOutlineItem Insert(int index, string title)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return InsertAt(index, title);
    }

    private PdfOutlineItem InsertAt(int? index, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        OutlineEditor.Link link = OutlineEditor.Insert(_document, Dictionary, EnsureIndirect(), index, title);
        return _document.OutlineViews.Remember(new PdfOutlineItem(_document, link.Item, link.Reference));
    }

    /// <summary>Returns the outline's reference, making the outline dictionary an indirect object when the catalog holds it directly.</summary>
    internal CosReference EnsureIndirect()
    {
        if (Reference is null)
        {
            Reference = _document.AddObject(Dictionary);
            _document.Catalog[NavigationNames.Outlines] = Reference;
        }

        return Reference;
    }
}
