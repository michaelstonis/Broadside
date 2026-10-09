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
    private readonly Lazy<Dictionary<CosDictionary, PdfPage>> _byObject;

    internal PdfPageCollection(Func<IReadOnlyList<PdfPage>> walk)
    {
        _pages = new Lazy<IReadOnlyList<PdfPage>>(walk, LazyThreadSafetyMode.ExecutionAndPublication);
        _byObject = new Lazy<Dictionary<CosDictionary, PdfPage>>(IndexPages, LazyThreadSafetyMode.ExecutionAndPublication);
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

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Finds the page whose page object is <paramref name="pageObject"/> (by identity), for references such as a structure element's <c>Pg</c>.</summary>
    internal bool TryGetPage(CosDictionary pageObject, [MaybeNullWhen(false)] out PdfPage page) => _byObject.Value.TryGetValue(pageObject, out page);

    private Dictionary<CosDictionary, PdfPage> IndexPages()
    {
        var index = new Dictionary<CosDictionary, PdfPage>(ReferenceEqualityComparer.Instance);
        foreach (PdfPage page in _pages.Value)
        {
            index.TryAdd(page.Dictionary, page);
        }

        return index;
    }
}
