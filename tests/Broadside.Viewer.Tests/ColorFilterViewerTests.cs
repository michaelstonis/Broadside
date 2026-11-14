using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>
/// Smart dark mode in the viewer (spec #33 viewer capability 9): the session draws its tiles with its color filter, turning it on or
/// off builds no display list again, and a backend or remote renderer that cannot draw the filter falls back to the software path.
/// </summary>
public sealed class ColorFilterViewerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ViewerThreading.Cooperative)]
    [InlineData(ViewerThreading.Background)]
    public async Task Toggling_dark_mode_redraws_image_smask_pdf_from_the_same_display_list_with_its_image_pixels_unchanged(ViewerThreading threading)
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("image-smask.pdf"), new ViewerOptions().WithThreading(threading));
        using var surface = new HeadlessSurface();
        DisplayList list = await session.GetDisplayListAsync(0, cancellationToken: Token);
        long builds = session.Document.CacheStatistics.DisplayLists.Misses;

        await session.RenderPageAsync(0, 1, surface, Token);
        using Bitmap light = Copy(surface.Tiles[0].Pixels);
        session.ColorFilter = ColorFilter.DarkMode;
        await session.RenderPageAsync(0, 1, surface, Token);
        using Bitmap dark = Copy(surface.Tiles[0].Pixels);
        session.ColorFilter = ColorFilter.None;
        await session.RenderPageAsync(0, 1, surface, Token);

        Assert.Same(list, await session.GetDisplayListAsync(0, cancellationToken: Token));
        Assert.Equal(builds, session.Document.CacheStatistics.DisplayLists.Misses);
        Assert.Equal(light.Pixels.ToArray(), surface.Tiles[0].Pixels.Pixels.ToArray());

        using PdfDocument reference = await PdfDocument.OpenAsync(Corpus.Bytes("image-smask.pdf"), Token);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(reference.Pages[0], new RenderOptions().WithColorFilter(ColorFilter.DarkMode));
        Assert.Equal(expected.Pixels.ToArray(), dark.Pixels.ToArray());

        // The image's pixels (device x 72 to 172, y 92 to 192) are those of light mode; the paper around them turned black.
        Assert.Equal(light.GetRow(120).Slice(72 * 4, 100 * 4).ToArray(), dark.GetRow(120).Slice(72 * 4, 100 * 4).ToArray());
        Assert.Equal([0, 0, 0, 255], dark.GetRow(400).Slice(300 * 4, 4).ToArray());
    }

    [Fact]
    public async Task The_options_set_the_first_filter_and_a_scheduler_redraws_its_tiles_after_a_change()
    {
        using var surface = new HeadlessSurface();
        await using DocumentSession session = DocumentSession.Open(
            Corpus.Bytes("paths-fill-rules.pdf"),
            new ViewerOptions().WithThreading(ViewerThreading.Cooperative).WithColorFilter(ColorFilter.DarkMode));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().WithRenderAhead(0).WithNearMargin(0));
        Assert.Equal(ColorFilter.DarkMode, session.ColorFilter);
        Assert.Equal(ColorFilter.None, new ViewerOptions().ColorFilter);

        scheduler.Update(new Viewport(0, 0, 400, 300, 1));
        Settle(scheduler);
        PresentedTile darkTile = surface.Tiles.First(t => t.Tile.Scale == 1 && t.Tile.Region.X == 0 && t.Tile.Region.Y == 0);
        Assert.Equal([0, 0, 0, 255], darkTile.Pixels.GetRow(0).Slice(0, 4).ToArray()); // the paper's corner
        long builds = session.Document.CacheStatistics.DisplayLists.Misses;

        session.ColorFilter = ColorFilter.None;
        scheduler.Invalidate(0, DisplayLayers.All);
        Settle(scheduler);

        PresentedTile lightTile = surface.Tiles.First(t => t.Tile.Scale == 1 && t.Tile.Region.X == 0 && t.Tile.Region.Y == 0);
        Assert.Equal([255, 255, 255, 255], lightTile.Pixels.GetRow(0).Slice(0, 4).ToArray());
        Assert.Equal(builds, session.Document.CacheStatistics.DisplayLists.Misses);
    }

    [Theory]
    [InlineData(ViewerThreading.Cooperative)]
    [InlineData(ViewerThreading.Background)]
    public async Task A_backend_that_cannot_draw_the_filter_falls_back_to_the_software_path(ViewerThreading threading)
    {
        var created = new List<CountingBackend>();
        await using DocumentSession session = DocumentSession.Open(
            Corpus.Bytes("image-smask.pdf"),
            new ViewerOptions().WithThreading(threading).UseBackend(CountingBackend.Factory(created)).WithColorFilter(ColorFilter.DarkMode));
        using var surface = new HeadlessSurface();

        await session.RenderPageAsync(0, 1, surface, Token);

        using PdfDocument reference = await PdfDocument.OpenAsync(Corpus.Bytes("image-smask.pdf"), Token);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(reference.Pages[0], new RenderOptions().WithColorFilter(ColorFilter.DarkMode));
        Assert.Equal(expected.Pixels.ToArray(), surface.Tiles[0].Pixels.Pixels.ToArray());
        Assert.All(created, backend => Assert.Equal(0, backend.Slices)); // its own Begin was never asked to draw
    }

    [Fact]
    public async Task A_remote_renderer_that_cannot_draw_the_filter_leaves_filtered_tiles_to_the_session()
    {
        using var remote = new UnfilteredRemote();
        var tile = new Tile(0, 1, new PixelRectangle(0, 0, 10, 10));
        IRemoteTileRenderer renderer = remote;

        Assert.True(renderer.CanRender(tile, ColorFilter.None));
        Assert.False(renderer.CanRender(tile, ColorFilter.DarkMode));
        await renderer.RenderAsync(tile, ColorFilter.DarkMode, new NullSurface(), Token);
        Assert.Equal(0, remote.Renders);
        await renderer.RenderAsync(tile, ColorFilter.None, new NullSurface(), Token);
        Assert.Equal(1, remote.Renders);
    }

    private static void Settle(TileScheduler scheduler)
    {
        while (scheduler.RunFrame(TimeSpan.FromMilliseconds(50)))
        {
        }
    }

    private static Bitmap Copy(Bitmap source)
    {
        var copy = new Bitmap(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        {
            source.GetRow(y).CopyTo(copy.GetRow(y));
        }

        return copy;
    }

    /// <summary>A remote renderer written before color filters: it implements only the members without a filter.</summary>
    private sealed class UnfilteredRemote : IRemoteTileRenderer
    {
        public int Renders;

        public int MaxConcurrency => 1;

        public bool CanRender(in Tile tile) => true;

        public Task RenderAsync(Tile tile, IRenderSurface surface, CancellationToken cancellationToken)
        {
            Renders++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
