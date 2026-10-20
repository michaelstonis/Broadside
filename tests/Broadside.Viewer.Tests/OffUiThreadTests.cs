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

    [Fact]
    public async Task A_page_read_during_an_edit_waits_for_it_and_takes_its_geometry_from_inside_the_read()
    {
        // Review #364 (P1): the page's geometry is read under the read lease, so an edit of it never races the read.
        await using DocumentSession session = DocumentSession.Open(
            ManyPagesDocument.Create(3),
            new ViewerOptions().WithThreading(ViewerThreading.Background));
        var cropped = new PdfRectangle(10, 20, 300, 400);
        Task<PageMetrics> metrics;
        using (session.BeginEdit())
        {
            metrics = session.GetPageMetricsAsync(2, TestContext.Current.CancellationToken).AsTask();
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(metrics.IsCompleted);
            session.Document.Pages[2].CropBox = cropped;
        }

        PageMetrics read = await metrics.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(cropped, read.CropBox);
    }

    [Fact]
    public async Task An_edit_stops_a_remote_prefetch_in_flight_and_the_prefetch_runs_again_after_it()
    {
        // Review #364 (P1): a background session's remote prefetch is a read: an edit cancels it rather than racing it.
        byte[] file = SpreadContents(40);
        using var server = new Broadside.Tests.Document.RangeServer(file);
        using var held = new HoldingHandler(server);
        using var client = new HttpClient(held, disposeHandler: false);
        await using DocumentSession session = await DocumentSession.OpenAsync(
            client,
            new Uri("https://example.test/many.pdf"),
            new ViewerOptions().WithThreading(ViewerThreading.Background),
            TestContext.Current.CancellationToken);
        held.Hold = true;
        Task<DisplayList> metrics = session.GetDisplayListAsync(20, DisplayLayers.All, TestContext.Current.CancellationToken).AsTask();
        await WaitUntilAsync(() => held.Waiting > 0);

        var edit = Task.Run(() => session.BeginEdit(), TestContext.Current.CancellationToken);
        IDisposable scope = await edit.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.True(held.Cancelled > 0, "the edit cancelled the prefetch's range request");
        Assert.False(metrics.IsCompleted);
        held.Hold = false;
        scope.Dispose();
        Assert.True((await metrics.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).OperationCount > 0);
    }

    /// <summary>Pages whose content streams lie past what opening reads (8 KB apart), so loading a page fetches its content remotely.</summary>
    private static byte[] SpreadContents(int count)
    {
        var pdf = new System.Text.StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        void Add(string body)
        {
            offsets.Add(pdf.Length);
            pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        Add("<< /Type /Catalog /Pages 2 0 R >>");
        Add($"<< /Type /Pages /Count {count} /Kids [{string.Join(" ", Enumerable.Range(0, count).Select(i => $"{3 + i} 0 R"))}] >>");
        for (int i = 0; i < count; i++)
        {
            Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {3 + count + i} 0 R >>");
        }

        string padding = "%" + new string('x', 8000) + "\n";
        for (int i = 0; i < count; i++)
        {
            string content = padding + "0.2 0.4 0.8 rg 72 72 300 400 re f";
            Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        int xref = pdf.Length;
        pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    [Fact]
    public async Task A_tile_the_remote_renderer_gives_back_is_drawn_by_the_scheduler_itself()
    {
        // Review #364: a worker that stops (or cannot open the document) completes its tiles without presenting them, and stops
        // accepting tiles; the scheduler then draws them on its own thread instead of leaving them failed.
#pragma warning disable CA2000 // the session owns and disposes it
        var remote = new FakeRemote { GiveBack = true };
#pragma warning restore CA2000
        using var surface = new HeadlessSurface();
        await using DocumentSession session = DocumentSession.Open(
            ManyPagesDocument.Create(3),
            new ViewerOptions().WithThreading(ViewerThreading.Cooperative).UseRemoteRenderer(remote));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().WithSeparateOverlays());
        scheduler.Update(new Viewport(0, 0, 500, 700, 0.5));
        await SettleAsync(scheduler);

        Assert.True(remote.GivenBack > 0);
        Assert.Empty(remote.Rendered);
        Assert.NotEmpty(surface.Tiles);
        Assert.All(surface.Tiles, t => Assert.NotEqual(FakeRemote.Marker, t.Pixels.GetRow(0)[0]));
        Assert.True(scheduler.IsIdle);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "timed out");
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Holds range requests while <see cref="Hold"/> is set, counting those waiting and those cancelled.</summary>
    private sealed class HoldingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _waiting;
        private int _cancelled;
        private volatile bool _hold;

        public bool Hold
        {
            get => _hold;
            set => _hold = value;
        }

        public int Waiting => Volatile.Read(ref _waiting);

        public int Cancelled => Volatile.Read(ref _cancelled);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _waiting);
            try
            {
                while (_hold)
                {
                    await Task.Delay(1, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelled);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _waiting);
            }

            return await base.SendAsync(request, cancellationToken);
        }
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
        private int _givenBack;
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

        /// <summary>Gets or sets whether the renderer has stopped: it gives every tile back and then declines new ones.</summary>
        public bool GiveBack { get; set; }

        public int GivenBack => Volatile.Read(ref _givenBack);

        public bool CanRender(in Tile tile) => (tile.Layers & ~DisplayLayers.Content) == 0 && (!GiveBack || Volatile.Read(ref _givenBack) == 0);

        public async Task RenderAsync(Tile tile, IRenderSurface surface, CancellationToken cancellationToken)
        {
            if (GiveBack)
            {
                await Task.Delay(1, cancellationToken);
                Interlocked.Increment(ref _givenBack);
                return; // completed without presenting: the tile goes back to the scheduler
            }

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
