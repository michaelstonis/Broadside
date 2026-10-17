using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

public sealed class PageLayoutTests
{
    /// <summary>
    /// A mixed-size document: a 100 x 200 portrait, a 200 x 100 landscape, a 100 x 100 square turned a quarter by its own
    /// <c>Rotate</c>, and a 50 x 50 square at user unit 2 (100 x 100 points).
    /// </summary>
    private static readonly PageMetrics[] Mixed =
    [
        new(0, new PdfRectangle(0, 0, 100, 200), 0, 1),
        new(1, new PdfRectangle(0, 0, 200, 100), 0, 1),
        new(2, new PdfRectangle(10, 20, 110, 120), 90, 1),
        new(3, new PdfRectangle(0, 0, 50, 50), 0, 2),
    ];

    public static TheoryData<PageLayoutMode, bool, int> EveryModeAndRotation()
    {
        var data = new TheoryData<PageLayoutMode, bool, int>();
        foreach (PageLayoutMode mode in Enum.GetValues<PageLayoutMode>())
        {
            foreach (bool cover in new[] { false, true })
            {
                foreach (int rotation in new[] { 0, 90, 180, 270 })
                {
                    data.Add(mode, cover, rotation);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryModeAndRotation))]
    public void A_point_maps_to_the_page_and_back_in_every_mode_and_rotation(PageLayoutMode mode, bool cover, int rotation)
    {
        PageLayout layout = Layout(new PageLayoutOptions().WithMode(mode).WithCover(cover).WithRotation(rotation));

        for (int page = 0; page < layout.PageCount; page++)
        {
            // Scrolled to the page at a zoom that does not divide evenly, so offsets and centring are both exercised.
            Viewport viewport = layout.ScrollTo(new Viewport(0, 0, 150, 120, 1.37), page);
            PdfRectangle crop = Mixed[page].CropBox;
            foreach ((double x, double y) in new[] { (crop.Left + 7.25, crop.Bottom + 3.5), (crop.Right - 1, crop.Top - 2), ((crop.Left + crop.Right) / 2, (crop.Bottom + crop.Top) / 2) })
            {
                PathPoint shown = layout.PageToViewport(viewport, page, x, y);
                PagePoint back = layout.ViewportToPage(viewport, page, shown.X, shown.Y);
                Assert.Equal(page, back.PageIndex);
                Assert.Equal(x, back.X, 9);
                Assert.Equal(y, back.Y, 9);

                if (shown.X >= 0 && shown.X <= viewport.Width && shown.Y >= 0 && shown.Y <= viewport.Height)
                {
                    Assert.True(layout.TryGetPageAt(viewport, shown.X, shown.Y, out PagePoint hit));
                    Assert.Equal(page, hit.PageIndex);
                    Assert.Equal(x, hit.X, 9);
                    Assert.Equal(y, hit.Y, 9);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryModeAndRotation))]
    public void A_page_shows_inside_its_box_and_its_visible_pixels_are_where_the_viewport_is(PageLayoutMode mode, bool cover, int rotation)
    {
        PageLayout layout = Layout(new PageLayoutOptions().WithMode(mode).WithCover(cover).WithRotation(rotation));
        Span<VisiblePage> visible = stackalloc VisiblePage[8];
        for (int page = 0; page < layout.PageCount; page++)
        {
            Viewport viewport = layout.ScrollTo(new Viewport(0, 0, 150, 120, 2), page);
            int count = layout.GetVisiblePages(viewport, visible);
            Assert.Contains(page, visible[..count].ToArray().Select(v => v.PageIndex));
            foreach (VisiblePage shown in visible[..count])
            {
                // The crop box's corners land on the corners of the page's pixels, wherever the rotation takes them.
                PdfRectangle crop = Mixed[shown.PageIndex].CropBox;
                LayoutRectangle box = layout.PageToViewport(viewport, shown.PageIndex, crop);
                PixelRectangle whole = layout.GetPagePixels(shown.PageIndex, viewport.Zoom);
                Assert.Equal(shown.X, box.X, 9);
                Assert.Equal(shown.Y, box.Y, 9);
                Assert.Equal(whole.Width, box.Width, 6);
                Assert.Equal(whole.Height, box.Height, 6);

                // The visible pixels are inside the viewport, rounded outwards by less than a pixel.
                Assert.True(shown.X + shown.Region.X > -1 && shown.X + shown.Region.X + shown.Region.Width < viewport.Width + 1);
                Assert.True(shown.Y + shown.Region.Y > -1 && shown.Y + shown.Region.Y + shown.Region.Height < viewport.Height + 1);
            }
        }
    }

    [Fact]
    public void Continuous_pages_are_centred_in_one_column_with_gaps_and_padding()
    {
        PageLayout layout = Layout(new PageLayoutOptions().WithMode(PageLayoutMode.Continuous));

        Assert.Equal(4, layout.SpreadCount);
        Assert.Equal(new LayoutRectangle(55, 5, 100, 200), layout.GetPageBounds(0));
        Assert.Equal(new LayoutRectangle(5, 215, 200, 100), layout.GetPageBounds(1));
        Assert.Equal(new LayoutRectangle(55, 325, 100, 100), layout.GetPageBounds(2));
        Assert.Equal(new LayoutRectangle(55, 435, 100, 100), layout.GetPageBounds(3));
        Assert.Equal(210, layout.ContentWidth);
        Assert.Equal(540, layout.ContentHeight);

        // Layout rows 200 to 300: the last 5 rows of the portrait and the first 85 of the landscape.
        Assert.Equal(
            [new VisiblePage(0, new PixelRectangle(0, 195, 100, 5), 55, -195), new VisiblePage(1, new PixelRectangle(0, 0, 200, 85), 5, 15)],
            Visible(layout, new Viewport(0, 200, 210, 100, 1)));
    }

    [Fact]
    public void Single_page_shows_only_the_spread_at_the_centre_of_the_viewport()
    {
        PageLayout layout = Layout(new PageLayoutOptions().WithMode(PageLayoutMode.SinglePage));

        Assert.Equal([new VisiblePage(1, new PixelRectangle(0, 0, 200, 85), 5, 15)], Visible(layout, new Viewport(0, 200, 210, 100, 1)));
        Assert.Equal(1, layout.GetCurrentPage(new Viewport(0, 200, 210, 100, 1)));
        Assert.False(layout.TryGetPageAt(new Viewport(0, 200, 210, 100, 1), 100, 2, out _));
    }

    [Fact]
    public void Two_up_pages_meet_at_a_spine()
    {
        PageLayout layout = Layout(new PageLayoutOptions().WithMode(PageLayoutMode.TwoUpContinuous));

        Assert.Equal(2, layout.SpreadCount);
        Assert.Equal(new LayoutRectangle(5, 5, 100, 200), layout.GetPageBounds(0));
        Assert.Equal(new LayoutRectangle(115, 55, 200, 100), layout.GetPageBounds(1));
        Assert.Equal(new LayoutRectangle(5, 215, 100, 100), layout.GetPageBounds(2));
        Assert.Equal(new LayoutRectangle(115, 215, 100, 100), layout.GetPageBounds(3));
        Assert.Equal((320d, 320d), (layout.ContentWidth, layout.ContentHeight));

        // Rows 100 to 260 cross both spreads; columns 0 to 110 only the left slot.
        Assert.Equal(
            [new VisiblePage(0, new PixelRectangle(0, 95, 100, 105), 5, -95), new VisiblePage(2, new PixelRectangle(0, 0, 100, 45), 5, 115)],
            Visible(layout, new Viewport(0, 100, 110, 160, 1)));
    }

    [Fact]
    public void Two_up_with_a_cover_puts_the_first_page_alone_on_the_right()
    {
        PageLayout layout = Layout(new PageLayoutOptions().WithMode(PageLayoutMode.TwoUpContinuous).WithCover(true));

        Assert.Equal(3, layout.SpreadCount);
        Assert.Equal((0, 1), layout.GetSpreadPages(0));
        Assert.Equal((1, 2), layout.GetSpreadPages(1));
        Assert.Equal(new LayoutRectangle(215, 5, 100, 200), layout.GetPageBounds(0));
        Assert.Equal(new LayoutRectangle(5, 215, 200, 100), layout.GetPageBounds(1));
        Assert.Equal(new LayoutRectangle(215, 215, 100, 100), layout.GetPageBounds(2));
        Assert.Equal(new LayoutRectangle(105, 325, 100, 100), layout.GetPageBounds(3));
        Assert.Equal((320d, 430d), (layout.ContentWidth, layout.ContentHeight));
    }

    [Fact]
    public void Two_up_paged_shows_one_spread()
    {
        PageLayout layout = Layout(new PageLayoutOptions().WithMode(PageLayoutMode.TwoUp));

        Assert.Equal(
            [new VisiblePage(2, new PixelRectangle(0, 0, 100, 100), 5, 0), new VisiblePage(3, new PixelRectangle(0, 0, 100, 100), 115, 0)],
            Visible(layout, new Viewport(0, 215, 320, 105, 1)));
    }

    [Fact]
    public void Horizontal_pages_run_in_one_row_centred_vertically()
    {
        PageLayout layout = Layout(new PageLayoutOptions().WithMode(PageLayoutMode.Horizontal));

        Assert.Equal(new LayoutRectangle(5, 5, 100, 200), layout.GetPageBounds(0));
        Assert.Equal(new LayoutRectangle(115, 55, 200, 100), layout.GetPageBounds(1));
        Assert.Equal(new LayoutRectangle(325, 55, 100, 100), layout.GetPageBounds(2));
        Assert.Equal(new LayoutRectangle(435, 55, 100, 100), layout.GetPageBounds(3));
        Assert.Equal((540d, 210d), (layout.ContentWidth, layout.ContentHeight));

        // Columns 300 to 400: the landscape's last 15 columns and the square's first 75.
        Assert.Equal(
            [new VisiblePage(1, new PixelRectangle(185, 0, 15, 100), -185, 55), new VisiblePage(2, new PixelRectangle(0, 0, 75, 100), 25, 55)],
            Visible(layout, new Viewport(300, 0, 100, 210, 1)));
    }

    [Fact]
    public void The_user_rotation_turns_every_page_and_content_smaller_than_the_viewport_is_centred()
    {
        PageLayout layout = Layout(new PageLayoutOptions().WithRotation(-270));

        Assert.Equal(90, layout.Rotation);
        Assert.Equal(new[] { 90, 90, 180, 90 }, Enumerable.Range(0, 4).Select(layout.GetPageRotation));
        Assert.Equal(new LayoutRectangle(5, 5, 200, 100), layout.GetPageBounds(0));
        Assert.Equal(new PixelRectangle(0, 0, 200, 100), layout.GetPagePixels(0, 1));

        // 210 points wide at zoom 1 in a 410-pixel viewport: 100 pixels each side, whatever the scroll offset says.
        // The landscape turned a quarter shows 100 x 200 at (55, 115): its user-space origin, bottom left, turns to the top left.
        Assert.Equal(new LayoutRectangle(55, 115, 100, 200), layout.GetPageBounds(1));
        Assert.Equal(new PathPoint(100 + 55, 115), layout.PageToViewport(new Viewport(50, 0, 410, 300, 1), 1, 0, 0));
    }

    [Fact]
    public void Visible_tiles_cut_each_visible_page_into_a_grid_clipped_to_the_page()
    {
        PageLayout layout = Layout(new PageLayoutOptions().WithRotation(180));
        var viewport = new Viewport(0, 0, 210, 540, 1);

        Tile[] tiles = new Tile[layout.GetVisibleTiles(viewport, 64, [])];
        Assert.Equal(24, layout.GetVisibleTiles(viewport, 64, tiles));

        Assert.Equal(new Tile(0, 1, new PixelRectangle(0, 0, 64, 64)) { Rotation = 180 }, tiles[0]);
        Assert.Equal(new Tile(0, 1, new PixelRectangle(64, 192, 36, 8)) { Rotation = 180 }, tiles[7]);
        Assert.Equal([8, 8, 4, 4], tiles.GroupBy(t => t.PageIndex).Select(g => g.Count()));

        // Only the tiles under a small viewport; a margin brings the neighbours in for render-ahead.
        Assert.Equal(4, layout.GetVisibleTiles(new Viewport(0, 300, 210, 10, 1), 64, new Tile[4]));
        Assert.Equal(6, layout.GetVisibleTiles(new Viewport(0, 300, 210, 10, 1), 64, new Tile[8], margin: 20));
    }

    [Fact]
    public void Fit_width_fit_page_and_actual_size_fit_the_spread_and_its_padding()
    {
        PageLayout layout = Layout(new PageLayoutOptions());

        Assert.Equal(2, layout.GetZoom(ZoomMode.FitWidth, 420, 100, 1));
        Assert.Equal(1, layout.GetZoom(ZoomMode.FitWidth, 210, 100, 0));
        Assert.Equal(300 / 210d, layout.GetZoom(ZoomMode.FitPage, 420, 300, 0));
        Assert.Equal(2, layout.GetZoom(ZoomMode.FitPage, 420, 300, 1));
        Assert.Equal(96 / 72d, layout.GetZoom(ZoomMode.ActualSize, 420, 300, 1, actualSize: 96 / 72d));

        PageLayout twoUp = Layout(new PageLayoutOptions().WithMode(PageLayoutMode.TwoUp));
        Assert.Equal(2, twoUp.GetZoom(ZoomMode.FitWidth, 640, 100, 3));
        Assert.Equal(1, twoUp.GetZoom(ZoomMode.FitPage, 640, 210, 0));
    }

    [Fact]
    public void Scrolling_to_a_page_snapping_and_zooming_at_a_point_keep_the_viewport_in_the_content()
    {
        PageLayout layout = Layout(new PageLayoutOptions());
        var viewport = new Viewport(0, 0, 105, 100, 1);

        Assert.Equal(viewport with { X = 52.5, Y = 320 }, layout.ScrollTo(viewport, 2));
        Assert.Equal(viewport with { X = 52.5, Y = 430 }, layout.ScrollTo(viewport, 3));
        Assert.Equal(viewport with { Y = 320 }, layout.Snap(viewport with { Y = 330 }));
        Assert.Equal(viewport with { Y = 210 }, layout.Snap(viewport with { Y = 260 }));
        Assert.Equal(2, layout.GetCurrentPage(viewport with { Y = 320 }));

        // The page point under (30, 40) stays under it.
        Viewport scrolled = viewport with { X = 20, Y = 250 };
        PagePoint before = layout.ViewportToPage(scrolled, 1, 30, 40);
        Viewport zoomed = layout.ZoomAt(scrolled, 2.5, 30, 40);
        PagePoint after = layout.ViewportToPage(zoomed, 1, 30, 40);
        Assert.Equal(2.5, zoomed.Zoom);
        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
        Assert.Equal(viewport with { X = 105, Y = 440 }, layout.Clamp(viewport with { X = 500, Y = 900 }));
    }

    [Fact]
    public async Task The_page_transform_is_the_renderers_device_transform_moved_to_where_the_page_shows()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("page-rotation.pdf"));
        var visible = new VisiblePage[8];
        foreach (int rotation in new[] { 0, 90, 180, 270 })
        {
            PageLayout layout = await PageLayout.CreateAsync(session, new PageLayoutOptions().WithRotation(rotation), TestContext.Current.CancellationToken);
            var viewport = new Viewport(13, 170, 300, 260, 1.5);
            int count = layout.GetVisiblePages(viewport, visible);
            Assert.True(count >= 1);
            foreach (VisiblePage shown in visible.AsSpan(0, count))
            {
                PdfPage page = session.Document.Pages[shown.PageIndex];
                var options = new RenderOptions { Scale = 1.5, Rotation = rotation };
                Matrix expected = options.GetDeviceTransform(page) * Matrix.CreateTranslation(shown.X, shown.Y);
                Matrix actual = layout.GetPageTransform(viewport, shown.PageIndex);
                Assert.Equal(expected.A, actual.A, 9);
                Assert.Equal(expected.B, actual.B, 9);
                Assert.Equal(expected.C, actual.C, 9);
                Assert.Equal(expected.D, actual.D, 9);
                Assert.Equal(expected.E, actual.E, 9);
                Assert.Equal(expected.F, actual.F, 9);
                PixelRectangle pixels = options.GetPixelRectangle(page);
                Assert.Equal(pixels, layout.GetPagePixels(shown.PageIndex, 1.5));
            }
        }
    }

    [Fact]
    public async Task The_red_square_at_the_user_space_origin_shows_where_the_page_rotation_puts_it()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("page-rotation.pdf"));
        PageLayout layout = await PageLayout.CreateAsync(session, new PageLayoutOptions().WithGap(0).WithPadding(0), TestContext.Current.CancellationToken);

        // Page 1 is /Rotate 90 (100 x 200 shown): the red square at the origin shows top left; page 2 (/Rotate 180) top right.
        var viewport = new Viewport(0, 0, 200, 400, 1);
        PathPoint first = layout.PageToViewport(viewport, 0, 20, 20);
        LayoutRectangle firstBox = layout.GetPageBounds(0);
        Assert.Equal(new PathPoint(firstBox.X + 20, firstBox.Y + 20), first);
        PathPoint second = layout.PageToViewport(viewport, 1, 20, 20);
        LayoutRectangle secondBox = layout.GetPageBounds(1);
        Assert.Equal(new PathPoint(secondBox.Right - 20, secondBox.Y + 20), second);

        // Page 4: /UserUnit 2 and a 160 x 80 crop box shows 320 x 160 points.
        Assert.Equal((320d, 160d), (layout.GetPageBounds(3).Width, layout.GetPageBounds(3).Height));
    }

    [Fact]
    public void Options_follow_the_documents_page_layout_entry()
    {
        Assert.Equal(PageLayoutMode.SinglePage, PageLayoutOptions.For(PdfPageLayout.SinglePage).Mode);
        Assert.Equal(PageLayoutMode.Continuous, PageLayoutOptions.For(PdfPageLayout.OneColumn).Mode);
        Assert.Equal((PageLayoutMode.TwoUpContinuous, false), Of(PageLayoutOptions.For(PdfPageLayout.TwoColumnLeft)));
        Assert.Equal((PageLayoutMode.TwoUpContinuous, true), Of(PageLayoutOptions.For(PdfPageLayout.TwoColumnRight)));
        Assert.Equal((PageLayoutMode.TwoUp, false), Of(PageLayoutOptions.For(PdfPageLayout.TwoPageLeft)));
        Assert.Equal((PageLayoutMode.TwoUp, true), Of(PageLayoutOptions.For(PdfPageLayout.TwoPageRight)));

        static (PageLayoutMode, bool) Of(PageLayoutOptions options) => (options.Mode, options.ShowCover);
    }

    [Theory]
    [InlineData(PageLayoutMode.SinglePage, 0.4)]
    [InlineData(PageLayoutMode.SinglePage, 1)]
    [InlineData(PageLayoutMode.TwoUp, 0.4)]
    [InlineData(PageLayoutMode.TwoUp, 1)]
    [InlineData(PageLayoutMode.Continuous, 0.4)]
    [InlineData(PageLayoutMode.TwoUpContinuous, 0.4)]
    [InlineData(PageLayoutMode.Horizontal, 0.4)]
    public void The_first_and_last_pages_can_be_shown_at_a_zoom_where_a_spread_is_smaller_than_the_viewport(PageLayoutMode mode, double zoom)
    {
        // Twelve 100 x 100 pages: at these zooms a spread is far shorter (and narrower) than the 600 x 800 viewport, yet the content is longer.
        PageMetrics[] pages = [.. Enumerable.Range(0, 12).Select(i => new PageMetrics(i, new PdfRectangle(0, 0, 100, 100), 0, 1))];
        foreach (bool cover in new[] { false, true })
        {
            PageLayout layout = PageLayout.Create(pages, new PageLayoutOptions().WithMode(mode).WithCover(cover).WithGap(10).WithPadding(5));
            bool paged = mode is PageLayoutMode.SinglePage or PageLayoutMode.TwoUp;
            var visible = new VisiblePage[16];

            Viewport first = layout.ScrollTo(new Viewport(0, 0, 600, 800, zoom), 0);
            Assert.Equal(first, layout.Clamp(first));
            Assert.Equal(0, visible[..layout.GetVisiblePages(first, visible)][0].PageIndex);
            Viewport last = layout.ScrollTo(first, 11);
            Assert.Equal(11, visible[..layout.GetVisiblePages(last, visible)][^1].PageIndex);
            if (paged)
            {
                // Only the spread at the viewport's centre shows, and it is the first (or last) one.
                Assert.Equal(0, layout.GetSpread(layout.GetCurrentPage(first)));
                Assert.Equal(layout.GetSpreadPages(0).Count, layout.GetVisiblePages(first, visible));
                Assert.Equal(layout.GetSpread(11), layout.GetSpread(layout.GetCurrentPage(last)));
            }

            if (paged)
            {
                for (int page = 0; page < layout.PageCount; page++)
                {
                    Viewport shown = layout.ScrollTo(first, page);
                    Assert.Equal(layout.GetSpread(page), layout.GetSpread(layout.GetCurrentPage(shown)));
                    Assert.Equal(shown, layout.Clamp(shown));
                }
            }

            // A scroll past either end still lands on the first or last spread.
            double beyond = 1e6;
            bool horizontal = mode == PageLayoutMode.Horizontal;
            Viewport before = layout.Clamp(first with { X = horizontal ? -beyond : 0, Y = horizontal ? 0 : -beyond });
            Viewport after = layout.Clamp(first with { X = horizontal ? beyond : 0, Y = horizontal ? 0 : beyond });
            Assert.Equal(0, visible[..layout.GetVisiblePages(before, visible)][0].PageIndex);
            Assert.Equal(11, visible[..layout.GetVisiblePages(after, visible)][^1].PageIndex);
            if (paged)
            {
                Assert.Equal(0, layout.GetSpread(layout.GetCurrentPage(before)));
                Assert.Equal(layout.SpreadCount - 1, layout.GetSpread(layout.GetCurrentPage(after)));
            }
        }
    }

    [Fact]
    public void Bad_arguments_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageLayoutOptions().WithGap(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageLayoutOptions().WithPadding(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageLayoutOptions().WithRotation(45));
        Assert.Throws<ArgumentException>(() => PageLayout.Create([Mixed[1]]));
        PageLayout layout = Layout(new PageLayoutOptions());
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetPageBounds(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetVisiblePages(new Viewport(0, 0, 10, 10, 0), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetVisibleTiles(new Viewport(0, 0, 10, 10, 1), 0, []));

        PageLayout empty = PageLayout.Create([]);
        Assert.Equal(0, empty.GetVisiblePages(new Viewport(0, 0, 10, 10, 1), new VisiblePage[1]));
        Assert.Equal(-1, empty.GetCurrentPage(new Viewport(0, 0, 10, 10, 1)));
        Assert.False(empty.TryGetPageAt(new Viewport(0, 0, 10, 10, 1), 5, 5, out _));
    }

    private static PageLayout Layout(PageLayoutOptions options) => PageLayout.Create(Mixed, options.WithGap(10).WithPadding(5));

    private static VisiblePage[] Visible(PageLayout layout, Viewport viewport)
    {
        var pages = new VisiblePage[8];
        return pages[..layout.GetVisiblePages(viewport, pages)];
    }
}
