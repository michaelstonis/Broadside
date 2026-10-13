using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Broadside.Caching;
using Broadside.Objects;

namespace Broadside;

/// <summary>The pages of a document, in page order: the leaves of the page tree. Pages are inserted, removed and moved here.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.7.3. The page tree is walked on first use (count, index or enumeration), once, by its <c>Kids</c> arrays; its
/// <c>Count</c> entries are only checked, never trusted (§7.7.3.2 calls them redundant). Each <see cref="PdfPage"/> is a live view over
/// its page object, but the list itself is the tree's shape at the first walk or the last edit made here: a change made to the page
/// tree through the COS layer after that is not seen.
/// </para>
/// <para>
/// Editing (PDF 1.0): <see cref="Insert(int, PdfRectangle)"/>, <see cref="Insert(int, CosReference)"/>, <see cref="RemoveAt"/> and
/// <see cref="Move"/> rebuild the page tree at once, balanced, with every page's inherited attributes (§7.7.3.4) made its own first,
/// so a page keeps its <c>Resources</c>, <c>MediaBox</c>, <c>CropBox</c> and <c>Rotate</c> wherever it moves; the root keeps its
/// object, and intermediate nodes are reused. Views of the pages stay valid across edits. Each edit is a mutation: no read may run
/// concurrently with it.
/// </para>
/// </remarks>
public sealed class PdfPageCollection : IReadOnlyList<PdfPage>
{
    // OnceCache rather than Lazy: a walk of a remote file's tree that misses bytes (issue #207) is not cached as failed.
    private readonly OnceCache<bool, PageList> _read = new();
    private readonly PdfDocument _document;
    private readonly Func<IReadOnlyList<PdfPage>> _walk;
    private PageList? _edited;
    private List<CosReference>? _nodes;
    private HashSet<CosReference>? _removed;

    internal PdfPageCollection(PdfDocument document, Func<IReadOnlyList<PdfPage>> walk)
    {
        _document = document;
        _walk = walk;
    }

    /// <summary>Gets the number of pages.</summary>
    public int Count => Pages.Count;

    /// <summary>Gets the page objects removed through <see cref="RemoveAt"/> or <see cref="Remove"/>, for the save-time report; <see langword="null"/> when none.</summary>
    internal IReadOnlySet<CosReference>? RemovedPages => _removed;

    private PageList Current => _edited ?? _read.GetOrCreate(
        true,
        _walk,
        static (_, walk) => new Created<PageList>(new PageList(walk())),
        static (_, _) => throw new InvalidOperationException("The page tree was read while it was being read."));

    private IReadOnlyList<PdfPage> Pages => Current.Pages;

    /// <summary>Gets the page at a zero-based index.</summary>
    /// <param name="index">The index: 0 for the first page.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public PdfPage this[int index] => Pages[index];

    /// <inheritdoc/>
    public IEnumerator<PdfPage> GetEnumerator() => Pages.GetEnumerator();

    /// <summary>Returns the index of <paramref name="page"/>, or -1 when it is not a page of the document.</summary>
    /// <param name="page">A page view.</param>
    /// <returns>The 0-based index, or -1.</returns>
    /// <remarks>ISO 32000-2 §7.7.3. Pages are matched by their page object.</remarks>
    public int IndexOf(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return IndexOf(page.Dictionary);
    }

    /// <summary>Adds a new, empty page at the end of the document.</summary>
    /// <param name="mediaBox">The page's media box, in default user space units (1/72 inch).</param>
    /// <returns>The new page.</returns>
    /// <remarks>ISO 32000-2 §7.7.3.3 (PDF 1.0). As <see cref="Insert(int, PdfRectangle)"/> at <see cref="Count"/>.</remarks>
    public PdfPage Add(PdfRectangle mediaBox) => Insert(Count, mediaBox);

    /// <summary>Inserts a new, empty page at <paramref name="index"/>.</summary>
    /// <param name="index">The new page's index: 0 to insert before the first page, <see cref="Count"/> to add after the last.</param>
    /// <param name="mediaBox">The page's media box, in default user space units (1/72 inch).</param>
    /// <returns>The new page.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or greater than <see cref="Count"/>.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.7.3.3, Table 31 (PDF 1.0): a new indirect page object <c>&lt;&lt; /Type /Page /MediaBox [...] /Resources
    /// &lt;&lt;&gt;&gt; &gt;&gt;</c> with no <c>Contents</c> (an empty page), placed in the rebuilt page tree.
    /// </remarks>
    public PdfPage Insert(int index, PdfRectangle mediaBox)
    {
        ThrowIfInvalidInsertion(index);
        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = KnownNames.Page,
            [KnownNames.MediaBox] = new CosArray([Number(mediaBox.Left), Number(mediaBox.Bottom), Number(mediaBox.Right), Number(mediaBox.Top)]),
            [KnownNames.Resources] = new CosDictionary(),
        };
        CosReference reference = _document.AddObject(dictionary);
        return InsertPage(index, new PdfPage(_document, dictionary, reference, ancestors: null));
    }

    /// <summary>Adds an existing page object of the document, not yet in the page tree, at the end of the document.</summary>
    /// <param name="pageObject">The page object's reference.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentException"><paramref name="pageObject"/> is not a page object, or is already a page of the document.</exception>
    /// <remarks>ISO 32000-2 §7.7.3.3 (PDF 1.0). As <see cref="Insert(int, CosReference)"/> at <see cref="Count"/>.</remarks>
    public PdfPage Add(CosReference pageObject) => Insert(Count, pageObject);

    /// <summary>Inserts an existing page object of the document, not yet in the page tree, at <paramref name="index"/>.</summary>
    /// <param name="index">The page's index: 0 to insert before the first page, <see cref="Count"/> to add after the last.</param>
    /// <param name="pageObject">
    /// The page object's reference: a dictionary added with <see cref="PdfDocument.AddObject"/>, for instance a page copied from
    /// another document. Its <c>Type</c> shall be <c>Page</c> (it is set when absent).
    /// </param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or greater than <see cref="Count"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pageObject"/> is not a page object, or is already a page of the document.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.7.3.2 and §7.7.3.3 (PDF 1.0). A page appears in the page tree once, so a page of the document is moved with
    /// <see cref="Move"/>, not inserted again. The page's <c>Parent</c> is set; the attributes it does not have are not inherited
    /// from anywhere, so give it its own <c>MediaBox</c> and <c>Resources</c>.
    /// </remarks>
    public PdfPage Insert(int index, CosReference pageObject)
    {
        ArgumentNullException.ThrowIfNull(pageObject);
        ThrowIfInvalidInsertion(index);
        if (_document.Resolve(pageObject) is not CosDictionary dictionary)
        {
            throw new ArgumentException("The reference does not resolve to a dictionary.", nameof(pageObject));
        }

        dictionary.TryGetValue(KnownNames.Type, out CosObject? type);
        if (type is not null ? !KnownNames.Page.Equals(type) : dictionary.ContainsKey(KnownNames.Kids))
        {
            throw new ArgumentException("The dictionary is not a page object (Type /Page).", nameof(pageObject));
        }

        if (IndexOf(dictionary) >= 0)
        {
            throw new ArgumentException("The page object is already a page of the document; move it instead.", nameof(pageObject));
        }

        if (type is null)
        {
            dictionary[KnownNames.Type] = KnownNames.Page;
        }

        return InsertPage(index, new PdfPage(_document, dictionary, pageObject, ancestors: null));
    }

    /// <summary>Removes the page at <paramref name="index"/> from the document.</summary>
    /// <param name="index">The page's index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.7.3 and §7.3.10 (PDF 1.0). The page leaves the page tree and its page object is removed from the document
    /// (<see cref="PdfDocument.RemoveObject"/>), so references to it resolve to null. What only the page used (its content, resources,
    /// annotations) stays in the document until a save with <see cref="PdfSaveOptions.RemoveUnreachableObjects"/> drops it.
    /// </para>
    /// <para>
    /// Destinations that show the page (outline items, link annotations, named destinations) are not changed: each one is reported
    /// in <see cref="PdfDocument.Diagnostics"/> when the document is saved, as <c>DestinationPageRemoved</c> (§12.3.2.2).
    /// </para>
    /// </remarks>
    public void RemoveAt(int index)
    {
        IReadOnlyList<PdfPage> previous = Pages;
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, previous.Count);
        PdfPage page = previous[index];
        var pages = new List<PdfPage>(previous);
        pages.RemoveAt(index);
        Commit(previous, pages);
        if (page.Reference is { } reference && _document.RemoveObject(reference))
        {
            (_removed ??= []).Add(reference);
        }
    }

    /// <summary>Removes <paramref name="page"/> from the document.</summary>
    /// <param name="page">A page of the document.</param>
    /// <returns><see langword="true"/> when the page was removed; <see langword="false"/> when it is not a page of the document.</returns>
    /// <remarks>ISO 32000-2 §7.7.3 (PDF 1.0). As <see cref="RemoveAt"/>.</remarks>
    public bool Remove(PdfPage page)
    {
        int index = IndexOf(page);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    /// <summary>Moves the page at <paramref name="fromIndex"/> so that it is at <paramref name="toIndex"/>.</summary>
    /// <param name="fromIndex">The page's index.</param>
    /// <param name="toIndex">The index the page has after the move; the pages between shift by one.</param>
    /// <exception cref="ArgumentOutOfRangeException">An index is negative or not less than <see cref="Count"/>.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.7.3 (PDF 1.0). The page object stays the same object, so destinations that show it follow it; its inherited
    /// attributes become its own (§7.7.3.4).
    /// </remarks>
    public void Move(int fromIndex, int toIndex)
    {
        IReadOnlyList<PdfPage> previous = Pages;
        ArgumentOutOfRangeException.ThrowIfNegative(fromIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromIndex, previous.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(toIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toIndex, previous.Count);
        if (fromIndex == toIndex)
        {
            return;
        }

        var pages = new List<PdfPage>(previous);
        PdfPage page = pages[fromIndex];
        pages.RemoveAt(fromIndex);
        pages.Insert(toIndex, page);
        Commit(previous, pages);
    }

    /// <summary>Returns the index of the page whose page object is <paramref name="page"/>, or -1 when no page has it.</summary>
    /// <param name="page">A page object, as resolved through the document (resolving returns the same instance every time).</param>
    /// <returns>The 0-based index, or -1.</returns>
    /// <remarks>For destinations (§12.3.2.2) and any other object that refers to a page. Built once per page list.</remarks>
    internal int IndexOf(CosDictionary page) => Current.Indexes.TryGetValue(page, out int index) ? index : -1;

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Finds the page whose page object is <paramref name="pageObject"/> (by identity), for references such as a structure element's <c>Pg</c>.</summary>
    internal bool TryGetPage(CosDictionary pageObject, [MaybeNullWhen(false)] out PdfPage page)
    {
        PageList current = Current;
        if (current.Indexes.TryGetValue(pageObject, out int index))
        {
            page = current.Pages[index];
            return true;
        }

        page = null;
        return false;
    }

    private static CosNumber Number(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < int.MaxValue ? new CosInteger((long)value) : new CosReal(value);

    private void ThrowIfInvalidInsertion(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Count);
    }

    private PdfPage InsertPage(int index, PdfPage page)
    {
        IReadOnlyList<PdfPage> previous = Pages;
        var pages = new List<PdfPage>(previous);
        pages.Insert(index, page);
        Commit(previous, pages);
        return page;
    }

    /// <summary>Rebuilds the page tree around <paramref name="pages"/> and makes them the list.</summary>
    private void Commit(IReadOnlyList<PdfPage> previous, List<PdfPage> pages)
    {
        _nodes = PageTreeEditor.Rebuild(_document, previous, pages, _nodes);
        _edited = new PageList(pages.ToArray());
    }

    /// <summary>One version of the page list, with its index by page object built on first use.</summary>
    private sealed class PageList(IReadOnlyList<PdfPage> pages)
    {
        private Dictionary<CosDictionary, int>? _indexes;

        public IReadOnlyList<PdfPage> Pages => pages;

        public Dictionary<CosDictionary, int> Indexes
        {
            get
            {
                Dictionary<CosDictionary, int>? indexes = Volatile.Read(ref _indexes);
                if (indexes is null)
                {
                    indexes = new Dictionary<CosDictionary, int>(pages.Count, ReferenceEqualityComparer.Instance);
                    for (int index = 0; index < pages.Count; index++)
                    {
                        indexes.TryAdd(pages[index].Dictionary, index);
                    }

                    // Concurrent readers may build it twice; the lists are equal and one wins.
                    indexes = Interlocked.CompareExchange(ref _indexes, indexes, null) ?? indexes;
                }

                return indexes;
            }
        }
    }
}
