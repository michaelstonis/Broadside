using System.Globalization;
using System.Text;
using Broadside.Graphics;

namespace Broadside.Viewer.Tests;

/// <summary>Find in a document session through <see cref="DocumentSearch"/> (issue #215, viewer capability 7).</summary>
public sealed class DocumentSearchTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task By_default_case_and_diacritics_are_ignored_and_matches_come_back_in_page_and_reading_order()
    {
        await using DocumentSession session = Open(ViewerThreading.Background, ["Café one", "cafe two"], ["CAFE three"]);
        using var search = new DocumentSearch(session);

        await SearchAsync(search, "cafe");

        Assert.Equal(["Café", "cafe", "CAFE"], search.Matches.Select(m => m.Text));
        Assert.Equal([0, 0, 1], search.Matches.Select(m => m.PageIndex));
        Assert.Equal("Café one cafe two", search.Matches[0].Context);
    }

    [Fact]
    public async Task Matching_case_and_diacritics_narrows_the_results()
    {
        await using DocumentSession session = Open(ViewerThreading.Background, ["Café one", "cafe two"], ["CAFE three"]);
        using var search = new DocumentSearch(session);

        await SearchAsync(search, "cafe", new TextSearchOptions().WithMatchCase());
        Assert.Equal(["cafe"], search.Matches.Select(m => m.Text));

        await SearchAsync(search, "café", new TextSearchOptions().WithMatchDiacritics());
        Assert.Equal(["Café"], search.Matches.Select(m => m.Text));

        // A decomposed query (e and a combining acute) is the same text.
        await SearchAsync(search, "café", new TextSearchOptions().WithMatchDiacritics());
        Assert.Equal(["Café"], search.Matches.Select(m => m.Text));

        // Compatibility characters fold to what they stand for, with or without the host's globalization data: full-width letters.
        await SearchAsync(search, "ｃａｆｅ", new TextSearchOptions().WithMatchCase());
        Assert.Equal(["cafe"], search.Matches.Select(m => m.Text));
    }

    [Fact]
    public async Task Whole_word_matches_only_at_word_boundaries()
    {
        await using DocumentSession session = Open(ViewerThreading.Background, ["cat concatenate cat, scatter"]);
        using var search = new DocumentSearch(session);

        await SearchAsync(search, "cat");
        Assert.Equal(4, search.MatchCount);

        await SearchAsync(search, "cat", new TextSearchOptions().WithWholeWord());
        Assert.Equal(["cat", "cat"], search.Matches.Select(m => m.Text));
        Assert.All(search.Matches, m => Assert.Equal("cat concatenate cat, scatter", m.Context)); // a short page is its own context
    }

    [Fact]
    public async Task A_match_is_a_quad_over_its_glyphs_and_a_phrase_across_a_line_break_gives_one_quad_per_line()
    {
        await using DocumentSession session = Open(ViewerThreading.Background, ["alpha beta", "gamma delta"]);
        using var search = new DocumentSearch(session);

        await SearchAsync(search, "beta");
        SearchMatch beta = Assert.Single(search.Matches);
        Quad quad = Assert.Single(beta.Quads);
        // Helvetica advances (AFM): "alpha " is 2,724 units, "alpha beta" 4,670 and "gamma" 3,334, at 12 points.
        const double betaLeft = 72 + 32.688, betaRight = 72 + 56.04;
        Assert.Equal(betaLeft, quad.BottomLeft.X, 3);
        Assert.Equal(betaRight, quad.BottomRight.X, 3);
        Assert.Equal(700 - 2.484, quad.BottomLeft.Y, 3); // the glyph boxes reach down to the descender (-207/1000 em)
        Assert.True(quad.TopLeft.Y > 708, "the quad covers the glyphs' height");
        Assert.Equal(quad.Bounds, beta.Bounds);

        await SearchAsync(search, "beta   gamma");
        SearchMatch phrase = Assert.Single(search.Matches);
        Assert.Equal(2, phrase.Quads.Count);
        Assert.Equal(betaLeft, phrase.Quads[0].BottomLeft.X, 3);
        Assert.Equal(72 + 40.008, phrase.Quads[1].BottomRight.X, 3);
        Assert.Equal(686 - 2.484, phrase.Quads[1].BottomLeft.Y, 3);

        var buffer = new Quad[4];
        Assert.Equal(2, search.GetQuads(0, buffer));
        Assert.Equal(phrase.Quads, buffer[..2]);
        Assert.Equal(2, search.GetQuads(0, [])); // a short buffer still hears how many there are
        Assert.Equal(0, search.GetQuads(1, buffer));
    }

    [Fact]
    public async Task Next_and_previous_step_through_the_matches_and_wrap_around()
    {
        await using DocumentSession session = Open(ViewerThreading.Background, ["x one"], ["x two"], ["x three"]);
        using var search = new DocumentSearch(session);

        await SearchAsync(search, "x", startPage: 1);

        Assert.Equal(1, search.Current!.PageIndex); // the first match at or after the start page
        Assert.Equal(2, search.Next()!.PageIndex);
        Assert.Equal(0, search.Next()!.PageIndex);
        Assert.Equal(2, search.Previous()!.PageIndex);
        Assert.Equal(2, search.CurrentIndex);
        Assert.Equal(0, search.MoveTo(0).PageIndex);
        Assert.Equal(2, search.Previous()!.PageIndex);

        search.Clear();
        Assert.Null(search.Next());
        Assert.Null(search.Current);
        Assert.Equal(0, search.MatchCount);
    }

    [Fact]
    public async Task Searching_a_500_page_file_returns_first_results_before_indexing_finishes()
    {
        // Cooperative mode with a yield the test releases: the index grows one page per slice, so the state between slices is exact.
        var gate = new SliceGate();
        await using DocumentSession session = DocumentSession.Open(
            Pages(500, page => [string.Create(CultureInfo.InvariantCulture, $"page {page + 1} needle")]),
            Options(ViewerThreading.Cooperative).UseYield(gate.YieldAsync));
        using var search = new DocumentSearch(session);
        int changes = 0;
        search.Changed += (_, _) => changes++;

        search.Search("needle");
        Assert.Equal(0, search.IndexedPageCount); // nothing is read on the caller's slice

        await gate.ReleaseAsync(3);
        Assert.Equal(3, search.MatchCount);
        Assert.Equal(3, search.IndexedPageCount);
        Assert.False(search.IsIndexComplete);
        Assert.True(search.IsIndexing);
        Assert.Equal(0, search.Current!.PageIndex);
        Assert.True(changes > 0);

        await gate.ReleaseAllAsync(() => !search.IsIndexing);
        Assert.Equal(500, search.MatchCount);
        Assert.True(search.IsIndexComplete);
        Assert.True(search.IsSearchComplete);
        Assert.Equal(Enumerable.Range(0, 500), search.Matches.Select(m => m.PageIndex));
    }

    [Fact]
    public async Task A_background_search_of_500_pages_finds_every_page_and_keeps_the_index_for_the_next_query()
    {
        await using DocumentSession session = DocumentSession.Open(
            Pages(500, page => [page % 2 == 0 ? "even needle" : "odd needle"]), Options(ViewerThreading.Background));
        using var search = new DocumentSearch(session);

        await SearchAsync(search, "needle", startPage: 250);
        Assert.Equal(500, search.MatchCount);
        Assert.Equal(250, search.Current!.PageIndex);
        Assert.Equal(500, search.IndexedPageCount);

        await SearchAsync(search, "odd");
        Assert.Equal(250, search.MatchCount);
        Assert.Equal(500, search.IndexedPageCount);
    }

    [Fact]
    public async Task Closing_the_session_stops_indexing()
    {
        var gate = new SliceGate();
        DocumentSession session = DocumentSession.Open(
            Pages(500, page => ["needle"]), Options(ViewerThreading.Cooperative).UseYield(gate.YieldAsync));
        using var search = new DocumentSearch(session);
        search.Search("needle");
        await gate.ReleaseAsync(2);
        Assert.True(search.IsIndexing);

        await session.DisposeAsync(); // completes once every pending read has stopped
        await gate.ReleaseAllAsync(() => !search.IsIndexing);

        int indexed = search.IndexedPageCount;
        Assert.Equal(2, indexed);
        Assert.False(search.IsIndexing);
        Assert.False(search.IsIndexComplete);
        Assert.Equal(indexed, search.IndexedPageCount);
    }

    [Fact]
    public async Task Disposing_the_search_stops_indexing_in_background_mode()
    {
        await using DocumentSession session = DocumentSession.Open(Pages(500, page => ["needle"]), Options(ViewerThreading.Background));
        var search = new DocumentSearch(session);
        search.Search("needle");
        search.Dispose();

        await WaitUntilAsync(() => !search.IsIndexing);
        int indexed = search.IndexedPageCount;
        await Task.Delay(50, Token);
        Assert.Equal(indexed, search.IndexedPageCount);
        Assert.Throws<ObjectDisposedException>(() => search.Search("needle"));
    }

    [Fact]
    public async Task Highlighting_every_match_draws_no_tile_again()
    {
        await using DocumentSession session = DocumentSession.Open(
            Pages(3, page => ["needle in a haystack", "another needle"]), Options(ViewerThreading.Cooperative));
        using var surface = new HeadlessSurface();
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        var events = new List<TileSchedulerEvent>();
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().UseObserver(events.Add));
        var viewport = new Viewport(0, 0, 700, 2400, 1);
        scheduler.Update(viewport);
        while (scheduler.RunFrame(Timeout.InfiniteTimeSpan))
        {
        }

        int presented = surface.Tiles.Count;
        events.Clear();
        using var search = new DocumentSearch(session);
        await SearchAsync(search, "needle");
        var quads = new Quad[8];
        Assert.Equal(2, search.GetQuads(0, quads));
        search.Next();
        scheduler.Update(viewport);
        while (scheduler.RunFrame(Timeout.InfiniteTimeSpan))
        {
        }

        Assert.Equal(6, search.MatchCount);
        Assert.Equal(presented, surface.Tiles.Count);
        Assert.DoesNotContain(events, e => e.Kind is TileEventKind.Started or TileEventKind.Presented or TileEventKind.Invalidated);
    }

    private static async Task SearchAsync(DocumentSearch search, string query, TextSearchOptions? options = null, int startPage = 0)
    {
        search.Search(query, options, startPage);
        await WaitUntilAsync(() => !search.IsIndexing && search.IsSearchComplete);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(5, Token);
        }
    }

    private static ViewerOptions Options(ViewerThreading threading) =>
        new ViewerOptions().WithDocumentOptions(new PdfOptions().UseStandard14Fonts()).WithThreading(threading);

    private static DocumentSession Open(ViewerThreading threading, params string[][] pages) =>
        DocumentSession.Open(Pages(pages.Length, page => pages[page]), Options(threading));

    /// <summary>A file of Letter pages, each showing its lines in Helvetica 12 (WinAnsiEncoding) from (72, 700) down 14 points a line.</summary>
    private static byte[] Pages(int count, Func<int, string[]> lines)
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        void Add(string body)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        Add("<< /Type /Catalog /Pages 2 0 R >>");
        Add($"<< /Type /Pages /Count {count} /Kids [{string.Join(' ', Enumerable.Range(0, count).Select(i => $"{4 + (2 * i)} 0 R"))}] >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        for (int i = 0; i < count; i++)
        {
            var content = new StringBuilder("BT /F1 12 Tf 14 TL 72 700 Td");
            foreach (string line in lines(i))
            {
                content.Append(" (").Append(line.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal)).Append(") Tj T*");
            }

            content.Append(" ET");
            Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + (2 * i)} 0 R >>");
            Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        int xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(pdf.ToString());
    }

    /// <summary>A cooperative yield that waits for the test: each release lets one waiting slice go on.</summary>
    private sealed class SliceGate
    {
        private readonly Lock _lock = new();
        private readonly Queue<TaskCompletionSource> _waiting = new();
        private bool _open;

        public ValueTask YieldAsync(CancellationToken cancellationToken)
        {
            var slice = new TaskCompletionSource();
            lock (_lock)
            {
                if (_open)
                {
                    return ValueTask.CompletedTask;
                }

                _waiting.Enqueue(slice);
            }

            return new ValueTask(slice.Task.WaitAsync(cancellationToken));
        }

        /// <summary>Lets <paramref name="slices"/> slices run, one at a time, each to its next yield.</summary>
        public async Task ReleaseAsync(int slices)
        {
            for (int i = 0; i < slices; i++)
            {
                TaskCompletionSource next = await NextAsync();
                next.SetResult();
                await WaitUntilAsync(HasWaiting); // the slice ran up to its next yield
            }
        }

        /// <summary>Opens the gate until <paramref name="done"/>.</summary>
        public async Task ReleaseAllAsync(Func<bool> done)
        {
            lock (_lock)
            {
                _open = true;
            }

            while (true)
            {
                TaskCompletionSource[] waiting;
                lock (_lock)
                {
                    waiting = [.. _waiting];
                    _waiting.Clear();
                }

                foreach (TaskCompletionSource slice in waiting)
                {
                    slice.TrySetResult();
                }

                if (done())
                {
                    return;
                }

                await WaitUntilAsync(() => done() || HasWaiting());
            }
        }

        private bool HasWaiting()
        {
            lock (_lock)
            {
                return _waiting.Count > 0;
            }
        }

        private async Task<TaskCompletionSource> NextAsync()
        {
            await WaitUntilAsync(HasWaiting);
            lock (_lock)
            {
                return _waiting.Dequeue();
            }
        }
    }
}
