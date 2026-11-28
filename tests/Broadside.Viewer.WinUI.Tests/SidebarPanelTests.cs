using Broadside.TestSupport;
using static Broadside.Viewer.WinUI.Tests.PdfToolbarTests;

namespace Broadside.Viewer.WinUI.Tests;

/// <summary>
/// The side panels' model (#636) bound to this shell's controller through the calls its <c>PdfView</c> forwards for
/// <see cref="IDocumentViewer"/>: the panels lay out a <see cref="ViewerSidebar"/>, whose navigation and layer toggles reach the view.
/// The panel controls themselves need the platform; this project compiles the shell's platform-free sources only.
/// </summary>
public sealed class SidebarPanelTests
{
    [Fact]
    public void A_layer_toggle_redraws_the_view_and_reset_returns_to_the_file_states() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Corpus.Bytes("optional-content-pages.pdf"));
        var viewer = new ControllerViewer(shell);
        using var sidebar = new ViewerSidebar(viewer);
        await sidebar.Loading;
        DocumentLayers layers = Assert.IsType<DocumentLayers>(sidebar.Layers);
        int invalidated = shell.Events.Count(e => e.Kind == TileEventKind.Invalidated);

        Assert.True(sidebar.SetLayerVisible(layers.Roots[0], false));
        Assert.True(shell.Events.Count(e => e.Kind == TileEventKind.Invalidated) > invalidated);
        await SettleAsync(shell, viewer);
        Assert.False(layers.Roots[0].IsVisible);

        sidebar.ResetLayers();
        await SettleAsync(shell, viewer);
        Assert.Null(shell.Controller.Session!.OptionalContentState);
    });

    [Fact]
    public void Thumbnails_and_the_outline_navigate_the_view() => UiThread.Run(async () =>
    {
        using (Shell shell = await Shell.OpenAsync(Corpus.Bytes("page-labels.pdf")))
        {
            var viewer = new ControllerViewer(shell);
            using var sidebar = new ViewerSidebar(viewer, SidebarPanels.Thumbnails);
            await sidebar.Loading;
            Assert.Equal("iii", sidebar.GetPageLabel(2));

            sidebar.GoToPage(5);
            await SettleAsync(shell, viewer);
            Assert.Equal(5, shell.Controller.State.PageIndex);
        }

        using (Shell shell = await Shell.OpenAsync(Corpus.Bytes("outline-full.pdf")))
        {
            var viewer = new ControllerViewer(shell);
            using var sidebar = new ViewerSidebar(viewer, SidebarPanels.Outline | SidebarPanels.Annotations);
            await sidebar.Loading;
            DocumentOutline outline = Assert.IsType<DocumentOutline>(sidebar.Outline);
            Assert.True(sidebar.GoTo(outline.Entries.First(e => e.Target is not null)));
            Assert.False(sidebar.GoTo(outline.Entries.First(e => e.Target is null && e.Uri is not null))); // a URI entry
            await SettleAsync(shell, viewer);
            Assert.Equal(0, shell.Controller.State.PageIndex);
            Assert.NotNull(sidebar.Annotations);
        }
    });

    [Fact]
    public void A_search_result_becomes_the_current_match() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(ManyPagesDocument.CreateWithText(3, lines: 1));
        var viewer = new ControllerViewer(shell);
        using var sidebar = new ViewerSidebar(viewer, SidebarPanels.Search);
        sidebar.Search("lazy");
        for (int i = 0; i < 1000 && shell.Controller.Search is { IsSearchComplete: false }; i++)
        {
            await SettleAsync(shell, viewer);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        SearchResults results = Assert.IsType<SearchResults>(sidebar.SearchResults);
        Assert.Equal(3, results.Pages.Count);
        sidebar.Show(results.Pages[2].Items[0]);
        await SettleAsync(shell, viewer);
        Assert.Equal(2, shell.Controller.State.PageIndex);
        Assert.Equal(results.Pages[2].Items[0].MatchIndex, shell.Controller.Search!.CurrentIndex);
    });
}
