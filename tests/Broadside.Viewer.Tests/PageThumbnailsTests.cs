using System.Diagnostics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>Page thumbnails (issue #216, viewer capability 10): low resolution, from the shared display-list cache, behind the main view.</summary>
/// <remarks>In the Heavy collection: the tests count display-list cache misses, and the cache's epoch is process-wide.</remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class PageThumbnailsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DocumentSession Open(string file, ViewerThreading threading) =>
        DocumentSession.Open(Corpus.Bytes(file), new ViewerOptions().WithThreading(threading));

    [Theory]
    [InlineData(ViewerThreading.Cooperative)]
    [InlineData(ViewerThreading.Background)]
    public async Task Thumbnails_replay_the_display_lists_the_main_view_built_and_leave_theirs_for_it(ViewerThreading threading)
    {
        await using DocumentSession session = Open("page-labels.pdf", threading);
        using var surface = new HeadlessSurface();
        var thumbnails = new PageThumbnails(session, surface);

        // The main view built pages 0 to 2.
        DisplayList[] lists = [.. await Task.WhenAll(Enumerable.Range(0, 3).Select(i => session.GetDisplayListAsync(i, cancellationToken: Token).AsTask()))];
        PdfCacheStatistics before = session.Document.CacheStatistics.DisplayLists;

        int drawn = await thumbnails.RenderAsync([0, 1, 2], Token);

        PdfCacheStatistics after = session.Document.CacheStatistics.DisplayLists;
        Assert.Equal(3, drawn);
        Assert.Equal(before.Misses, after.Misses); // no list was built for a thumbnail
        Assert.True(after.Hits >= before.Hits + 3, $"Hits went from {before.Hits} to {after.Hits}.");
        for (int i = 0; i < 3; i++)
        {
            Assert.Same(lists[i], await session.GetDisplayListAsync(i, cancellationToken: Token));
        }

        // A page the main view has not shown: the thumbnail builds its list once, and the main view takes it from the cache.
        await thumbnails.RenderAsync([7], Token);
        PdfCacheStatistics built = session.Document.CacheStatistics.DisplayLists;
        Assert.Equal(after.Misses + 1, built.Misses);
        await session.GetDisplayListAsync(7, cancellationToken: Token);
        Assert.Equal(built.Misses, session.Document.CacheStatistics.DisplayLists.Misses);
    }

    [Fact]
    public async Task A_thumbnail_is_a_whole_page_whose_longer_side_is_the_requested_size_in_the_order_asked()
    {
        await using DocumentSession session = Open("page-labels.pdf", ViewerThreading.Cooperative);
        using var surface = new HeadlessSurface();
        var thumbnails = new PageThumbnails(session, surface, new ThumbnailOptions().WithSize(100).WithMaxDeferral(TimeSpan.Zero));

        await thumbnails.RenderAsync([4, 1], Token);

        IReadOnlyList<PresentedTile> tiles = surface.Tiles;
        Assert.Equal([4, 1], tiles.Select(t => t.Tile.PageIndex));
        Assert.All(tiles, t =>
        {
            Assert.Equal(100.0 / 792, t.Tile.Scale, 9);
            Assert.Equal(78, t.Pixels.Width); // 612 * 100 / 792 = 77.3, rounded up
            Assert.Equal(100, t.Pixels.Height);
            Assert.Equal(0, t.Tile.Region.X);
            Assert.Equal(0, t.Tile.Region.Y);
        });
        Assert.Equal(100.0 / 792, await thumbnails.GetScaleAsync(0, Token), 9);
    }

    [Fact]
    public async Task Thumbnails_follow_the_sessions_color_filter()
    {
        await using DocumentSession session = Open("page-labels.pdf", ViewerThreading.Cooperative);
        using var surface = new HeadlessSurface();
        var thumbnails = new PageThumbnails(session, surface, new ThumbnailOptions().WithMaxDeferral(TimeSpan.Zero));

        await thumbnails.RenderAsync([0], Token);
        session.ColorFilter = ColorFilter.DarkMode;
        await thumbnails.RenderAsync([0], Token);

        PresentedTile dark = surface.Tiles.Single(); // the second thumbnail replaced the first (same tile)
        Assert.Equal([0, 0, 0, 255], dark.Pixels.GetRow(10).Slice(10 * 4, 4).ToArray()); // the white paper turned black
    }

    [Fact]
    public async Task Thumbnails_wait_while_the_session_has_other_work_but_never_longer_than_the_deferral()
    {
        await using DocumentSession session = Open("page-labels.pdf", ViewerThreading.Cooperative);
        using var surface = new HeadlessSurface();
        var patient = new PageThumbnails(session, surface, new ThumbnailOptions().WithMaxDeferral(TimeSpan.FromSeconds(30)));

        // Work the reader is waiting for is pending (here a held operation: a tile's page load and render are such operations).
        CancellationTokenSource foreground = session.Enter(CancellationToken.None);
        Task<int> drawing = patient.RenderAsync([0], Token);
        await Task.Delay(150, Token);
        Assert.False(drawing.IsCompleted);
        Assert.Empty(surface.Tiles);

        session.Exit(foreground);
        Assert.Equal(1, await drawing.WaitAsync(TimeSpan.FromSeconds(30), Token));
        Assert.Single(surface.Tiles);

        // A viewer that is never quiet still gets its thumbnails, one deferral apart.
        using var other = new HeadlessSurface();
        var impatient = new PageThumbnails(session, other, new ThumbnailOptions().WithMaxDeferral(TimeSpan.FromMilliseconds(40)));
        CancellationTokenSource busy = session.Enter(CancellationToken.None);
        try
        {
            long start = Stopwatch.GetTimestamp();
            Assert.Equal(2, await impatient.RenderAsync([0, 1], Token).WaitAsync(TimeSpan.FromSeconds(30), Token));
            Assert.True(Stopwatch.GetElapsedTime(start) >= TimeSpan.FromMilliseconds(70), "Each thumbnail waited its deferral.");
        }
        finally
        {
            session.Exit(busy);
        }
    }

    [Fact]
    public async Task Canceling_stops_the_thumbnails_and_a_page_that_cannot_be_drawn_is_reported_and_skipped()
    {
        await using DocumentSession session = Open("page-labels.pdf", ViewerThreading.Cooperative);
        using var surface = new HeadlessSurface();
        var thumbnails = new PageThumbnails(session, surface, new ThumbnailOptions().WithMaxDeferral(TimeSpan.Zero));
        var failed = new List<int>();
        thumbnails.Failed += (_, e) => failed.Add(e.PageIndex);

        Assert.Equal(2, await thumbnails.RenderAsync([0, 99, 1], Token));

        Assert.Equal([99], failed);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => thumbnails.RenderAsync([2], canceled.Token));
    }
}
