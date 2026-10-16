using Broadside.GoldenImages;
using Broadside.Rendering;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

public sealed class DocumentSessionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Page_geometry_is_known_once_a_page_is_loaded_and_follows_rotation_and_crop_box()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("page-rotation.pdf"));

        Assert.False(session.TryGetPageMetrics(0, out _));
        PageMetrics first = await session.GetPageMetricsAsync(0, Token);
        PageMetrics fourth = await session.GetPageMetricsAsync(3, Token);

        // Page 1: 200 x 100 crop box turned 90 degrees displays 100 wide, 200 high; page 4: crop box 20 10 180 90, unrotated, UserUnit 2.
        Assert.Equal((90, 100.0, 200.0), (first.Rotation, first.Width, first.Height));
        Assert.Equal((0, 320.0, 160.0, new PdfRectangle(20, 10, 180, 90)), (fourth.Rotation, fourth.Width, fourth.Height, fourth.CropBox));
        Assert.True(session.TryGetPageMetrics(0, out PageMetrics cached));
        Assert.Equal(first, cached);
        Assert.False(session.TryGetPageMetrics(1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.TryGetPageMetrics(4, out _));
    }

    [Theory]
    [InlineData(ViewerThreading.Background)]
    [InlineData(ViewerThreading.Cooperative)]
    public async Task Tiles_of_a_page_share_one_display_list_and_together_equal_its_whole_render(ViewerThreading threading)
    {
        const string fileName = "text-truetype-embedded.pdf";
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes(fileName), ViewerTestSupport.OptionsFor(fileName, threading));
        Tile page = await session.GetPageTileAsync(0, 2, 0, Token);
        using var surface = new HeadlessSurface();

        int half = page.Region.Width / 2, middle = page.Region.Height / 2;
        Tile[] tiles =
        [
            page with { Region = new PixelRectangle(0, 0, half, middle) },
            page with { Region = new PixelRectangle(half, 0, page.Region.Width - half, middle) },
            page with { Region = new PixelRectangle(0, middle, half, page.Region.Height - middle) },
            page with { Region = new PixelRectangle(half, middle, page.Region.Width - half, page.Region.Height - middle) },
        ];
        await Task.WhenAll(tiles.Select(tile => session.RenderTileAsync(tile, surface, Token)));

        using PdfDocument reference = GoldenCorpus.Open(fileName);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap whole = rasterizer.Render(reference.Pages[0], new RenderOptions().WithScale(2));
        Assert.Equal(4, surface.Tiles.Count);
        Assert.All(surface.Tiles, tile => Assert.InRange(ViewerTestSupport.MaxDifference(whole, tile), 0, 1));
        PdfCacheStatistics lists = session.Document.CacheStatistics.DisplayLists;
        Assert.Equal(1, lists.Count);
        Assert.Equal(1, lists.Misses);
    }

    [Fact]
    public async Task A_requester_that_stops_waiting_leaves_the_shared_build_to_the_others()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerOptions options = new ViewerOptions().WithThreading(ViewerThreading.Cooperative)
            .UseYield(async cancellationToken => await release.Task.WaitAsync(cancellationToken));
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("paths-fill-rules.pdf"), options);
        using var first = CancellationTokenSource.CreateLinkedTokenSource(Token);

        Task<DisplayList> abandoned = session.GetDisplayListAsync(0, DisplayLayers.All, first.Token).AsTask();
        Task<DisplayList> kept = session.GetDisplayListAsync(0, DisplayLayers.All, Token).AsTask();
        await first.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        release.SetResult();

        Assert.True((await kept).OperationCount > 0);
        Assert.Equal(1, session.Document.CacheStatistics.DisplayLists.Misses);
    }

    [Fact]
    public async Task A_cooperative_session_renders_in_slices_and_yields_between_them()
    {
        const string fileName = "paths-fill-rules.pdf";
        var backends = new List<CountingBackend>();
        int yields = 0;
        ViewerOptions options = ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Cooperative)
            .WithSliceBudget(TimeSpan.FromTicks(1))
            .UseBackend(CountingBackend.Factory(backends))
            .UseYield(_ =>
            {
                yields++;
                return ValueTask.CompletedTask;
            });
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes(fileName), options);
        using var surface = new HeadlessSurface();

        await session.RenderPageAsync(0, 1, surface, Token);

        CountingBackend renderer = Assert.Single(backends);
        Assert.True(renderer.Slices > 2, $"{renderer.Slices} slices");
        Assert.Equal(renderer.Slices, yields); // one yield before the build, one between each pair of slices
        Assert.Single(surface.Tiles);
    }

    [Fact]
    public async Task A_cancelled_tile_never_reaches_the_surface_and_returns_its_pixels()
    {
        const string fileName = "paths-fill-rules.pdf";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        int yields = 0;
        ViewerOptions options = ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Cooperative)
            .WithSliceBudget(TimeSpan.FromTicks(1))
            .UseYield(async _ =>
            {
                // The second yield is between two drawing slices: the tile's pixels are held when the token fires.
                if (++yields == 2)
                {
                    await cancellation.CancelAsync();
                }
            });
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes(fileName), options);
        Tile tile = await session.GetPageTileAsync(0, 1, 0, Token);
        using var surface = new HeadlessSurface();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RenderTileAsync(tile, surface, cancellation.Token));

        Assert.Empty(surface.Tiles);
        Assert.Equal(new SessionStatistics(0, 0, 0, 1), session.Statistics);
        await session.RenderTileAsync(tile, surface, Token);
        Assert.Single(surface.Tiles);
    }

    [Fact]
    public async Task Closing_the_session_cancels_pending_work_and_releases_backends_and_document()
    {
        const string fileName = "paths-fill-rules.pdf";
        var backends = new List<CountingBackend>();
        var drawing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int yields = 0;
        ViewerOptions options = ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Cooperative)
            .WithSliceBudget(TimeSpan.FromTicks(1))
            .UseBackend(CountingBackend.Factory(backends))
            .UseYield(async cancellationToken =>
            {
                if (++yields == 2)
                {
                    // Mid-render: park here until the session closes.
                    drawing.SetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
            });
        DocumentSession session = DocumentSession.Open(Corpus.Bytes(fileName), options);
        Tile tile = await session.GetPageTileAsync(0, 1, 0, Token);
        using var surface = new HeadlessSurface();
        Task render = session.RenderTileAsync(tile, surface, Token);
        await drawing.Task.WaitAsync(Token);
        Assert.Equal(1, session.Statistics.PendingOperations);
        Assert.True(session.Statistics.PixelBytes > 0);

        await session.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render);
        Assert.Empty(surface.Tiles);
        Assert.Equal(new SessionStatistics(0, 0, 0, 1), session.Statistics);
        Assert.True(session.IsClosed);
        Assert.True(Assert.Single(backends).IsDisposed);
        Assert.Equal(0, session.Document.CacheStatistics.DisplayLists.Count); // the document was disposed, which clears its caches
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.RenderTileAsync(tile, surface, Token));
    }

    [Fact]
    public async Task A_synchronous_close_lets_pending_work_finish_cancelling_then_releases()
    {
        const string fileName = "paths-fill-rules.pdf";
        var backends = new List<CountingBackend>();
        var drawing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int yields = 0;
        ViewerOptions options = ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Background)
            .UseBackend(CountingBackend.Factory(backends))
            .WithThreading(ViewerThreading.Cooperative)
            .WithSliceBudget(TimeSpan.FromTicks(1))
            .UseYield(async cancellationToken =>
            {
                if (++yields == 2)
                {
                    drawing.SetResult();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
            });
        DocumentSession session = DocumentSession.Open(Corpus.Bytes(fileName), options);
        Task render = session.RenderPageAsync(0, 1, new NullSurface(), Token);
        await drawing.Task.WaitAsync(Token);

#pragma warning disable CA1849 // the synchronous close is what this test exercises
        session.Dispose();
#pragma warning restore CA1849

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render);
        await session.DisposeAsync(); // completes once released
        Assert.Equal(0, session.Statistics.PixelBytes);
        Assert.True(Assert.Single(backends).IsDisposed);
    }

    [Fact]
    public async Task A_remote_document_renders_without_synchronous_fetches_as_in_the_browser()
    {
        const string fileName = "linearized.pdf";
        using var server = new RangeServer(Corpus.Bytes(fileName));
        using HttpClient client = server.CreateClient();
        ViewerOptions options = ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Cooperative);
        options.DocumentOptions!.WithSynchronousRangeFetch(false);

        await using DocumentSession session = await DocumentSession.OpenAsync(client, new Uri("https://example.test/linearized.pdf"), options, Token);
        using var surface = new HeadlessSurface();
        await session.RenderPageAsync(1, 1, surface, Token);

        using PdfDocument reference = GoldenCorpus.Open(fileName);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(reference.Pages[1], GoldenCorpus.CreateRenderOptions());
        Assert.Equal(0, ViewerTestSupport.MaxDifference(expected, Assert.Single(surface.Tiles).Pixels));
        Assert.True(session.TryGetPageMetrics(1, out _));
    }

    [Fact]
    public async Task Invalid_tiles_and_pages_are_refused()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("paths-fill-rules.pdf"));
        var surface = new NullSurface();

        await Assert.ThrowsAsync<ArgumentException>(() => session.RenderTileAsync(new Tile(0, 1, new PixelRectangle(0, 0, 0, 10)), surface, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => session.RenderTileAsync(new Tile(0, 0, new PixelRectangle(0, 0, 10, 10)), surface, Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.RenderPageAsync(5, 1, surface, Token));
        Assert.Equal(new SessionStatistics(0, 0, 0, 0), session.Statistics);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewerOptions().WithMaxConcurrency(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewerOptions().WithSliceBudget(TimeSpan.Zero));
    }
}
