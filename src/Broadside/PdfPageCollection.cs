using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Broadside.Caching;
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
    // OnceCache rather than Lazy: a walk of a remote file's tree that misses bytes (issue #207) is not cached as failed.
    private readonly OnceCache<bool, IReadOnlyList<PdfPage>> _pages = new();
    private readonly OnceCache<bool, Dictionary<CosDictionary, int>> _indexes = new();
    private readonly Func<IReadOnlyList<PdfPage>> _walk;

    internal PdfPageCollection(Func<IReadOnlyList<PdfPage>> walk) => _walk = walk;

    /// <summary>Gets the number of pages.</summary>
    public int Count => Pages.Count;

    private IReadOnlyList<PdfPage> Pages => _pages.GetOrCreate(
        true,
        _walk,
        static (_, walk) => new Created<IReadOnlyList<PdfPage>>(walk()),
        static (_, _) => throw new InvalidOperationException("The page tree was read while it was being read."));

    private Dictionary<CosDictionary, int> Indexes => _indexes.GetOrCreate(
        true,
        this,
        static (_, pages) => new Created<Dictionary<CosDictionary, int>>(pages.IndexPages()),
        static (_, _) => throw new InvalidOperationException("The page index was read while it was being built."));

    /// <summary>Gets the page at a zero-based index.</summary>
    /// <param name="index">The index: 0 for the first page.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public PdfPage this[int index] => Pages[index];

    /// <inheritdoc/>
    public IEnumerator<PdfPage> GetEnumerator() => Pages.GetEnumerator();

    /// <summary>Returns the index of the page whose page object is <paramref name="page"/>, or -1 when no page has it.</summary>
    /// <param name="page">A page object, as resolved through the document (resolving returns the same instance every time).</param>
    /// <returns>The 0-based index, or -1.</returns>
    /// <remarks>For destinations (§12.3.2.2) and any other object that refers to a page. Built once, like the page list.</remarks>
    internal int IndexOf(CosDictionary page) => Indexes.TryGetValue(page, out int index) ? index : -1;

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Finds the page whose page object is <paramref name="pageObject"/> (by identity), for references such as a structure element's <c>Pg</c>.</summary>
    internal bool TryGetPage(CosDictionary pageObject, [MaybeNullWhen(false)] out PdfPage page)
    {
        if (Indexes.TryGetValue(pageObject, out int index))
        {
            page = Pages[index];
            return true;
        }

        page = null;
        return false;
    }

    private Dictionary<CosDictionary, int> IndexPages()
    {
        IReadOnlyList<PdfPage> pages = Pages;
        var indexes = new Dictionary<CosDictionary, int>(pages.Count, ReferenceEqualityComparer.Instance);
        for (int index = 0; index < pages.Count; index++)
        {
            indexes.TryAdd(pages[index].Dictionary, index);
        }

        return indexes;
    }
}
