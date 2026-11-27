namespace Broadside.Viewer;

/// <summary>
/// A <see cref="DocumentSearch"/>'s matches as a search panel lists them: grouped by page, each with the page's text before and after
/// it. A view over the search: it follows <see cref="DocumentSearch.Changed"/>, and <see cref="Show"/> moves the search's current match
/// and the viewer to an item.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.10 (text extraction). <see cref="Pages"/> is computed again after each change of the search, when read. Raised on
/// the thread the search raises its event on; a shell marshals to its UI thread as it does for the search.
/// </remarks>
public sealed class SearchResults : IDisposable
{
    private readonly int _contextCharacters;
    private IReadOnlyList<SearchResultGroup>? _pages;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="SearchResults"/> class over a search.</summary>
    /// <param name="search">The search of the open document.</param>
    /// <param name="contextCharacters">About how many characters of the page's text to show on each side of a match, cut to whole words.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="contextCharacters"/> is negative.</exception>
    public SearchResults(DocumentSearch search, int contextCharacters = 40)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentOutOfRangeException.ThrowIfNegative(contextCharacters);
        Search = search;
        _contextCharacters = contextCharacters;
        search.Changed += OnSearchChanged;
    }

    /// <summary>Raised when the matches, the current match or the search's progress changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the search.</summary>
    public DocumentSearch Search { get; }

    /// <summary>Gets the matches found so far, by page in page order, each page's matches in reading order.</summary>
    public IReadOnlyList<SearchResultGroup> Pages => Volatile.Read(ref _pages) ?? Build();

    /// <summary>Gets the number of matches found so far.</summary>
    public int Count => Search.MatchCount;

    /// <summary>Makes an item the search's current match and shows it in a viewer: what a click on a result does.</summary>
    /// <param name="item">A result of <see cref="Pages"/>.</param>
    /// <param name="viewer">The viewer, or <see langword="null"/> to only move the current match.</param>
    public void Show(SearchResultItem item, IDocumentViewer? viewer)
    {
        ArgumentNullException.ThrowIfNull(item);
        int index = Search.Matches is { } matches && item.MatchIndex < matches.Count && ReferenceEquals(matches[item.MatchIndex], item.Match)
            ? item.MatchIndex
            : IndexOf(Search.Matches, item.Match);
        if (index >= 0)
        {
            Search.MoveTo(index);
        }

        viewer?.Reveal(item.Match.PageIndex, item.Match.Bounds);
    }

    /// <summary>Stops following the search.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Search.Changed -= OnSearchChanged;
        }
    }

    private static int IndexOf(IReadOnlyList<SearchMatch> matches, SearchMatch match)
    {
        for (int i = 0; i < matches.Count; i++)
        {
            if (ReferenceEquals(matches[i], match))
            {
                return i;
            }
        }

        return -1;
    }

    private void OnSearchChanged(object? sender, EventArgs e)
    {
        Volatile.Write(ref _pages, null);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<SearchResultGroup> Build()
    {
        IReadOnlyList<SearchMatch> matches = Search.Matches;
        var groups = new List<SearchResultGroup>();
        List<SearchResultItem>? items = null;
        int page = -1;
        for (int i = 0; i < matches.Count; i++)
        {
            SearchMatch match = matches[i];
            if (match.PageIndex != page)
            {
                if (items is not null)
                {
                    groups.Add(new SearchResultGroup(page, items));
                }

                page = match.PageIndex;
                items = [];
            }

            (string before, string after) = Search.GetSurroundings(match, _contextCharacters);
            items!.Add(new SearchResultItem(i, match, before, after));
        }

        if (items is not null)
        {
            groups.Add(new SearchResultGroup(page, items));
        }

        groups.Sort(static (a, b) => a.PageIndex.CompareTo(b.PageIndex));
        Volatile.Write(ref _pages, groups);
        return groups;
    }
}

/// <summary>The matches of one page.</summary>
/// <param name="PageIndex">The zero-based page.</param>
/// <param name="Items">Its matches.</param>
public sealed record SearchResultGroup(int PageIndex, IReadOnlyList<SearchResultItem> Items);

/// <summary>One match with the page's text around it.</summary>
/// <param name="MatchIndex">Its index in <see cref="DocumentSearch.Matches"/> when it was listed.</param>
/// <param name="Match">The match: its page, quads, bounds and text.</param>
/// <param name="Before">The page's text just before it (with a leading ellipsis when cut).</param>
/// <param name="After">The page's text just after it (with a trailing ellipsis when cut).</param>
/// <remarks>ISO 32000-2 §9.10.</remarks>
public sealed record SearchResultItem(int MatchIndex, SearchMatch Match, string Before, string After)
{
    /// <summary>Gets the matched text.</summary>
    public string Text => Match.Text;
}
