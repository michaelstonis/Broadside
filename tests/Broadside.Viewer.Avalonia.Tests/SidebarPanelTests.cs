using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace Broadside.Viewer.Avalonia.Tests;

/// <summary>The side panels (#636) bound to a real <see cref="PdfView"/>: the core's sidebar model laid out with Avalonia controls.</summary>
public sealed class SidebarPanelTests
{
    [Fact]
    public Task The_thumbnails_panel_draws_every_page_and_a_click_shows_one() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("page-labels.pdf"));
        using var panel = new PdfThumbnailsPanel { Viewer = viewer.Viewer };
        await panel.Sidebar!.Loading;
        Assert.Equal(12, panel.PageCount);
        for (int i = 0; i < 500 && panel.RenderedCount < 12; i++)
        {
            await viewer.SettleAsync();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.Equal(12, panel.RenderedCount);
        Button third = Find<Button>(panel, "thumbnail").ElementAt(2);
        Assert.Equal("Page iii", AutomationProperties.GetName(third));
        third.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await viewer.SettleAsync();
        Assert.Equal(2, viewer.Viewer.State.PageIndex);
    });

    [Fact]
    public Task The_layers_panel_hides_a_layer_in_the_view_and_reset_shows_it_again() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("optional-content-pages.pdf"));
        using var panel = new PdfLayersPanel { Viewer = viewer.Viewer };
        await panel.Sidebar!.Loading;
        List<CheckBox> boxes = [.. Find<CheckBox>(panel, "layer")];
        Assert.Equal(4, boxes.Count);
        Assert.False(boxes[2].IsEnabled); // Draft is locked

        boxes[0].IsChecked = false;
        await viewer.SettleAsync();
        Assert.NotNull(viewer.Viewer.CurrentSession!.OptionalContentState);

        Find<Button>(panel, "layers-reset").Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(viewer.Viewer.CurrentSession.OptionalContentState);
        Assert.True(Find<CheckBox>(panel, "layer").First().IsChecked);
    });

    [Fact]
    public Task The_search_panel_lists_matches_by_page_and_a_click_shows_one() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("text-standard14.pdf"));
        using var panel = new PdfSearchPanel { Viewer = viewer.Viewer };
        panel.Search("o"); // Hello, Broadside
        for (int i = 0; i < 500 && viewer.Viewer.Search is { IsSearchComplete: false }; i++)
        {
            await viewer.SettleAsync();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        await viewer.SettleAsync();
        Assert.True(panel.ResultCount > 1, $"{panel.ResultCount} results");
        Button last = Find<Button>(panel, "search-result").Last();
        last.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await viewer.SettleAsync();
        Assert.Equal(panel.Sidebar!.SearchResults!.Count - 1, viewer.Viewer.Search!.CurrentIndex);
    });

    [Fact]
    public Task The_sidebar_shares_one_model_across_its_tabs_and_hands_attachments_to_the_app() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("embedded-files.pdf"));
        using var sidebar = new PdfSidebar { Viewer = viewer.Viewer };
        await sidebar.Sidebar!.Loading;
        var opened = new TaskCompletionSource<AttachmentOpeningEventArgs>();
        sidebar.AttachmentOpening += (_, e) => opened.TrySetResult(e);
        Assert.Equal(SidebarPanels.Thumbnails, sidebar.SelectedPanel);

        sidebar.SelectedPanel = SidebarPanels.Attachments;
        Assert.Equal(SidebarPanels.Attachments, sidebar.SelectedPanel);
        Find<Button>(sidebar, "attachment-open").First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        AttachmentOpeningEventArgs args = await opened.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal((AttachmentAction.Open, "dätä.csv"), (args.Action, args.Item.FileName));
        sidebar.Viewer = null;
        Assert.Null(sidebar.Sidebar);
    });

    private static IEnumerable<T> Find<T>(ILogical root, string automationId)
        where T : Control =>
        root.GetLogicalDescendants().OfType<T>().Where(control => AutomationProperties.GetAutomationId(control) == automationId);
}
