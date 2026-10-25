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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(29)]
    public async Task A_remote_linearized_page_loads_its_links_text_and_accessible_tree_without_synchronous_fetches(int pageIndex)
    {
        // Issue #410: in the browser, a remote linearized file's text and accessibility loads asked for the page's index through the
        // page tree, which needs the main cross-reference section at the end of the file: every page's load failed with a
        // PdfRangeNotLoadedException, which the Blazor sample showed as a message (and the message's resize stopped a scroll).
        // The pages are padded so that the file is far larger than the first request: the main cross-reference section stays unread.
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(' ', Enumerable.Range(0, 30).Select(static page => $"{4 + (2 * page)} 0 R")) + "] /Count 30 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        for (int i = 0; i < 30; i++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + (2 * i)} 0 R >>");
            string content = $"BT /F1 36 Tf 72 560 Td (Page {i + 1}) Tj ET\n" + string.Concat(Enumerable.Repeat("%" + new string('x', 78) + "\n", 400));
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        using PdfDocument source = PdfDocument.Open(new TestPdf().Build([.. objects]));
        using var linearized = new MemoryStream();
        await source.SaveAsync(linearized, new PdfSaveOptions().WithLinearize(true).WithStreamCompression(PdfStreamCompression.None), Token);
        using var server = new RangeServer(linearized.ToArray());
        using HttpClient client = server.CreateClient();
        var options = new ViewerOptions().WithThreading(ViewerThreading.Cooperative).UseEngine(new PdfEngine(new PdfOptions().WithSynchronousRangeFetch(false)));
        await using DocumentSession session = await DocumentSession.OpenAsync(client, new Uri("https://example.test/many.pdf"), options, Token);

        await new AnnotationLayer(session).LoadPageAsync(pageIndex, Token);
        SelectableText text = await new TextSelection(session).LoadPageAsync(pageIndex, Token);
        AccessibleNode page = await new AccessibilityBridge(session).LoadPageAsync(pageIndex, Token);

        Assert.True(session.Document.IsLinearized);
        Assert.Equal(pageIndex, session.Document.Pages.IndexOf(text.Page));
        Assert.Contains($"Page {pageIndex + 1}", text.Page.ExtractText().Text, StringComparison.Ordinal);
        Assert.Equal(pageIndex, page.PageIndex);
        Assert.Empty(session.Document.Diagnostics);
    }

    [Theory]
    [InlineData(ViewerThreading.Cooperative)]
    [InlineData(ViewerThreading.Background)]
    public async Task A_remote_linearized_tagged_page_loads_its_accessible_tree_and_links_without_synchronous_fetches(ViewerThreading threading)
    {
        // Issue #417: the structure tree of a linearized file lies after its pages (Annex F, F.3.10), so a page's prefetch does not
        // reach it, and its elements' Pg entries named pages the hints had not handed out, which walked the page tree. In the browser
        // both needed the main cross-reference section at the end of the file synchronously, and the page's load failed.
        using var server = new RangeServer(TaggedLinearizedFile.Create());
        using HttpClient client = server.CreateClient();
        var options = new ViewerOptions().WithThreading(threading).UseEngine(new PdfEngine(new PdfOptions().WithSynchronousRangeFetch(false)));
        await using DocumentSession session = await DocumentSession.OpenAsync(client, new Uri("https://example.test/tagged.pdf"), options, Token);
        var bridge = new AccessibilityBridge(session);
        var layer = new AnnotationLayer(session);

        foreach (int pageIndex in TaggedLinearizedFile.StructurePages)
        {
            AccessibleNode page = await bridge.LoadPageAsync(pageIndex, Token);
            SelectableText text = await new TextSelection(session).LoadPageAsync(pageIndex, Token);
            Assert.Equal(pageIndex, page.PageIndex);
            Assert.Equal(pageIndex, session.Document.Pages.IndexOf(text.Page));
        }

        // The link on page 1 goes to page 25, which no load has handed out: its page comes from the hints.
        await layer.LoadPageAsync(0, Token);
        var link = (Annotations.PdfLinkAnnotation)session.Document.Pages[0].Annotations[0];
        Assert.Equal(TaggedLinearizedFile.LinkedPage, Assert.IsType<PdfExplicitDestination>(link.Destination).PageIndex);
        Assert.Empty(session.Document.Diagnostics);
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
