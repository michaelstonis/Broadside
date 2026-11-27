using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;

namespace Broadside.Viewer.Avalonia.Tests;

/// <summary>The toolbar control bound to a real <see cref="PdfView"/> (#632): page and zoom round trips through its commands and controls.</summary>
public sealed class PdfToolbarTests
{
    [Fact]
    public Task The_page_commands_move_the_view_and_the_page_box_follows_it_by_label() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("page-labels.pdf"));
        using var bar = new PdfToolbar { Viewer = viewer.Viewer };
        ViewerToolbar toolbar = bar.Toolbar!;
        var page = (ViewerPageCommand)toolbar.Find(ViewerCommandIds.PageNumber)!;
        Assert.Same(viewer.Viewer, toolbar.Viewer);
        await viewer.SettleAsync();
        Assert.Equal(("i", 12), (page.Text, page.PageCount));

        // A click on the default "next page" button.
        Button next = FindControl<Button>(bar, ViewerCommandIds.PageNext);
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await viewer.SettleAsync();
        Assert.Equal(1, viewer.Viewer.State.PageIndex);
        Assert.Equal("ii", page.Text);

        // A label typed into the page box, then a number, then a page that does not exist.
        Assert.True(page.TrySubmit("V"));
        await viewer.SettleAsync();
        Assert.Equal(6, viewer.Viewer.State.PageIndex);
        Assert.Equal("V", page.Text);
        Assert.True(page.TrySubmit("10"));
        await viewer.SettleAsync();
        Assert.Equal(9, viewer.Viewer.State.PageIndex);
        Assert.False(page.TrySubmit("99"));
        await viewer.SettleAsync();
        Assert.Equal(9, viewer.Viewer.State.PageIndex);
        Assert.NotNull(page.Error);

        ((ViewerButtonCommand)toolbar.Find(ViewerCommandIds.PagePrevious)!).Execute();
        await viewer.SettleAsync();
        Assert.Equal(8, viewer.Viewer.State.PageIndex);
    });

    [Fact]
    public Task The_zoom_commands_zoom_the_view_and_the_picker_shows_the_zoom() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("page-labels.pdf"));
        using var bar = new PdfToolbar { Viewer = viewer.Viewer, Options = new ViewerToolbarOptions().WithZoomPresets(1, 2) };
        ViewerToolbar toolbar = bar.Toolbar!;
        var picker = (ViewerChoiceCommand)toolbar.Find(ViewerCommandIds.ZoomMode)!;
        double start = viewer.Viewer.State.Zoom;

        ((ViewerButtonCommand)toolbar.Find(ViewerCommandIds.ZoomIn)!).Execute();
        await viewer.SettleAsync();
        Assert.Equal(start * 1.25, viewer.Viewer.State.Zoom, 3);

        picker.Selected = 4; // 200 %
        await viewer.SettleAsync();
        Assert.Equal(2, viewer.Viewer.State.Zoom, 3);
        Assert.Equal((4, "200%"), (picker.Selected, picker.Text));

        picker.Selected = 2; // actual size
        await viewer.SettleAsync();
        Assert.Equal(1, viewer.Viewer.State.Zoom, 3);
        ComboBox box = FindControl<ComboBox>(bar, ViewerCommandIds.ZoomMode);
        Assert.Equal(2, box.SelectedIndex);
    });

    [Fact]
    public Task An_item_template_restyles_a_command_and_compact_options_move_the_rest_to_the_overflow() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("page-labels.pdf"));
        using var bar = new PdfToolbar
        {
            Options = new ViewerToolbarOptions().WithCompact(true),
            ItemTemplate = new FuncDataTemplate<ViewerCommand>(command => command.Id == ViewerCommandIds.PageNext, (_, _) => new TextBlock { Text = "custom" }),
        };
        bar.Viewer = viewer.Viewer;
        ViewerToolbar toolbar = bar.Toolbar!;

        var row = (StackPanel)bar.Content!;
        Assert.Contains(row.Children, child => child is TextBlock { Text: "custom" } block && block.DataContext is ViewerCommand { Id: ViewerCommandIds.PageNext });
        Assert.Equal(ViewerCommandPlacement.Overflow, toolbar.Find(ViewerCommandIds.Rotate)!.Placement);
        Assert.Contains(row.Children, child => AutomationProperties.GetAutomationId(child) == "toolbar.more");

        // An app's own command shows up at once; hiding a command takes its control away.
        int shared = 0;
        toolbar.Insert(0, new ViewerButtonCommand("app.share", "Share", () => shared++));
        Button share = FindControl<Button>(bar, "app.share");
        share.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, shared);
        toolbar.Find("app.share")!.Placement = ViewerCommandPlacement.Hidden;
        Assert.DoesNotContain(((StackPanel)bar.Content!).Children, child => AutomationProperties.GetAutomationId(child) == "app.share");

        // Rotate from the overflow turns the view.
        ((ViewerButtonCommand)toolbar.Find(ViewerCommandIds.Rotate)!).Execute();
        await viewer.SettleAsync();
        Assert.Equal(90, viewer.Viewer.LayoutOptions!.Rotation);

        bar.Viewer = null;
        Assert.Null(bar.Toolbar);
        Assert.Empty(((StackPanel)bar.Content!).Children);
    });

    private static T FindControl<T>(PdfToolbar bar, string id)
        where T : Control
    {
        var row = (StackPanel)bar.Content!;
        return row.Children.OfType<T>().Single(child => AutomationProperties.GetAutomationId(child) == id);
    }
}
