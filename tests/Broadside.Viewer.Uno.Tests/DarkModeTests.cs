using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer.Uno.Tests;

/// <summary>
/// Dark mode through the Uno shell's controller: a change draws every tile again from the cached display lists. The test counts
/// the lists the document's cache builds, so it runs in <see cref="HeavyTestCollection"/>, where no parallel test makes them stale.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DarkModeTests
{
    [Fact]
    public void Dark_mode_redraws_the_tiles_from_the_cached_lists_with_black_paper_and_back() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        LayoutRectangle page = shell.Layout.PageToViewport(shell.Viewport, 0, shell.Layout.GetPageMetrics(0).CropBox);
        PdfCacheStatistics before = shell.Controller.Session!.Document.CacheStatistics.DisplayLists;

        shell.Controller.SetColorFilter(ColorFilter.DarkMode);
        await shell.SettleAsync();
        Assert.True(shell.Controller.LatestFrame.IsDark);
        Raster dark = shell.Draw();
        Assert.Equal(ViewerColors.DarkBackground, dark[1, 1]);
        (int darkSamples, int whiteSamples) = ViewerControllerTests.Count(dark, page);
        Assert.True(darkSamples > whiteSamples * 3, $"dark {darkSamples}, white {whiteSamples}");
        PdfCacheStatistics after = shell.Controller.Session.Document.CacheStatistics.DisplayLists;
        Assert.True(
            after.Misses == before.Misses && after.Hits > before.Hits,
            $"display lists before {before}, after {after}: dark mode built a list again");

        shell.Controller.SetColorFilter(ColorFilter.None);
        await shell.SettleAsync();
        (darkSamples, whiteSamples) = ViewerControllerTests.Count(shell.Draw(), page);
        Assert.True(whiteSamples > darkSamples * 3, $"dark {darkSamples}, white {whiteSamples}");
    });
}
