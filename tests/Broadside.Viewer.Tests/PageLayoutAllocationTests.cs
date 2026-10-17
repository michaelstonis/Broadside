using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>The per-frame layout queries allocate nothing, so a shell can call them on every scroll event.</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class PageLayoutAllocationTests
{
    [Theory]
    [InlineData(PageLayoutMode.Continuous)]
    [InlineData(PageLayoutMode.TwoUpContinuous)]
    [InlineData(PageLayoutMode.Horizontal)]
    [InlineData(PageLayoutMode.SinglePage)]
    public void Visible_pages_tiles_and_mappings_allocate_nothing(PageLayoutMode mode)
    {
        PageLayout layout = PageLayout.Create(Pages(10_000), new PageLayoutOptions().WithMode(mode).WithRotation(90));
        Viewport viewport = layout.ScrollTo(new Viewport(0, 0, 1200, 900, 1.25), 5_000);
        var pages = new VisiblePage[16];
        var tiles = new Tile[256];
        int found = 0;

        Action query = () =>
        {
            found += layout.GetVisiblePages(viewport, pages, margin: 256);
            found += layout.GetVisibleTiles(viewport, 256, tiles, margin: 256);
            found += layout.TryGetPageAt(viewport, 600, 450, out PagePoint point) ? point.PageIndex : 0;
            found += (int)layout.PageToViewport(viewport, 5_000, 10, 10).X;
            found += layout.GetCurrentPage(layout.Snap(viewport));
        };

        Assert.Equal(0, Allocations.Measure(query));
        Assert.True(found > 0);
    }

    internal static PageMetrics[] Pages(int count)
    {
        var pages = new PageMetrics[count];
        for (int i = 0; i < count; i++)
        {
            // Letter and A4, portrait and landscape, a few turned by their own Rotate.
            PdfRectangle crop = (i % 4) switch
            {
                0 => new PdfRectangle(0, 0, 612, 792),
                1 => new PdfRectangle(0, 0, 595, 842),
                2 => new PdfRectangle(0, 0, 792, 612),
                _ => new PdfRectangle(18, 18, 594, 774),
            };
            pages[i] = new PageMetrics(i, crop, i % 7 == 0 ? 90 : 0, 1);
        }

        return pages;
    }
}
