using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Broadside.TestSupport;

namespace Broadside.Viewer.Wpf.Tests;

/// <summary>The toolbar control bound to a real <see cref="PdfView"/> on an STA thread (#632): page and zoom round trips.</summary>
public sealed class PdfToolbarTests
{
    [Fact]
    public void The_page_commands_and_the_page_box_move_the_view() => Sta.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(ManyPagesDocument.Create(6), Shell.Options());
        using PdfView view = await ShowAsync(session);
        using var bar = new PdfToolbar { Viewer = view };
        ViewerToolbar toolbar = bar.Toolbar!;
        var page = (ViewerPageCommand)toolbar.Find(ViewerCommandIds.PageNumber)!;
        Assert.Equal(("1", 6), (page.Text, page.PageCount));

        // A click on the default "next page" button.
        var next = (Button)Item(bar, ViewerCommandIds.PageNext);
        next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await SettleAsync(view);
        Assert.Equal(1, view.State.PageIndex);
        Assert.Equal("2", page.Text);

        Assert.True(page.TrySubmit("5"));
        await SettleAsync(view);
        Assert.Equal(4, view.State.PageIndex);
        Assert.False(page.TrySubmit("7"));
        await SettleAsync(view);
        Assert.Equal(4, view.State.PageIndex);
        Assert.NotNull(page.Error);

        ((ViewerButtonCommand)toolbar.Find(ViewerCommandIds.PagePrevious)!).Execute();
        await SettleAsync(view);
        Assert.Equal(3, view.State.PageIndex);
    });

    [Fact]
    public void The_zoom_commands_zoom_the_view_and_the_picker_shows_the_zoom() => Sta.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(ManyPagesDocument.Create(2), Shell.Options());
        using PdfView view = await ShowAsync(session);
        using var bar = new PdfToolbar { Options = new ViewerToolbarOptions().WithZoomPresets(1, 2), Viewer = view };
        ViewerToolbar toolbar = bar.Toolbar!;
        var picker = (ViewerChoiceCommand)toolbar.Find(ViewerCommandIds.ZoomMode)!;
        double start = view.State.Zoom;

        ((ViewerButtonCommand)toolbar.Find(ViewerCommandIds.ZoomIn)!).Execute();
        await SettleAsync(view);
        Assert.Equal(start * 1.25, view.State.Zoom, 3);

        picker.Selected = 4; // 200 %
        await SettleAsync(view);
        Assert.Equal(2, view.State.Zoom, 3);
        Assert.Equal((4, "200%"), (picker.Selected, picker.Text));
        Assert.Equal(4, ((ComboBox)Item(bar, ViewerCommandIds.ZoomMode)).SelectedIndex);

        picker.Selected = 2; // actual size
        await SettleAsync(view);
        Assert.Equal(1, view.State.Zoom, 3);
    });

    [Fact]
    public void Compact_options_send_commands_to_the_overflow_and_hidden_ones_have_no_control() => Sta.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(ManyPagesDocument.Create(2), Shell.Options());
        using PdfView view = await ShowAsync(session);
        using var bar = new PdfToolbar { Viewer = view, Options = new ViewerToolbarOptions().WithCompact(true) };
        ViewerToolbar toolbar = bar.Toolbar!;

        Assert.Equal(OverflowMode.Always, ToolBar.GetOverflowMode(Item(bar, ViewerCommandIds.Rotate)));
        Assert.Equal(OverflowMode.AsNeeded, ToolBar.GetOverflowMode(Item(bar, ViewerCommandIds.PageNext)));

        toolbar.Find(ViewerCommandIds.Rotate)!.Placement = ViewerCommandPlacement.Hidden;
        Assert.DoesNotContain(bar.Bar.Items.Cast<FrameworkElement>(), item => AutomationProperties.GetAutomationId(item) == ViewerCommandIds.Rotate);

        int shared = 0;
        toolbar.Insert(0, new ViewerButtonCommand("app.share", "Share", () => shared++));
        ((Button)Item(bar, "app.share")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(1, shared);

        ((ViewerToggleCommand)toolbar.Find(ViewerCommandIds.DarkMode)!).IsChecked = true;
        await SettleAsync(view);
        Assert.Equal(Rendering.ColorFilter.DarkMode, view.ColorFilter);
        Assert.True(((ToggleButton)Item(bar, ViewerCommandIds.DarkMode)).IsChecked);
    });

    private static FrameworkElement Item(PdfToolbar bar, string id) =>
        bar.Bar.Items.Cast<FrameworkElement>().Single(item => AutomationProperties.GetAutomationId(item) == id);

    internal static async Task<PdfView> ShowAsync(DocumentSession session)
    {
        var view = new PdfView();
        view.Measure(new Size(400, 600));
        view.Arrange(new Rect(0, 0, 400, 600));
        view.UpdateLayout();
        view.Session = session;
        await SettleAsync(view);
        Assert.True(view.State.PageCount > 0);
        return view;
    }

    internal static async Task SettleAsync(PdfView view)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int quiet = 0;
        while (watch.Elapsed < TimeSpan.FromMinutes(1))
        {
            await Sta.YieldAsync();
            bool more = view.RunFrame();
            bool loading = view.State.IsLoading || view.State.PageCount == 0 || view.Controller.IsLoadingPages;
            quiet = more || loading ? 0 : quiet + 1;
            if (quiet >= 3)
            {
                view.UpdateLayout();
                return;
            }
        }

        Assert.Fail("The view did not settle.");
    }
}
