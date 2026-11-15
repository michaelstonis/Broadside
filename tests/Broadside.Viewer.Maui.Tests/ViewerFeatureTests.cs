using Broadside.Graphics;
using Broadside.Rendering;
using SkiaSharp;

namespace Broadside.Viewer.Maui.Tests;

/// <summary>The viewer features the MAUI view exposes beyond navigation (#358): search highlights, revealing an area and dark mode.</summary>
/// <remarks>
/// Not run in parallel: the dark mode test counts display-list cache misses, and the cache's memory budget is shared by every
/// document in the process, so other tests' documents can evict this one's lists.
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class ViewerFeatureTests
{
    [Fact]
    public void Search_matches_are_highlighted_over_the_tiles_without_drawing_a_page_again() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        DocumentSearch search = shell.Controller.Search!;
        shell.Events.Clear();

        search.Search("the");
        await shell.SettleUntilAsync(() => search.IsSearchComplete);

        Assert.True(search.MatchCount > 10, $"matches: {search.MatchCount}");
        Assert.Equal(0, search.Current!.PageIndex);
        int highlights = CountItems(shell, ViewerFrame.ItemKind.SearchMatch);
        Assert.True(highlights > 3, $"highlights: {highlights}");
        Assert.Equal(search.Current.Quads.Count, CountItems(shell, ViewerFrame.ItemKind.CurrentMatch));
        Assert.DoesNotContain(shell.Events, e => e.Kind == TileEventKind.Started);

        // The highlight is drawn: a pixel inside the current match is tinted, not white paper.
        using SKBitmap frame = shell.Draw();
        ViewerFrame.Item current = FirstItem(shell, ViewerFrame.ItemKind.CurrentMatch);
        SKPoint centre = new((current.A.X + current.C.X) / 2, (current.A.Y + current.C.Y) / 2);
        SKColor pixel = frame.GetPixel((int)centre.X, (int)centre.Y);
        Assert.NotEqual(SKColors.White, pixel);

        search.Clear();
        shell.Controller.Frame();
        Assert.Equal(0, CountItems(shell, ViewerFrame.ItemKind.SearchMatch) + CountItems(shell, ViewerFrame.ItemKind.CurrentMatch));
    });

    [Fact]
    public void Reveal_scrolls_an_area_of_another_page_into_view() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        var area = new PdfRectangle(72, 300, 200, 320);
        shell.Controller.Reveal(5, area);
        await shell.SettleAsync();

        LayoutRectangle box = shell.Layout.PageToViewport(shell.Viewport, 5, area);
        Assert.InRange(box.Y, 0, shell.Viewport.Height - box.Height);
        Assert.InRange(box.X, 0, shell.Viewport.Width - box.Width);
        Assert.Equal(5, shell.Controller.Input!.CurrentPage);

        // Already in view: nothing moves.
        Viewport before = shell.Viewport;
        shell.Controller.Reveal(5, area);
        await shell.SettleAsync();
        Assert.Equal(before, shell.Viewport);
    });

    [Fact]
    public void Dark_mode_redraws_the_tiles_from_the_cached_display_lists_on_a_dark_background() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        DocumentSession session = shell.Controller.Session!;
        long misses = session.Document.CacheStatistics.DisplayLists.Misses;
        shell.Events.Clear();

        shell.Controller.SetColorFilter(ColorFilter.DarkMode);
        await shell.SettleAsync();

        Assert.Equal(ColorFilter.DarkMode, session.ColorFilter);
        Assert.Contains(shell.Events, e => e.Kind == TileEventKind.Presented && (e.Tile.Layers & DisplayLayers.Content) != 0);
        Assert.Equal(misses, session.Document.CacheStatistics.DisplayLists.Misses);
        using (SKBitmap frame = shell.Draw())
        {
            Assert.Equal(ViewerFrame.DarkBackground, frame.GetPixel(1, 1));
            LayoutRectangle page = shell.Layout.PageToViewport(shell.Viewport, 0, shell.Layout.GetPageMetrics(0).CropBox);
            int black = 0;
            int white = 0;
            for (int y = (int)page.Y + 4; y < Math.Min(page.Bottom, frame.Height) - 4; y += 3)
            {
                for (int x = (int)page.X + 4; x < page.Right - 4; x += 3)
                {
                    SKColor c = frame.GetPixel(x, y);
                    black += c.Red < 16 && c.Green < 16 && c.Blue < 16 ? 1 : 0;
                    white += c.Red > 160 && c.Green > 160 && c.Blue > 160 ? 1 : 0;
                }
            }

            // Black paper with light text: the inverse of the light page.
            Assert.True(black > white * 3, $"black {black}, light {white}");
            Assert.True(white > 100, $"light {white}");
        }

        // A document opened later keeps the filter the view was given.
        shell.Controller.SetColorFilter(null);
        Assert.Equal(ColorFilter.DarkMode, session.ColorFilter);
        shell.Controller.SetColorFilter(ColorFilter.None);
        await shell.SettleAsync();
        using SKBitmap light = shell.Draw();
        Assert.Equal(ViewerFrame.Background, light.GetPixel(1, 1));
    });

    [Fact]
    public void Going_to_an_outline_entry_shows_its_destination_page() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("novel-large-print.pdf"));
        DocumentOutline outline = await DocumentOutline.LoadAsync(shell.Controller.Session!, TestContext.Current.CancellationToken);
        OutlineEntry[] entries = [.. outline.Entries.Where(e => e.Target is { PageIndex: > 0 })];
        Assert.NotEmpty(entries);
        foreach (OutlineEntry entry in entries.Reverse().Take(3))
        {
            shell.Controller.GoTo(entry.Target!.Value);
            await shell.SettleAsync();
            Assert.Equal(entry.Target.Value.PageIndex, shell.Controller.Input!.CurrentPage);
        }
    });

    private static int CountItems(Shell shell, ViewerFrame.ItemKind kind)
    {
        int count = 0;
        foreach (ViewerFrame.Item item in shell.Controller.Presenter.Latest!.Items)
        {
            count += item.Kind == kind ? 1 : 0;
        }

        return count;
    }

    private static ViewerFrame.Item FirstItem(Shell shell, ViewerFrame.ItemKind kind)
    {
        foreach (ViewerFrame.Item item in shell.Controller.Presenter.Latest!.Items)
        {
            if (item.Kind == kind)
            {
                return item;
            }
        }

        throw new InvalidOperationException($"No {kind} item in the frame.");
    }
}
