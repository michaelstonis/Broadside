using System.Diagnostics;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Text;

namespace Broadside.Viewer;

/// <summary>
/// Find in a document session: the words of each page are extracted in the background into an index that grows page by page, and
/// a query (<see cref="Search"/>) matches every page as soon as it is indexed, so the first results arrive while the rest of the
/// document is still being read. Results are quads in default user space (<see cref="SearchMatch"/>); <see cref="Next"/> and
/// <see cref="Previous"/> step through them, and <see cref="GetQuads"/> gives a page's matches for a shell to draw over its tiles
/// (highlight-all is an overlay: no tile is drawn again).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.10 (the text a page shows), §14.8.2.5 (reading order), §14.8.2.6.2 (word breaks) and §14.9.4 (replacement text).
/// Spec #33 viewer capability 7.
/// </para>
/// <para>
/// In <see cref="ViewerThreading.Background"/> mode pages are read on thread-pool workers (at most two at once, so tiles keep the
/// rest), each read under the session's read lease, so an edit (<see cref="DocumentSession.BeginEdit"/>) stops it and it starts again
/// afterwards. In <see cref="ViewerThreading.Cooperative"/> mode (the browser) pages are read on the caller's thread, one page per
/// slice with the session's yield between them. The index is kept for the next query: a page is read once. Closing the session or
/// disposing the search stops the indexing.
/// </para>
/// <para>
/// Drive it from the UI thread (<see cref="Search"/>, <see cref="Next"/>, <see cref="Previous"/>, <see cref="Clear"/>); the
/// properties and <see cref="GetQuads"/> may be read from any thread and <see cref="GetQuads"/> allocates nothing.
/// <see cref="Changed"/> is raised on the thread that indexed a page: a worker in background mode.
/// </para>
/// </remarks>
public sealed class DocumentSearch : IDisposable
{
    private const int MaxWorkers = 2;

    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _disposed = new();
    private readonly SearchablePage?[] _pages;
    private readonly List<SearchMatch>?[] _pageMatches;
    private readonly List<SearchMatch> _matches = [];
    private SearchMatch[]? _snapshot;
    private CancellationTokenSource? _pass;
    private int _generation;
    private int _current = -1;
    private bool _moved;
    private int _startPage;
    private int _searched;
    private int _indexed;
    private int _failed;
    private int _running;
    private string _needle = string.Empty;
    private TextSearchOptions _options = new();

    /// <summary>Creates a search over a session's pages; nothing is read until <see cref="Search"/> or <see cref="StartIndexing"/>.</summary>
    /// <param name="session">The session.</param>
    public DocumentSearch(DocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        _pages = new SearchablePage?[session.PageCount];
        _pageMatches = new List<SearchMatch>?[session.PageCount];
    }

    /// <summary>Raised when matches arrive, when the current match moves, and as pages are indexed (so a shell shows progress and redraws).</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the session.</summary>
    public DocumentSession Session { get; }

    /// <summary>Gets the query being searched for, or the empty string.</summary>
    public string Query { get; private set; } = string.Empty;

    /// <summary>Gets the options of the current query.</summary>
    public TextSearchOptions Options => _options;

    /// <summary>Gets the matches found so far, in page order and reading order within a page.</summary>
    public IReadOnlyList<SearchMatch> Matches
    {
        get
        {
            lock (_lock)
            {
                return _snapshot ??= [.. _matches];
            }
        }
    }

    /// <summary>Gets the number of matches found so far.</summary>
    public int MatchCount
    {
        get
        {
            lock (_lock)
            {
                return _matches.Count;
            }
        }
    }

    /// <summary>Gets the position of the current match in <see cref="Matches"/>, or -1 when there is none.</summary>
    public int CurrentIndex
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    /// <summary>Gets the current match (the one a shell shows distinctly and scrolls to), or <see langword="null"/>.</summary>
    public SearchMatch? Current
    {
        get
        {
            lock (_lock)
            {
                return _current >= 0 ? _matches[_current] : null;
            }
        }
    }

    /// <summary>Gets the number of pages the current query has been matched against.</summary>
    public int SearchedPageCount => Volatile.Read(ref _searched);

    /// <summary>Gets the number of pages whose words are in the index.</summary>
    public int IndexedPageCount => Volatile.Read(ref _indexed);

    /// <summary>Gets the number of indexed pages whose text could not be read (they hold no matches).</summary>
    public int FailedPageCount => Volatile.Read(ref _failed);

    /// <summary>Gets whether every page is in the index.</summary>
    public bool IsIndexComplete => IndexedPageCount == _pages.Length;

    /// <summary>Gets whether the current query has been matched against every page.</summary>
    public bool IsSearchComplete => Query.Length > 0 && SearchedPageCount == _pages.Length;

    /// <summary>Gets whether pages are being read or matched right now.</summary>
    public bool IsIndexing => Volatile.Read(ref _running) > 0;

    /// <summary>
    /// Searches for <paramref name="query"/>: matches on pages already indexed arrive at once, the others as their pages are indexed,
    /// starting with <paramref name="startPage"/> and wrapping around. The first match found becomes current, preferring one at or after
    /// <paramref name="startPage"/>. A new query replaces the previous one; an empty one clears the results.
    /// </summary>
    /// <param name="query">The text to find; runs of white space match any break between words or lines.</param>
    /// <param name="options">How to compare, or <see langword="null"/> to ignore case and diacritics.</param>
    /// <param name="startPage">The page to search first (the page the viewer shows).</param>
    /// <exception cref="ObjectDisposedException">The search is disposed.</exception>
    /// <remarks>ISO 32000-2 §9.10 and §14.8.2.6.2. Spec #33 viewer capability 7.</remarks>
    public void Search(string query, TextSearchOptions? options = null, int startPage = 0)
    {
        ArgumentNullException.ThrowIfNull(query);
        ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
        options = options is null
            ? new TextSearchOptions()
            : new TextSearchOptions().WithMatchCase(options.MatchCase).WithWholeWord(options.WholeWord).WithMatchDiacritics(options.MatchDiacritics);
        string needle = SearchablePage.FoldQuery(query, options);
        int generation;
        lock (_lock)
        {
            generation = Reset();
            Query = needle.Length == 0 ? string.Empty : query;
            _needle = needle;
            _options = options;
            _startPage = _pages.Length == 0 ? 0 : Math.Clamp(startPage, 0, _pages.Length - 1);
        }

        if (needle.Length > 0 || !IsIndexComplete)
        {
            StartPass(generation);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts indexing every page without a query (when idle), so a later search finds its matches at once.</summary>
    /// <exception cref="ObjectDisposedException">The search is disposed.</exception>
    public void StartIndexing()
    {
        ObjectDisposedException.ThrowIf(_disposed.IsCancellationRequested, this);
        int generation;
        lock (_lock)
        {
            if (_pass is not null || IsIndexComplete)
            {
                return;
            }

            generation = _generation;
        }

        StartPass(generation);
    }

    /// <summary>Clears the query and its results; the index is kept (indexing of the remaining pages stops).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            Reset();
            Query = string.Empty;
            _needle = string.Empty;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Makes the next match current, wrapping from the last to the first.</summary>
    /// <returns>The new current match, or <see langword="null"/> when there is none yet.</returns>
    public SearchMatch? Next() => Step(1);

    /// <summary>Makes the previous match current, wrapping from the first to the last.</summary>
    /// <returns>The new current match, or <see langword="null"/> when there is none yet.</returns>
    public SearchMatch? Previous() => Step(-1);

    /// <summary>Makes a match current.</summary>
    /// <param name="index">Its position in <see cref="Matches"/>.</param>
    /// <returns>The match.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such match.</exception>
    public SearchMatch MoveTo(int index)
    {
        SearchMatch match;
        lock (_lock)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _matches.Count);
            _current = index;
            _moved = true;
            match = _matches[index];
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return match;
    }

    /// <summary>
    /// Copies the quads of every match on a page into <paramref name="destination"/> (as many as fit), for a shell to highlight over
    /// the page's tiles. Allocates nothing.
    /// </summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="destination">Receives the quads, in default user space.</param>
    /// <returns>The number of quads the page's matches have (more than <paramref name="destination"/> holds when it is too small).</returns>
    /// <remarks>ISO 32000-2 §9.4.4.</remarks>
    public int GetQuads(int pageIndex, Span<Quad> destination)
    {
        if ((uint)pageIndex >= (uint)_pageMatches.Length)
        {
            return 0;
        }

        lock (_lock)
        {
            if (_pageMatches[pageIndex] is not { } matches)
            {
                return 0;
            }

            int count = 0;
            foreach (SearchMatch match in matches)
            {
                ReadOnlySpan<Quad> quads = match.QuadSpan;
                if (count + quads.Length <= destination.Length)
                {
                    quads.CopyTo(destination[count..]);
                }

                count += quads.Length;
            }

            return count;
        }
    }

    /// <summary>Stops indexing and clears the results.</summary>
    public void Dispose()
    {
        if (_disposed.IsCancellationRequested)
        {
            return;
        }

        lock (_lock)
        {
            Reset();
            _disposed.Cancel();
            _disposed.Dispose();
        }
    }

    /// <summary>Cancels the running pass and clears the results; returns the new generation. Called under the lock.</summary>
    private int Reset()
    {
        _pass?.Cancel();
        _pass?.Dispose();
        _pass = null;
        _matches.Clear();
        _snapshot = null;
        Array.Clear(_pageMatches);
        _current = -1;
        _moved = false;
        Volatile.Write(ref _searched, 0);
        return ++_generation;
    }

    private SearchMatch? Step(int direction)
    {
        SearchMatch? match;
        lock (_lock)
        {
            if (_matches.Count == 0)
            {
                return null;
            }

            _current = _current < 0
                ? direction > 0 ? 0 : _matches.Count - 1
                : (((_current + direction) % _matches.Count) + _matches.Count) % _matches.Count;
            _moved = true;
            match = _matches[_current];
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return match;
    }

    private void StartPass(int generation)
    {
        CancellationTokenSource pass;
        lock (_lock)
        {
            if (generation != _generation || _disposed.IsCancellationRequested)
            {
                return;
            }

            pass = CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token);
            _pass = pass;
        }

        CancellationToken token = pass.Token;
        int count = _pages.Length;
        int start = _startPage;
        int[] cursor = [0];
        int workers = Session.IsCooperative ? 1 : Math.Min(MaxWorkers, Math.Max(1, Session.MaxConcurrency / 2));
        for (int i = 0; i < workers; i++)
        {
            Interlocked.Increment(ref _running);
            _ = RunWorkerAsync(generation, start, count, cursor, token);
        }
    }

    private async Task RunWorkerAsync(int generation, int start, int count, int[] cursor, CancellationToken token)
    {
        try
        {
            bool cooperative = Session.IsCooperative;
            long slice = Stopwatch.GetTimestamp();
            for (int next = Interlocked.Increment(ref cursor[0]) - 1; next < count; next = Interlocked.Increment(ref cursor[0]) - 1)
            {
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(Session.IsClosed, Session);
                int page = (start + next) % count;
                if (cooperative && (next == 0 || Volatile.Read(ref _pages[page]) is null || Stopwatch.GetElapsedTime(slice) >= Session.SliceBudget))
                {
                    // One page's extraction per slice; matching pages already indexed shares a slice (the browser's thread stays free).
                    await Session.PauseAsync(token).ConfigureAwait(false);
                    slice = Stopwatch.GetTimestamp();
                }

                await SearchPageAsync(generation, page, token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            // A newer query, the search's disposal or the session's closing stopped the pass.
        }
        finally
        {
            Interlocked.Decrement(ref _running);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task SearchPageAsync(int generation, int pageIndex, CancellationToken token)
    {
        SearchablePage? page = Volatile.Read(ref _pages[pageIndex]);
        if (page is null)
        {
            page = await IndexPageAsync(pageIndex, token).ConfigureAwait(false);
            if (Interlocked.CompareExchange(ref _pages[pageIndex], page, null) is { } raced)
            {
                page = raced;
            }
            else
            {
                Interlocked.Increment(ref _indexed);
            }
        }

        string needle;
        TextSearchOptions options;
        lock (_lock)
        {
            if (generation != _generation)
            {
                return;
            }

            needle = _needle;
            options = _options;
        }

        List<SearchMatch> found = [];
        page.Find(pageIndex, needle, options, found, token);
        lock (_lock)
        {
            if (generation != _generation)
            {
                return;
            }

            Merge(pageIndex, found);
            Volatile.Write(ref _searched, _searched + 1);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Inserts a page's matches in page order, keeping the current match current. Called under the lock.</summary>
    private void Merge(int pageIndex, List<SearchMatch> found)
    {
        if (found.Count == 0)
        {
            return;
        }

        _pageMatches[pageIndex] = found;
        int lo = 0, hi = _matches.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_matches[mid].PageIndex < pageIndex)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        _matches.InsertRange(lo, found);
        _snapshot = null;
        if (_current >= lo)
        {
            _current += found.Count;
        }

        if (_current < 0 || (!_moved && Distance(pageIndex) < Distance(_matches[_current].PageIndex)))
        {
            // Until the user moves, the current match is the first at or after the start page (pages arrive in any order).
            _current = lo;
        }
    }

    /// <summary>How far after the start page a page comes in search order (wrapping around).</summary>
    private int Distance(int pageIndex) => (pageIndex - _startPage + _pages.Length) % _pages.Length;

    /// <summary>Reads a page's words: on a worker under the session's read lease, or on this thread after a yield (cooperative).</summary>
    private async Task<SearchablePage> IndexPageAsync(int pageIndex, CancellationToken token)
    {
        CancellationTokenSource operation = Session.Enter(token);
        try
        {
            CancellationToken read = operation.Token;
            await Session.ReadPageAsync(pageIndex, read).ConfigureAwait(false);
            try
            {
                return await Session.ReadAsync(
                    readToken => SearchablePage.Create(Session.Document.Pages[pageIndex].ExtractText(new TextExtractionOptions()
                        .WithArtifacts()
                        .WithContentOptions(new ContentOptions().WithCancellation(readToken)))),
                    read).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not ObjectDisposedException)
            {
                // A page whose content cannot be read still draws what it can; the search counts it and goes on.
                Interlocked.Increment(ref _failed);
                return SearchablePage.Empty;
            }
        }
        finally
        {
            Session.Exit(operation);
        }
    }
}
