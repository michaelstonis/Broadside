using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer.WinUI.Tests;

/// <summary>
/// Dark mode through the WinUI shell's controller: a change draws every tile again from the cached display lists. The test counts
/// the lists the document's cache builds, so it runs in <see cref="HeavyTestCollection"/>, where no parallel test makes them stale.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DarkModeTests
{
    [Fact]
    public void Dark_mode_redraws_the_tiles_from_the_cached_display_lists_on_black_paper() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        DocumentSession session = shell.Controller.Session!;
        LayoutRectangle page = shell.Layout.PageToViewport(shell.Viewport, 0, shell.Layout.GetPageMetrics(0).CropBox);
        (int darkBefore, int whiteBefore) = ViewerControllerTests.CountInk(shell.Draw(), page);
        PdfCacheStatistics before = session.Document.CacheStatistics.DisplayLists;

        shell.Events.Clear();
        shell.Controller.SetColorFilter(ColorFilter.DarkMode);
        Assert.Equal(ColorFilter.DarkMode, session.ColorFilter);
        await shell.SettleAsync();
        Assert.Contains(shell.Events, e => e.Kind == TileEventKind.Presented);
        PdfCacheStatistics after = session.Document.CacheStatistics.DisplayLists;
        Assert.True(
            after.Misses == before.Misses && after.Hits > before.Hits,
            $"display lists before {before}, after {after}: dark mode built a list again");
        Assert.True(shell.Controller.LatestFrame.IsDark);

        // Light text on black paper: the counts of dark and white samples swap.
        Shell.Pixels frame = shell.Draw();
        Assert.Equal(0xFF2B2B2Bu, frame[1, 1]);
        (int dark, int white) = ViewerControllerTests.CountInk(frame, page);
        Assert.True(dark > whiteBefore / 2, $"dark samples: {dark}, white before: {whiteBefore}");
        Assert.True(white < darkBefore * 2, $"white samples: {white}, dark before: {darkBefore}");

        // A document opened later keeps the filter; null leaves each session's own.
        shell.Controller.SetColorFilter(null);
        Assert.Equal(ColorFilter.DarkMode, session.ColorFilter);
    });
}
