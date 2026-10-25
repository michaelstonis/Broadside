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
/// <para>
/// A linearized file opened through range requests (<see cref="PdfEngine.OpenAsync(HttpClient, Uri, CancellationToken)"/>)
/// finds its pages through its page offset hint table instead (ISO 32000-2 F.4.2 and Annex G, G.4; issue #208): the count is the
/// linearization dictionary's <c>N</c>, and each page's object is read when the page is first asked for, so going to a page fetches
/// that page and not the tree. Finding a page's index (<see cref="IndexOf(PdfPage)"/>, a destination's page, a structure element's
/// <c>Pg</c>) comes from the hints too, and enumerating goes through the hinted pages one by one. The page tree is still read, on
/// first use, by editing or when a page the hints do not locate (reported as <c>LinearizationHintsInvalid</c>; the pages then come
/// from the tree, the views already handed out included).
/// </para>
/// <para>
/// Where synchronous fetching is off (<see cref="PdfOptions.SynchronousRangeFetch"/>, always on the browser), reading a page whose
/// bytes have not arrived throws <see cref="PdfRangeNotLoadedException"/>: <see cref="PrefetchAsync(int, CancellationToken)"/> gets
/// one page, and <see cref="PdfDocument.ReadAsync{T}"/> runs any read that needs more of the file (every page, for instance),
/// fetching what it misses.
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
    private HintedPageList? _hinted;
    private volatile bool _hintsRejected;

    internal PdfPageCollection(PdfDocument document, Func<IReadOnlyList<PdfPage>> walk)
    {
        _document = document;
        _walk = walk;
    }

    /// <summary>Gets the number of pages.</summary>
    public int Count => HintedCount ?? Pages.Count;

    /// <summary>Gets a value indicating whether the pages are found through the hint tables rather than the page tree.</summary>
    internal bool UsesHints => HintedCount is not null;

    /// <summary>The hinted page count while the hints are in use: before the tree is read or edited, and while no page failed.</summary>
    private int? HintedCount => _hinted is { } hinted && _edited is null && !_hintsRejected && !_read.TryGet(true, out _) ? hinted.Count : null;

    /// <summary>Gets the page objects removed through <see cref="RemoveAt"/> or <see cref="Remove"/>, for the save-time report; <see langword="null"/> when none.</summary>
    internal IReadOnlySet<CosReference>? RemovedPages => _removed;

    private PageList Current => _edited ?? _read.GetOrCreate(
        true,
        this,
        static (_, self) => new Created<PageList>(new PageList(self.Walk())),
        static (_, _) => throw new InvalidOperationException("The page tree was read while it was being read."));

    private IReadOnlyList<PdfPage> Pages => Current.Pages;

    /// <summary>Gets the page at a zero-based index.</summary>
    /// <param name="index">The index: 0 for the first page.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public PdfPage this[int index]
    {
        get
        {
            if (HintedCount is int count)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
                if (_hinted!.Get(index) is { } page)
                {
                    return page;
                }

                _hintsRejected = true;
            }

            return Pages[index];
        }
    }

    /// <inheritdoc/>
    public IEnumerator<PdfPage> GetEnumerator() => UsesHints ? EnumerateByIndex() : Pages.GetEnumerator();

    /// <summary>
    /// Gets the page at <paramref name="index"/> once the objects reading it needs are cached: for a remote document, its page object
    /// (through the page offset hints of a linearized file, else the page tree) and then what <see cref="PdfPage.PrefetchAsync"/>
    /// fetches.
    /// </summary>
    /// <param name="index">The index: 0 for the first page.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>The page; already completed for a document whose bytes are all at hand.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    /// <exception cref="HttpRequestException">The server answered with an error status, or could not be reached.</exception>
    /// <exception cref="IOException">The server's answers are inconsistent: the file changed while it was read, for instance.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>
    /// Spec #33, viewer capability 5; ISO 32000-2 Annex G, G.4 (going to another page of an open document). Where
    /// <see cref="PdfOptions.SynchronousRangeFetch"/> is off (the browser), asking a linearized remote document for a page it has
    /// not fetched throws <see cref="PdfRangeNotLoadedException"/>; this is the asynchronous way to get it.
    /// </remarks>
    public Task<PdfPage> PrefetchAsync(int index, CancellationToken cancellationToken = default) => _document.PrefetchPageAsync(index, cancellationToken);

    /// <summary>Uses the hint tables of a linearized remote file to find pages (issue #208).</summary>
    /// <param name="hinted">The hinted pages.</param>
    internal void UseHints(HintedPageList hinted) => _hinted = hinted;

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

    /// <summary>Adds page objects copied into the document, not yet in the page tree, after the last page, rebuilding the tree once.</summary>
    /// <param name="pageObjects">References to page dictionaries of the document, such as <see cref="PdfObjectCopier.CopyPage"/> returns.</param>
    /// <remarks>ISO 32000-2 §7.7.3.3. For merging (issue #160): one rebuild for every page appended, where <see cref="Add(CosReference)"/> rebuilds per page.</remarks>
    internal void AddRange(IReadOnlyList<CosReference> pageObjects)
    {
        IReadOnlyList<PdfPage> previous = Pages;
        var pages = new List<PdfPage>(previous.Count + pageObjects.Count);
        pages.AddRange(previous);
        foreach (CosReference reference in pageObjects)
        {
            var dictionary = (CosDictionary)_document.Resolve(reference);
            dictionary[KnownNames.Type] = KnownNames.Page;
            pages.Add(new PdfPage(_document, dictionary, reference, ancestors: null));
        }

        Commit(previous, pages);
    }

    /// <summary>Returns the index of the page whose page object is <paramref name="page"/>, or -1 when no page has it.</summary>
    /// <param name="page">A page object, as resolved through the document (resolving returns the same instance every time).</param>
    /// <returns>The 0-based index, or -1.</returns>
    /// <remarks>
    /// For destinations (§12.3.2.2) and any other object that refers to a page. Built once per page list. While the hint tables
    /// locate the pages (a linearized remote file), the page is found from them without reading the page tree, which would need the
    /// file's main cross-reference section and every node of the tree (F.3.7: the hints give the pages in page order).
    /// </remarks>
    internal int IndexOf(CosDictionary page) =>
        HintedIndexOf(page) is int hinted ? hinted : Current.Indexes.TryGetValue(page, out int index) ? index : -1;

    /// <summary>
    /// While the hint tables locate the pages, the index of the page whose page object <paramref name="reference"/> refers to, from
    /// its object number and without reading anything; -1 when it refers to no page; <see langword="null"/> when the hints are not
    /// in use (resolve the reference and use <see cref="IndexOf(CosDictionary)"/>).
    /// </summary>
    /// <param name="reference">A reference, such as a destination's first element.</param>
    /// <returns>The index, -1, or <see langword="null"/>.</returns>
    /// <remarks>ISO 32000-2 Table F.4 items 1 and 2, and F.3.1 (every object of a linearized file has generation 0).</remarks>
    internal int? HintedIndexOf(CosReference reference) =>
        HintedCount is null ? null : _hinted!.TryGetIndex(reference, out int index) ? index : -1;

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Finds the page whose page object is <paramref name="pageObject"/> (by identity), for references such as a structure element's <c>Pg</c>.</summary>
    internal bool TryGetPage(CosDictionary pageObject, [MaybeNullWhen(false)] out PdfPage page)
    {
        if (HintedIndexOf(pageObject) is int hinted)
        {
            page = hinted >= 0 ? _hinted!.Get(hinted) : null;
            return page is not null;
        }

        PageList current = Current;
        if (current.Indexes.TryGetValue(pageObject, out int index))
        {
            page = current.Pages[index];
            return true;
        }

        page = null;
        return false;
    }

    /// <summary>
    /// While the hint tables are in use, the index of the page whose page object is <paramref name="pageObject"/>, or -1 when it is
    /// no page's: found from the pages handed out and the page objects loaded, never from the page tree. <see langword="null"/> when
    /// the hints are not in use, or the page fails its check (they are then rejected, and the tree is read).
    /// </summary>
    private int? HintedIndexOf(CosDictionary pageObject)
    {
        if (HintedCount is null)
        {
            return null;
        }

        int index = _hinted!.Find(pageObject);
        if (index < 0)
        {
            return -1;
        }

        if (_hinted.Get(index) is not { } page)
        {
            _hintsRejected = true;
            return null;
        }

        return ReferenceEquals(page.Dictionary, pageObject) ? index : -1;
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

    /// <summary>Enumerates the hinted pages; when a page fails its check, continues from the same index in the page tree's list.</summary>
    private IEnumerator<PdfPage> EnumerateByIndex()
    {
        int index = 0;
        while (HintedCount is int count && index < count)
        {
            if (_hinted!.Get(index) is not { } page)
            {
                _hintsRejected = true;
                break;
            }

            yield return page;
            index++;
        }

        if (HintedCount is null)
        {
            IReadOnlyList<PdfPage> pages = Pages;
            for (; index < pages.Count; index++)
            {
                yield return pages[index];
            }
        }
    }

    /// <summary>
    /// Walks the page tree; a page the hint tables already handed out a view for keeps that view, which now inherits through the
    /// tree's nodes.
    /// </summary>
    private IReadOnlyList<PdfPage> Walk()
    {
        IReadOnlyList<PdfPage> pages = _walk();
        if (_hinted is not { } hinted)
        {
            return pages;
        }

        var list = new PdfPage[pages.Count];
        for (int index = 0; index < pages.Count; index++)
        {
            PdfPage page = pages[index];
            if (page.Reference is { } reference && hinted.TryGetCreated(reference, out PdfPage? created))
            {
                created.Relink(reference, page.Ancestors);
                page = created;
            }

            list[index] = page;
        }

        return list;
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
