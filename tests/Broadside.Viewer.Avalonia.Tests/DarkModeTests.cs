using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer.Avalonia.Tests;

/// <summary>
/// Dark mode through the Avalonia viewer: a change draws every tile again from the cached display lists. The test counts
/// the lists the document's cache builds, so it runs in <see cref="HeavyTestCollection"/>, where no parallel test makes them stale.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DarkModeTests
{
    [Fact]
    public Task Dark_mode_draws_dark_paper_from_the_cached_display_lists() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("text-standard14.pdf"));
        LayoutRectangle page = viewer.Layout.PageToViewport(viewer.Viewport, 0, viewer.Layout.GetPageMetrics(0).CropBox);
        int x = (int)page.X + 6;
        int y = (int)page.Y + 6;
        Assert.Equal((255, 255, 255), Rgb(viewer.Capture()[x, y]));
        PdfCacheStatistics before = viewer.Controller.Session!.Document.CacheStatistics.DisplayLists;

        viewer.Viewer.ColorFilter = ColorFilter.DarkMode;
        await viewer.SettleAsync();
        FramePixels dark = viewer.Capture();
        (int r, int g, int b) paper = Rgb(dark[x, y]);
        Assert.True(paper.r < 40 && paper.g < 40 && paper.b < 40, $"paper {paper}");
        Assert.Equal((0x2B, 0x2B, 0x2B), Rgb(dark[1, 1]));
        PdfCacheStatistics after = viewer.Controller.Session.Document.CacheStatistics.DisplayLists;
        Assert.True(
            after.Misses == before.Misses && after.Hits > before.Hits,
            $"display lists before {before}, after {after}: dark mode built a list again");
        PinnedSubsetTests.AssertTilesOnScreen(viewer, 0);

        viewer.Viewer.ColorFilter = ColorFilter.None;
        await viewer.SettleAsync();
        Assert.Equal((255, 255, 255), Rgb(viewer.Capture()[x, y]));

        static (int, int, int) Rgb((byte B, byte G, byte R, byte A) p) => (p.R, p.G, p.B);
    });
}
