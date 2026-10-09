using System.Collections;

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

    internal PdfPageCollection(Func<IReadOnlyList<PdfPage>> walk) =>
        _pages = new Lazy<IReadOnlyList<PdfPage>>(walk, LazyThreadSafetyMode.ExecutionAndPublication);

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
}
