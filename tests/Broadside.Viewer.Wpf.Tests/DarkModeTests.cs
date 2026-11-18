using Broadside.Rendering;

namespace Broadside.Viewer.Wpf.Tests;

/// <summary>
/// Dark mode through the WPF shell's controller: a change draws every tile again from the cached display lists. The test counts
/// the lists the document's cache builds, so it runs in <see cref="HeavyTestCollection"/>, where no parallel test makes them stale.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DarkModeTests
{
    [Fact]
    public void Dark_mode_draws_every_tile_again_from_the_cached_display_lists_with_dark_paper() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        PdfCacheStatistics before = shell.Controller.Session!.Document.CacheStatistics.DisplayLists;
        int presented = shell.Controller.Surface.PresentedCount;

        shell.Controller.SetColorFilter(ColorFilter.DarkMode);
        Assert.Equal(ColorFilter.DarkMode, shell.Controller.Session.ColorFilter);
        await shell.SettleAsync();
        Assert.True(shell.Frame.IsDark);
        Assert.True(shell.Controller.Surface.PresentedCount > presented);
        PdfCacheStatistics after = shell.Controller.Session.Document.CacheStatistics.DisplayLists;
        Assert.True(
            after.Misses == before.Misses && after.Hits > before.Hits,
            $"display lists before {before}, after {after}: dark mode built a list again");

        double dark = 0;
        double white = 0;
        foreach (ViewerFrame.Item item in shell.Frame.Items)
        {
            if (item.Kind == ViewerFrame.ItemKind.Tile && item.Image is TileCopy tile)
            {
                (double w, double d) = tile.Tones();
                white += w;
                dark += d;
            }
        }

        Assert.True(dark > white * 3, $"dark {dark}, white {white}: the paper turned black");

        // Null keeps the session's filter; a new document opened in the same view gets the view's filter.
        shell.Controller.SetColorFilter(null);
        Assert.Equal(ColorFilter.DarkMode, shell.Controller.Session.ColorFilter);
    });
}
