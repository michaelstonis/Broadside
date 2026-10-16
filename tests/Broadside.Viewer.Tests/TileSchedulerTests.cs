using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>The tile scheduler through the headless host: priority order, render-ahead, cancellation and the memory budget (issue #211).</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class TileSchedulerTests
{
    private const double Zoom = 0.5;

    [Fact]
    public async Task A_scripted_scroll_through_500_pages_never_starts_an_offscreen_tile_before_a_visible_one()
    {
        ScrollRun run = await ScrollAsync(TileSchedulerOptions.DefaultMemoryBudget);

        Assert.Empty(run.Violations);
        Assert.Contains(run.Events, e => e.Kind == TileEventKind.Started && e.Priority == TilePriority.Ahead);
        Assert.Contains(run.Events, e => e.Kind == TileEventKind.Started && e.Priority == TilePriority.Near);
        Assert.Contains(run.Events, e => e.Kind == TileEventKind.Cancelled);
        Assert.Contains(run.Events, e => e.Kind == TileEventKind.Presented && e.Tile.PageIndex == 499);
    }

    [Fact]
    public async Task Memory_stays_under_the_budget_throughout_a_scroll_through_500_pages()
    {
        const long budget = 4L * 1024 * 1024;
        ScrollRun run = await ScrollAsync(budget);

        Assert.Empty(run.Violations);
        Assert.All(run.Events, e => Assert.InRange(e.Bytes, 0, budget));
        Assert.All(run.Measured, bytes => Assert.InRange(bytes, 0, budget));
        Assert.InRange(run.Statistics.PeakBytes, 1, budget);
        Assert.True(run.Statistics.TilesEvicted > 100, $"evicted {run.Statistics.TilesEvicted}");
        Assert.Equal(run.Statistics.TileBytes, run.SurfaceBytes);
        Assert.Contains(run.Events, e => e.Kind == TileEventKind.Presented && e.Tile.PageIndex == 499);
    }

    [Fact]
    public async Task A_cooperative_render_cancelled_by_a_scroll_never_reaches_the_surface()
    {
        var backends = new List<HeldBackend>();
        using var surface = new HeadlessSurface();
        await using DocumentSession session = DocumentSession.Open(
            ManyPages(500),
            new ViewerOptions().WithThreading(ViewerThreading.Cooperative).UseBackend(HeldBackend.Factory(backends)));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var events = new List<TileSchedulerEvent>();
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().UseObserver(events.Add));

        HeldBackend.Hold = true;
        try
        {
            scheduler.Update(new Viewport(0, 0, 500, 700, Zoom));
            Assert.True(scheduler.RunFrame(TimeSpan.FromMilliseconds(2)));
            Assert.Contains(events, e => e.Kind == TileEventKind.Started && e.Tile.PageIndex == 0);
            Assert.Empty(surface.Tiles);

            scheduler.Update(layout.ScrollTo(new Viewport(0, 0, 500, 700, Zoom), 400));
        }
        finally
        {
            HeldBackend.Hold = false;
        }

        while (scheduler.RunFrame(Timeout.InfiniteTimeSpan))
        {
        }

        TileSchedulerEvent[] cancelled = [.. events.Where(e => e.Kind == TileEventKind.Cancelled)];
        Assert.NotEmpty(cancelled);
        Assert.All(cancelled, e => Assert.DoesNotContain(surface.Tiles, presented => presented.Tile == e.Tile));
        Assert.All(surface.Tiles, presented => Assert.InRange(presented.Tile.PageIndex, 390, 410));
        Assert.Equal(cancelled.Length, session.Statistics.TilesCancelled);
        Assert.Equal(0, session.Statistics.PixelBytes);
        Assert.Equal(0, session.Statistics.PendingOperations);
    }

    [Fact]
    public async Task A_background_render_cancelled_by_a_scroll_never_reaches_the_surface()
    {
        var backends = new List<HeldBackend>();
        using var surface = new HeadlessSurface();
        await using DocumentSession session = DocumentSession.Open(
            ManyPages(500),
            new ViewerOptions().WithThreading(ViewerThreading.Background).UseBackend(HeldBackend.Factory(backends)));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var events = new List<TileSchedulerEvent>();
        using var scheduler = new TileScheduler(
            session,
            layout,
            surface,
            new TileSchedulerOptions().WithMaxConcurrency(2).UseObserver(e =>
            {
                lock (events)
                {
                    events.Add(e);
                }
            }));

        HeldBackend.Hold = true;
        try
        {
            scheduler.Update(new Viewport(0, 0, 500, 700, Zoom));
            scheduler.RunFrame(TimeSpan.FromMilliseconds(8));
            await WaitUntilAsync(() => HeldBackend.Waiting >= 2);
            scheduler.Update(layout.ScrollTo(new Viewport(0, 0, 500, 700, Zoom), 400));
        }
        finally
        {
            HeldBackend.Hold = false;
        }

        await WaitUntilAsync(() => !scheduler.RunFrame(TimeSpan.FromMilliseconds(8)) && HeldBackend.Waiting == 0);
        await WaitUntilAsync(() => session.Statistics.PendingOperations == 0);

        TileSchedulerEvent[] cancelled;
        lock (events)
        {
            cancelled = [.. events.Where(e => e.Kind == TileEventKind.Cancelled)];
        }

        Assert.Equal(2, cancelled.Length);
        Assert.All(cancelled, e => Assert.DoesNotContain(surface.Tiles, presented => presented.Tile == e.Tile));
        Assert.All(surface.Tiles, presented => Assert.InRange(presented.Tile.PageIndex, 390, 410));
        Assert.Equal(2, session.Statistics.TilesCancelled);
        Assert.Equal(0, scheduler.Statistics.Bytes - session.Document.CachedBytes - surface.Bytes);
    }

    [Fact]
    public async Task A_page_shows_its_placeholder_before_its_sharp_tiles_and_the_tiles_match_a_whole_render()
    {
        using var surface = new HeadlessSurface();
        byte[] file = ManyPages(3);
        await using DocumentSession session = DocumentSession.Open(file, new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().WithRenderAhead(0).WithNearMargin(0));

        scheduler.Update(new Viewport(0, 0, 400, 300, 1.5));
        while (scheduler.RunFrame(TimeSpan.FromMilliseconds(4)))
        {
        }

        PresentedTile[] page0 = [.. surface.Tiles.Where(t => t.Tile.PageIndex == 0)];
        Assert.Equal(TileSchedulerOptions.DefaultPlaceholderScale, page0[0].Tile.Scale);
        Assert.All(page0.Skip(1), t => Assert.Equal(1.5, t.Tile.Scale));
        Assert.True(page0.Length > 2);

        using PdfDocument reference = PdfDocument.Open(file);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap whole = rasterizer.Render(reference.Pages[0], new RenderOptions().WithScale(1.5));
        Assert.All(page0.Skip(1), t => Assert.InRange(ViewerTestSupport.MaxDifference(whole, t), 0, 1));
        Assert.True(scheduler.IsIdle);
    }

    [Fact]
    public async Task Render_ahead_follows_the_scroll_direction_and_tiles_on_the_surface_are_not_drawn_again()
    {
        using var surface = new HeadlessSurface();
        await using DocumentSession session = DocumentSession.Open(ManyPages(500), new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var events = new List<TileSchedulerEvent>();
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().UseObserver(events.Add));

        Viewport middle = layout.ScrollTo(new Viewport(0, 0, 500, 700, Zoom), 250);
        scheduler.Update(middle with { Y = middle.Y - 10 });
        scheduler.Update(middle);
        Settle(scheduler);
        Assert.All(AheadPages(events), page => Assert.True(page >= 250, $"ahead page {page} going down"));

        events.Clear();
        Viewport up = middle with { Y = middle.Y - 300 };
        scheduler.Update(up);
        Settle(scheduler);
        Assert.NotEmpty(AheadPages(events));
        Assert.All(AheadPages(events), page => Assert.True(page < 250, $"ahead page {page} going up"));

        events.Clear();
        scheduler.Update(middle);
        Settle(scheduler);
        Assert.DoesNotContain(events, e => e.Kind == TileEventKind.Started && e.Priority == TilePriority.Visible);
        Assert.Equal(scheduler.Statistics.ResidentTiles, surface.Tiles.Count);
    }

    [Fact]
    public async Task A_new_layout_keeps_the_tiles_on_the_surface()
    {
        using var surface = new HeadlessSurface();
        await using DocumentSession session = DocumentSession.Open(ManyPages(20), new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().WithRenderAhead(0));
        scheduler.Update(new Viewport(0, 0, 500, 700, Zoom));
        Settle(scheduler);
        long started = scheduler.Statistics.TilesStarted;

        PageLayout gapless = await PageLayout.CreateAsync(session, new PageLayoutOptions().WithGap(0), TestContext.Current.CancellationToken);
        scheduler.SetLayout(gapless);
        Assert.Same(gapless, scheduler.Layout);
        Settle(scheduler);
        Assert.True(scheduler.Statistics.TilesStarted - started < started, "most tiles survived the new layout");
    }

    [Fact]
    public async Task Frames_with_everything_on_the_surface_allocate_nothing()
    {
        using var surface = new HeadlessSurface();
        await using DocumentSession session = DocumentSession.Open(ManyPages(40), new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        using var scheduler = new TileScheduler(session, layout, surface);
        Viewport a = layout.ScrollTo(new Viewport(0, 0, 500, 700, Zoom), 10);
        Viewport b = a with { Y = a.Y + 150 };
        for (int i = 0; i < 3; i++)
        {
            scheduler.Update(a);
            Settle(scheduler);
            scheduler.Update(b);
            Settle(scheduler);
        }

        bool more = false;
        long allocated = Allocations.Measure(() =>
        {
            scheduler.Update(a);
            more |= scheduler.RunFrame(TimeSpan.FromMilliseconds(8));
            scheduler.Update(b);
            more |= scheduler.RunFrame(TimeSpan.FromMilliseconds(8));
            _ = scheduler.Statistics;
        });

        Assert.Equal(0, allocated);
        Assert.False(more);
    }

    [Fact]
    public async Task Arguments_are_checked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileSchedulerOptions().WithTileSize(8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileSchedulerOptions().WithMemoryBudget(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileSchedulerOptions().WithNearMargin(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileSchedulerOptions().WithRenderAhead(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileSchedulerOptions().WithPlaceholderScale(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileSchedulerOptions().WithMaxConcurrency(0));

        await using DocumentSession session = DocumentSession.Open(ManyPages(2));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        using var surface = new HeadlessSurface();
        var scheduler = new TileScheduler(session, layout, surface);
        Assert.False(scheduler.RunFrame(TimeSpan.FromMilliseconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Update(new Viewport(0, 0, 0, 10, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.RunFrame(TimeSpan.FromMilliseconds(-2)));
        scheduler.Dispose();
        Assert.Throws<ObjectDisposedException>(() => scheduler.Update(new Viewport(0, 0, 10, 10, 1)));
        Assert.False(scheduler.RunFrame(TimeSpan.FromMilliseconds(1)));
    }

    internal static byte[] ManyPages(int count) => ManyPagesDocument.Create(count);

    private static void Settle(TileScheduler scheduler)
    {
        while (scheduler.RunFrame(Timeout.InfiniteTimeSpan))
        {
        }
    }

    private static IEnumerable<int> AheadPages(List<TileSchedulerEvent> events) =>
        events.Where(e => e.Kind == TileEventKind.Started && e.Priority == TilePriority.Ahead).Select(e => e.Tile.PageIndex);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Scrolls a cooperative session through 500 pages in steps of 40% of a viewport with two jumps of 15 pages every 60 steps, one
    /// frame of about a tile's work per step and every fifth step until idle, checking at every trace event that a non-visible tile starts only once every visible tile is on the surface and
    /// measuring the memory held independently of the scheduler: the surface's pixels, the session's pixels and the document's caches.
    /// </summary>
    private static async Task<ScrollRun> ScrollAsync(long budget)
    {
        using var surface = new HeadlessSurface();
        await using DocumentSession session = DocumentSession.Open(
            ManyPages(500),
            new ViewerOptions().WithThreading(ViewerThreading.Cooperative).UseBackend(static () => new QuotaBackend()));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var run = new ScrollRun();
        var resident = new HashSet<Tile>();
        var visible = new List<Tile>();
        var buffer = new Tile[256];
        var pages = new VisiblePage[16];

        void Observe(TileSchedulerEvent e)
        {
            run.Events.Add(e);
            run.Measured.Add(surface.Bytes + session.Statistics.PixelBytes + session.Document.CachedBytes);
            if (e.Kind == TileEventKind.Presented)
            {
                resident.Add(e.Tile);
            }
            else if (e.Kind == TileEventKind.Evicted)
            {
                resident.Remove(e.Tile);
            }
            else if (e.Kind == TileEventKind.Started && e.Priority != TilePriority.Visible)
            {
                foreach (Tile tile in visible)
                {
                    if (!resident.Contains(tile))
                    {
                        run.Violations.Add($"{e.Priority} {e.Tile} started before visible {tile}");
                    }
                }
            }
        }

        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().WithMemoryBudget(budget).UseObserver(Observe));
        double end = layout.ContentHeight * Zoom;
        int step = 0;
        for (var viewport = new Viewport(0, 0, 500, 700, Zoom); viewport.Y < end; viewport = viewport with { Y = viewport.Y + 280 }, step++)
        {
            if (step % 60 is 30 or 31)
            {
                // Two drags of the scroll bar in a row: the first starts renders far ahead, the second leaves them behind.
                viewport = layout.ScrollTo(viewport, Math.Min(layout.PageCount - 1, layout.GetCurrentPage(viewport) + 15));
            }

            visible.Clear();
            int count = layout.GetVisiblePages(viewport, pages);
            for (int i = 0; i < count; i++)
            {
                int page = pages[i].PageIndex;
                visible.Add(new Tile(page, TileSchedulerOptions.DefaultPlaceholderScale, layout.GetPagePixels(page, TileSchedulerOptions.DefaultPlaceholderScale)));
            }

            count = layout.GetVisibleTiles(viewport, TileSchedulerOptions.DefaultTileSize, buffer);
            visible.AddRange(buffer.AsSpan(0, count));

            scheduler.Update(viewport);
            if (step % 5 == 4)
            {
                QuotaBackend.Quota = int.MaxValue;
                Settle(scheduler);
            }
            else
            {
                // A frame that can draw about a tile's worth of operations, so renders are in flight when the viewport moves on.
                QuotaBackend.Quota = 8;
                scheduler.RunFrame(TimeSpan.FromMilliseconds(1));
            }

            run.Measured.Add(surface.Bytes + session.Statistics.PixelBytes + session.Document.CachedBytes);
        }

        QuotaBackend.Quota = int.MaxValue;
        Settle(scheduler);
        run.Statistics = scheduler.Statistics;
        run.SurfaceBytes = surface.Bytes;
        return run;
    }

    private sealed class ScrollRun
    {
        public List<TileSchedulerEvent> Events { get; } = [];

        public List<long> Measured { get; } = [];

        public List<string> Violations { get; } = [];

        public TileSchedulerStatistics Statistics { get; set; }

        public long SurfaceBytes { get; set; }
    }

    /// <summary>A software backend that draws one operation per slice, and nothing once the frame's <see cref="Quota"/> of slices is spent.</summary>
    private sealed class QuotaBackend : ITileBackend
    {
        private readonly SoftwareTileBackend _inner = new();

        public static int Quota { get; set; } = int.MaxValue;

        public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken) =>
            new Render(_inner.Begin(list, target, deviceTransform, cancellationToken));

        public void Dispose() => _inner.Dispose();

        private sealed class Render(ITileRender inner) : ITileRender
        {
            public bool RenderSlice(TimeSpan budget)
            {
                if (Quota <= 0)
                {
                    return false;
                }

                Quota--;
                return inner.RenderSlice(TimeSpan.FromTicks(1));
            }

            public void Dispose() => inner.Dispose();
        }
    }

    /// <summary>A software backend whose renders make no progress while <see cref="Hold"/> is set.</summary>
    private sealed class HeldBackend : ITileBackend
    {
        private static int _waiting;
        private static volatile bool _hold;
        private readonly SoftwareTileBackend _inner = new();

        public static bool Hold
        {
            get => _hold;
            set => _hold = value;
        }

        public static int Waiting => Volatile.Read(ref _waiting);

        public static Func<ITileBackend> Factory(List<HeldBackend> created) => () =>
        {
            var backend = new HeldBackend();
            lock (created)
            {
                created.Add(backend);
            }

            return backend;
        };

        public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken) =>
            new Render(_inner.Begin(list, target, deviceTransform, cancellationToken), cancellationToken);

        public void Dispose() => _inner.Dispose();

        private sealed class Render(ITileRender inner, CancellationToken token) : ITileRender
        {
            public bool RenderSlice(TimeSpan budget)
            {
                if (budget != Timeout.InfiniteTimeSpan)
                {
                    return !_hold && inner.RenderSlice(budget);
                }

                Interlocked.Increment(ref _waiting);
                try
                {
                    while (_hold && !token.IsCancellationRequested)
                    {
                        Thread.Sleep(1);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _waiting);
                }

                return inner.RenderSlice(budget);
            }

            public void Dispose() => inner.Dispose();
        }
    }
}
