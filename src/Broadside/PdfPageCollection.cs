using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Broadside.Objects;

namespace Broadside;

/// <summary>The pages of a document, in page order: the leaves of the page tree.</summary>
/// <remarks>
/// ISO 32000-2 §7.7.3. The page tree is walked on first use (count, index or enumeration), once, by its <c>Kids</c> arrays; its
/// <c>Count</c> entries are only checked, never trusted (§7.7.3.2 calls them redundant). Each <see cref="PdfPage"/> is a live view over
/// its page object, but the list itself is the tree's shape at the first walk: the page editing API (a later version) keeps it current.
/// </remarks>
public sealed class PdfPageCollection : IReadOnlyList<PdfPage>
{
    private readonly Lazy<IReadOnlyList<PdfPage>> _pages;
    private readonly Lazy<Dictionary<CosDictionary, int>> _indexes;

    internal PdfPageCollection(Func<IReadOnlyList<PdfPage>> walk)
    {
        _pages = new Lazy<IReadOnlyList<PdfPage>>(walk, LazyThreadSafetyMode.ExecutionAndPublication);
        _indexes = new Lazy<Dictionary<CosDictionary, int>>(IndexPages, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the number of pages.</summary>
    public int Count => _pages.Value.Count;

    /// <summary>Gets the page at a zero-based index.</summary>
    /// <param name="index">The index: 0 for the first page.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public PdfPage this[int index] => _pages.Value[index];

    /// <inheritdoc/>
    public IEnumerator<PdfPage> GetEnumerator() => _pages.Value.GetEnumerator();

    /// <summary>Returns the index of the page whose page object is <paramref name="page"/>, or -1 when no page has it.</summary>
    /// <param name="page">A page object, as resolved through the document (resolving returns the same instance every time).</param>
    /// <returns>The 0-based index, or -1.</returns>
    /// <remarks>For destinations (§12.3.2.2) and any other object that refers to a page. Built once, like the page list.</remarks>
    internal int IndexOf(CosDictionary page) => _indexes.Value.TryGetValue(page, out int index) ? index : -1;

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Finds the page whose page object is <paramref name="pageObject"/> (by identity), for references such as a structure element's <c>Pg</c>.</summary>
    internal bool TryGetPage(CosDictionary pageObject, [MaybeNullWhen(false)] out PdfPage page)
    {
        if (_indexes.Value.TryGetValue(pageObject, out int index))
        {
            page = _pages.Value[index];
            return true;
        }

        page = null;
        return false;
    }

    private Dictionary<CosDictionary, int> IndexPages()
    {
        IReadOnlyList<PdfPage> pages = _pages.Value;
        var indexes = new Dictionary<CosDictionary, int>(pages.Count, ReferenceEqualityComparer.Instance);
        for (int index = 0; index < pages.Count; index++)
        {
            indexes.TryAdd(pages[index].Dictionary, index);
        }

        return indexes;
    }
}
