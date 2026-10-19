using System.Collections.Concurrent;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>Background sessions keep document work off the UI thread, and edits never race the reads on workers (issue #357).</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class OffUiThreadTests
{
    [Fact]
    public async Task A_background_frame_only_plans_and_every_tile_loads_builds_renders_and_presents_on_workers()
    {
        var threads = new ConcurrentBag<(string What, int Thread)>();
        var surface = new ThreadSurface(threads);
        await using DocumentSession session = DocumentSession.Open(
            ManyPagesDocument.Create(40),
            new ViewerOptions().WithThreading(ViewerThreading.Background).UseBackend(() => new ThreadBackend(threads)));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);

        // The "UI thread": a thread with its own context that counts every continuation posted back to it.
        var context = new CountingContext();
        int ui = 0;
        var frames = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            ui = Environment.CurrentManagedThreadId;
            using var scheduler = new TileScheduler(session, layout, surface);
            var viewport = new Viewport(0, 0, 500, 700, 0.5);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int step = 0; step < 6 && watch.Elapsed < TimeSpan.FromSeconds(60);)
            {
                scheduler.Update(viewport);
                if (!scheduler.RunFrame(TimeSpan.FromMilliseconds(8)) && scheduler.IsIdle)
                {
                    viewport = layout.ScrollTo(viewport, 3 * (++step));
                }

                Thread.Sleep(2);
            }
        });
        frames.Start();
        frames.Join();

        Assert.Equal(0, context.Posts);
        Assert.Contains(threads, t => t.What == "render");
        Assert.Contains(threads, t => t.What == "present");
        Assert.DoesNotContain(threads, t => t.What != "evict" && t.Thread == ui);
    }

    [Fact]
    public async Task An_edit_holds_the_reads_that_arrive_during_it_until_it_ends()
    {
        await using DocumentSession session = DocumentSession.Open(
            ManyPagesDocument.Create(3),
            new ViewerOptions().WithThreading(ViewerThreading.Background));
        ValueTask<DisplayList> pending;
        Task<DisplayList> list;
        using (session.BeginEdit())
        {
            pending = session.GetDisplayListAsync(1, DisplayLayers.All, TestContext.Current.CancellationToken);
            list = pending.AsTask();
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(list.IsCompleted);
        }

        Assert.True((await list.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).OperationCount > 0);
    }

    [Fact]
    public async Task A_cooperative_session_edits_without_waiting()
    {
        await using DocumentSession session = DocumentSession.Open(
            ManyPagesDocument.Create(2),
            new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
        using (session.BeginEdit())
        {
            DisplayList list = await session.GetDisplayListAsync(0, DisplayLayers.All, TestContext.Current.CancellationToken);
            Assert.True(list.OperationCount > 0);
        }
    }

    [Fact]
    public async Task A_remote_renderer_draws_the_content_tiles_and_the_scheduler_draws_the_overlays_itself()
    {
#pragma warning disable CA2000 // the session owns and disposes it (checked below)
        var remote = new FakeRemote();
#pragma warning restore CA2000
        using var surface = new HeadlessSurface();
        var local = new List<CountingBackend>();
        await using DocumentSession session = DocumentSession.Open(
            Corpus.Bytes("acroform-fields.pdf"),
            ViewerTestSupport.OptionsFor("acroform-fields.pdf", ViewerThreading.Cooperative).UseBackend(CountingBackend.Factory(local)).UseRemoteRenderer(remote));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().WithSeparateOverlays());
        scheduler.Update(new Viewport(0, 0, 600, 800, 1));
        await SettleAsync(scheduler);

        // Content tiles come from the remote renderer (its pixels), overlays from the session's own backend; at most two wait on it.
        Assert.NotEmpty(remote.Rendered);
        Assert.All(remote.Rendered, tile => Assert.Equal(DisplayLayers.Content, tile.Layers));
        Assert.InRange(remote.MostAtOnce, 1, remote.MaxConcurrency);
        PresentedTile[] tiles = [.. surface.Tiles];
        Assert.All(tiles.Where(t => t.Tile.Layers == DisplayLayers.Content), t => Assert.Equal(FakeRemote.Marker, t.Pixels.GetRow(0)[0]));
        Assert.Contains(tiles, t => (t.Tile.Layers & DisplayLayers.Content) == 0);
        Assert.True(local.Sum(b => b.Slices) > 0);
        Assert.False(remote.IsDisposed);
        await session.DisposeAsync();
        Assert.True(remote.IsDisposed);
    }

    [Fact]
    public async Task A_remote_tile_cancelled_by_a_scroll_never_reaches_the_surface_and_empty_overlays_are_never_drawn()
    {
#pragma warning disable CA2000 // the session owns and disposes it
        var remote = new FakeRemote { Hold = true };
#pragma warning restore CA2000
        using var surface = new HeadlessSurface();
        await using DocumentSession session = DocumentSession.Open(
            ManyPagesDocument.Create(40),
            new ViewerOptions().WithThreading(ViewerThreading.Cooperative).UseRemoteRenderer(remote));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var events = new List<TileSchedulerEvent>();
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().WithSeparateOverlays().UseObserver(e =>
        {
            lock (events)
            {
                events.Add(e);
            }
        }));
        var viewport = new Viewport(0, 0, 500, 700, 0.5);
        scheduler.Update(viewport);

        // An unbounded frame starts everything it can (no time budget to run out of on a slow runner): the remote renderer takes as
        // many tiles as it accepts at once, synchronously, and holds them.
        scheduler.RunFrame(Timeout.InfiniteTimeSpan);
        Assert.Equal(remote.MaxConcurrency, remote.Waiting);
        scheduler.Update(layout.ScrollTo(viewport, 30));
        scheduler.RunFrame(Timeout.InfiniteTimeSpan);
        remote.Hold = false;
        await SettleAsync(scheduler);

        TileSchedulerEvent[] cancelled;
        lock (events)
        {
            cancelled = [.. events.Where(e => e.Kind == TileEventKind.Cancelled)];
        }

        Assert.NotEmpty(cancelled);
        Assert.All(cancelled, e => Assert.DoesNotContain(surface.Tiles, t => t.Tile == e.Tile));

        // These pages have no annotations: their overlay tiles are resident without pixels, never on the surface.
        Assert.DoesNotContain(surface.Tiles, t => (t.Tile.Layers & DisplayLayers.Content) == 0);
        Assert.True(scheduler.IsIdle);
    }

    private static async Task SettleAsync(TileScheduler scheduler)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(60) && (scheduler.RunFrame(TimeSpan.FromMilliseconds(8)) || !scheduler.IsIdle))
        {
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A remote renderer that "renders" on the thread pool: content tiles filled with a marker byte.</summary>
    private sealed class FakeRemote : IRemoteTileRenderer
    {
        public const byte Marker = 0x5A;
        private int _running;
        private int _mostAtOnce;
        private volatile bool _hold;

        public ConcurrentBag<Tile> Rendered { get; } = [];

        public int MaxConcurrency => 2;

        public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

        public int Waiting => Volatile.Read(ref _running);

        public bool Hold
        {
            get => _hold;
            set => _hold = value;
        }

        public bool IsDisposed { get; private set; }

        public bool CanRender(in Tile tile) => (tile.Layers & ~DisplayLayers.Content) == 0;

        public async Task RenderAsync(Tile tile, IRenderSurface surface, CancellationToken cancellationToken)
        {
            int running = Interlocked.Increment(ref _running);
            InterlockedMax(ref _mostAtOnce, running);
            try
            {
                while (_hold)
                {
                    await Task.Delay(1, cancellationToken);
                }

                await Task.Delay(1, cancellationToken);
                using var pixels = new Bitmap(tile.Region.Width, tile.Region.Height);
                pixels.Pixels.Fill(Marker);
                Rendered.Add(tile);
                surface.Present(tile, pixels);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        public void Dispose() => IsDisposed = true;

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    private sealed class CountingContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    private sealed class ThreadSurface(ConcurrentBag<(string What, int Thread)> threads) : IRenderSurface
    {
        public void Present(Tile tile, Bitmap pixels) => threads.Add(("present", Environment.CurrentManagedThreadId));

        public void Evict(Tile tile) => threads.Add(("evict", Environment.CurrentManagedThreadId));
    }

    private sealed class ThreadBackend(ConcurrentBag<(string What, int Thread)> threads) : ITileBackend
    {
        private readonly SoftwareTileBackend _inner = new();

        public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken)
        {
            threads.Add(("render", Environment.CurrentManagedThreadId));
            return _inner.Begin(list, target, deviceTransform, cancellationToken);
        }

        public void Dispose() => _inner.Dispose();
    }
}
