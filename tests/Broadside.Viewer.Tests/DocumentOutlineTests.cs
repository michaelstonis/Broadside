using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>
/// The outline as a navigable model (issue #216, viewer capability 10): each item of <c>outline-full.pdf</c> and
/// <c>destinations-all.pdf</c>, and every named destination of the latter, lands on the view its destination type asks for (ISO 32000-2
/// §12.3.2.2, Table 149), and page labels (§12.4.2).
/// </summary>
public sealed class DocumentOutlineTests
{
    // The corpus pages are Letter: 612 x 792 points with the layout's default 8-point padding around them.
    private const double PaddedWidth = 612 + 16;
    private const double PaddedHeight = 792 + 16;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(DocumentSession Session, PageLayout Layout)> OpenAsync(string file, ViewerThreading threading = ViewerThreading.Background)
    {
        DocumentSession session = DocumentSession.Open(Corpus.Bytes(file), new ViewerOptions().WithThreading(threading));
        return (session, await PageLayout.CreateAsync(session, cancellationToken: Token));
    }

    [Theory]
    [InlineData(ViewerThreading.Cooperative)]
    [InlineData(ViewerThreading.Background)]
    public async Task The_outline_of_outline_full_is_a_tree_with_each_items_look_and_destination(ViewerThreading threading)
    {
        (DocumentSession session, _) = await OpenAsync("outline-full.pdf", threading);
        await using DocumentSession owned = session;

        DocumentOutline outline = await DocumentOutline.LoadAsync(session, Token);

        Assert.Equal(
            ["Part I", "Chapter 1", "Section 1.1", "Chapter 2", "Part II", "Chapter 3 – Übersicht", "Chapter 4"],
            outline.Entries.Select(e => e.Title));
        Assert.Equal([0, 1, 2, 1, 0, 1, 1], outline.Entries.Select(e => e.Level));
        Assert.Equal(["Part I", "Part II"], outline.Roots.Select(e => e.Title));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], outline.Entries.Select(e => e.Id));
        Assert.All(outline.Roots, root => Assert.Null(root.Parent));
        Assert.Same(outline.Entries[0], outline.Entries[1].Parent);
        Assert.Same(outline.Entries[1], outline.Entries[2].Parent);

        // Count > 0 is open, < 0 closed (Table 151).
        Assert.Equal([true, false, false, false, false, false, false], outline.Entries.Select(e => e.IsOpen));
        Assert.Equal(["Part I", "Chapter 1", "Chapter 2", "Part II"], outline.GetVisible().Select(e => e.Title));
        Assert.Equal(
            ["Part I", "Chapter 1", "Section 1.1", "Chapter 2", "Part II", "Chapter 3 – Übersicht", "Chapter 4"],
            outline.GetVisible(_ => true).Select(e => e.Title));

        OutlineEntry chapter3 = outline.Entries[5];
        Assert.True(chapter3.IsBold);
        Assert.True(chapter3.IsItalic);
        Assert.Equal(new PdfRgbColor(1, 0, 0), chapter3.Color);
        Assert.False(outline.Entries[0].IsBold);

        Assert.Equal(new ViewTarget(0, PdfDestinationView.Fit, null, null, null, null, null), outline.Entries[0].Target);
        Assert.Equal(new ViewTarget(0, PdfDestinationView.FitH, null, 700, null, null, null), outline.Entries[1].Target);
        Assert.Equal(new ViewTarget(0, PdfDestinationView.Xyz, 72, 700, null, null, null), outline.Entries[2].Target);
        Assert.Equal(new ViewTarget(0, PdfDestinationView.Xyz, 0, 792, null, null, null), chapter3.Target); // a GoTo action to the name (alpha)
        Assert.Equal(new ViewTarget(0, PdfDestinationView.FitB, null, null, null, null, null), outline.Entries[6].Target);
        Assert.Equal("1", outline.Entries[0].PageLabel);

        // Chapter 2 is a URI action: nothing to go to in the document.
        OutlineEntry chapter2 = outline.Entries[3];
        Assert.Null(chapter2.Target);
        Assert.Equal("https://example.org/chapter-2", chapter2.Uri?.AbsoluteUri);
        Assert.Equal(PdfActionKind.Uri, chapter2.ActionKind);
        Assert.True(chapter2.IsNavigable);
        Assert.False(outline.Entries[4].IsNavigable); // Part II has neither
        Assert.Same(outline.Entries[6], outline.FindEntryFor(0)); // the last entry on page 0 is "you are here"
    }

    [Fact]
    public async Task Activating_each_item_of_outline_full_lands_on_the_view_its_destination_asks_for()
    {
        (DocumentSession session, PageLayout layout) = await OpenAsync("outline-full.pdf");
        await using DocumentSession owned = session;
        DocumentOutline outline = await DocumentOutline.LoadAsync(session, Token);
        var start = new Viewport(0, 0, 400, 300, 1);

        // Fit (Part I) and FitB (Chapter 4): the whole page in the window.
        double fit = Math.Min(400 / PaddedWidth, 300 / PaddedHeight);
        foreach (int index in new[] { 0, 6 })
        {
            Viewport viewport = outline.Entries[index].Target!.Value.GetViewport(layout, start);
            Assert.Equal(fit, viewport.Zoom, 9);
            Assert.Equal(0, viewport.Y, 6);
        }

        // FitH 700 (Chapter 1): the page's width fills the window, y = 700 at its top.
        double fitWidth = 400 / PaddedWidth;
        Viewport fitH = outline.Entries[1].Target!.Value.GetViewport(layout, start);
        Assert.Equal(fitWidth, fitH.Zoom, 9);
        Assert.Equal((8 + 792 - 700) * fitWidth, fitH.Y, 6);

        // XYZ 72 700 null (Section 1.1): x = 72 and y = 700 at the top-left corner, the zoom kept.
        Viewport xyz = outline.Entries[2].Target!.Value.GetViewport(layout, start);
        Assert.Equal(1, xyz.Zoom);
        Assert.Equal(8 + 72, xyz.X, 6);
        Assert.Equal(8 + 792 - 700, xyz.Y, 6);

        // XYZ 0 792 null (Chapter 3, through the named destination): the page's top-left corner.
        Viewport alpha = outline.Entries[5].Target!.Value.GetViewport(layout, start);
        Assert.Equal(1, alpha.Zoom);
        Assert.Equal(8, alpha.X, 6);
        Assert.Equal(8, alpha.Y, 6);
    }

    [Fact]
    public async Task Every_explicit_destination_form_of_destinations_all_resolves_and_shows_what_it_asks_for()
    {
        (DocumentSession session, PageLayout layout) = await OpenAsync("destinations-all.pdf");
        await using DocumentSession owned = session;
        var start = new Viewport(0, 0, 200, 300, 1);
        var shortWindow = new Viewport(0, 0, 200, 100, 1); // shorter than the page at its fit-width zoom, so the top can be scrolled to

        async Task<ViewTarget> FindAsync(string name) =>
            await ViewTarget.FindNamedAsync(session, name, Token) ?? throw new Xunit.Sdk.XunitException($"The name {name} is not found.");

        double fitPage = Math.Min(200 / PaddedWidth, 300 / PaddedHeight);
        double fitWidth = 200 / PaddedWidth;
        double fitHeight = 300 / PaddedHeight;

        // XYZ: with numbers (left, top and zoom), with nulls (the page only, zoom kept) and with zoom 0 (kept).
        ViewTarget xyz = await FindAsync("xyz");
        Assert.Equal(new ViewTarget(0, PdfDestinationView.Xyz, 72, 720, null, null, 1.5), xyz);
        Viewport shown = xyz.GetViewport(layout, start);
        Assert.Equal(1.5, shown.Zoom);
        Assert.Equal((8 + 72) * 1.5, shown.X, 6);
        Assert.Equal((8 + 792 - 720) * 1.5, shown.Y, 6);

        ViewTarget xyzNull = await FindAsync("xyz-null");
        Assert.Equal(new ViewTarget(0, PdfDestinationView.Xyz, null, null, null, null, null), xyzNull);
        Viewport pageOnly = xyzNull.GetViewport(layout, start with { Zoom = 0.5 });
        Assert.Equal(0.5, pageOnly.Zoom);
        Assert.Equal(0, pageOnly.Y);

        ViewTarget xyzZero = await FindAsync("xyz-zero");
        Assert.Null(xyzZero.Zoom);
        Assert.Equal(1, xyzZero.GetViewport(layout, start).Zoom);

        // Fit and FitB: the page fits.
        foreach (string name in new[] { "fit", "fitb", "with-d" })
        {
            Viewport viewport = (await FindAsync(name)).GetViewport(layout, start);
            Assert.Equal(fitPage, viewport.Zoom, 9);
        }

        // FitH and FitBH: the width fits, y at the top; FitH null only the width.
        foreach ((string name, double top) in new[] { ("fith", 650.0), ("fitbh", 700.0), ("indirect", 500.0) })
        {
            ViewTarget target = await FindAsync(name);
            Assert.Equal(top, target.Top);
            Viewport viewport = target.GetViewport(layout, shortWindow);
            Assert.Equal(fitWidth, viewport.Zoom, 9);
            Assert.Equal((8 + 792 - top) * fitWidth, viewport.Y, 6);
        }

        ViewTarget fitHNull = await FindAsync("fith-null");
        Assert.Null(fitHNull.Top);
        Assert.Equal(fitWidth, fitHNull.GetViewport(layout, shortWindow).Zoom, 9);
        Assert.Equal(0, fitHNull.GetViewport(layout, shortWindow).Y, 6);

        // FitV and FitBV: the height fits, x at the left edge.
        foreach ((string name, double left) in new[] { ("fitv", 50.0), ("fitbv", 36.0) })
        {
            ViewTarget target = await FindAsync(name);
            Assert.Equal(left, target.Left);
            Viewport viewport = target.GetViewport(layout, start);
            Assert.Equal(fitHeight, viewport.Zoom, 9);
            Assert.Equal((8 + left) * fitHeight, viewport.X, 6);
        }

        // FitR: the rectangle fits the window, centred.
        ViewTarget fitR = await FindAsync("fitr");
        Assert.Equal(new ViewTarget(0, PdfDestinationView.FitR, 10, 400.5, 300, 20, null), fitR);
        Viewport rectangle = fitR.GetViewport(layout, start);
        Assert.Equal(Math.Min(200 / 290.0, 300 / 380.5), rectangle.Zoom, 9);
        LayoutRectangle box = layout.PageToViewport(rectangle, 0, new PdfRectangle(10, 20, 300, 400.5));
        Assert.Equal(200, box.Width, 6); // the limiting axis fills the window exactly
        Assert.Equal(0, box.X, 6);
        Assert.InRange(box.Y, -1e-6, 300 - box.Height + 1e-6);

        // A name that is not defined is not found.
        Assert.Null(await ViewTarget.FindNamedAsync(session, "no-such-name", Token));
    }

    [Fact]
    public async Task The_outline_of_destinations_all_goes_by_a_name_in_the_catalogs_Dests_and_by_a_string_in_the_Names_tree()
    {
        (DocumentSession session, PageLayout layout) = await OpenAsync("destinations-all.pdf");
        await using DocumentSession owned = session;

        DocumentOutline outline = await DocumentOutline.LoadAsync(session, Token);

        Assert.Equal(["Chapter 6", "Alpha"], outline.Entries.Select(e => e.Title));
        Assert.Equal(new ViewTarget(0, PdfDestinationView.FitV, 72, null, null, null, null), outline.Entries[0].Target);
        Assert.Equal(new ViewTarget(0, PdfDestinationView.Xyz, 0, 792, null, null, null), outline.Entries[1].Target);
        Viewport viewport = outline.Entries[0].Target!.Value.GetViewport(layout, new Viewport(0, 0, 200, 300, 1));
        Assert.Equal(300 / PaddedHeight, viewport.Zoom, 9);
    }

    [Fact]
    public async Task A_destination_the_layout_has_no_page_for_leaves_the_viewport_alone()
    {
        (DocumentSession session, PageLayout layout) = await OpenAsync("outline-full.pdf");
        await using DocumentSession owned = session;
        var viewport = new Viewport(3, 4, 400, 300, 1);

        Assert.Equal(viewport, new ViewTarget(9, PdfDestinationView.Fit, null, null, null, null, null).GetViewport(layout, viewport));
    }

    [Fact]
    public async Task A_broken_outline_loads_what_can_be_read_without_throwing()
    {
        (DocumentSession session, _) = await OpenAsync("outline-broken.pdf");
        await using DocumentSession owned = session;

        DocumentOutline outline = await DocumentOutline.LoadAsync(session, Token);

        Assert.False(outline.IsEmpty);
        Assert.Contains("One", outline.Entries.Select(e => e.Title));
    }

    [Fact]
    public async Task A_document_without_an_outline_loads_an_empty_one()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("empty-page.pdf"));

        DocumentOutline outline = await DocumentOutline.LoadAsync(session, Token);

        Assert.True(outline.IsEmpty);
        Assert.Empty(outline.Roots);
        Assert.Empty(outline.GetVisible());
        Assert.Null(outline.FindEntryFor(0));
    }

    [Fact]
    public async Task Page_labels_follow_the_ranges_and_default_to_page_numbers_without_any()
    {
        await using DocumentSession labeled = DocumentSession.Open(Corpus.Bytes("page-labels.pdf"));
        PageLabelTable table = await PageLabelTable.LoadAsync(labeled, Token);

        Assert.True(table.HasLabels);
        Assert.Equal(12, table.Count);
        // i-iii, 1-2, IV-V, Z and AA (Table 161's letters repeat), "A-" and lowercase letters from 52, then the prefix "Cover" alone.
        Assert.Equal(["i", "ii", "iii", "1", "2", "IV", "V", "Z", "AA", "A-zz", "A-aaa", "Cover"], Enumerable.Range(0, 12).Select(table.GetLabel));
        Assert.True(table.TryFindPage("iii", out int page));
        Assert.Equal(2, page);
        Assert.True(table.TryFindPage("iv", out page)); // ignoring case, after the exact match
        Assert.Equal(5, page);
        Assert.False(table.TryFindPage("nope", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.GetLabel(12));

        await using DocumentSession plain = DocumentSession.Open(Corpus.Bytes("empty-page.pdf"));
        PageLabelTable numbers = await PageLabelTable.LoadAsync(plain, Token);
        Assert.False(numbers.HasLabels);
        Assert.Equal("1", numbers.GetLabel(0));
    }
}
