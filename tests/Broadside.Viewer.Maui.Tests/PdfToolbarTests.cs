using Broadside.TestSupport;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace Broadside.Viewer.Maui.Tests;

/// <summary>
/// The toolbar control bound to a real <see cref="PdfView"/> without a platform (#632): its frames run through the view's test hook,
/// and page and zoom make round trips through the toolbar's commands and controls.
/// </summary>
[Collection(HeavyTestCollection.Name)] // installs a process-wide dispatcher for its view: nothing runs beside it
public sealed class PdfToolbarTests
{
    // The view's members and its controller's continuations run on one UI thread, as on a device: without that context, a page load's
    // continuation rebuilt the semantic layer on a pool thread while a frame repositioned it (#632's flaky NullReferenceException).
    [Fact]
    public void The_page_and_zoom_commands_move_and_zoom_a_real_view() => UiThread.Run(async () =>
    {
        await using DocumentSession session = DocumentSession.Open(ManyPagesDocument.Create(6), Shell.Options());
        using PdfView view = await ShowAsync(session);
        using var bar = new PdfToolbar { Options = new ViewerToolbarOptions().WithZoomPresets(1, 2), Viewer = view };
        ViewerToolbar toolbar = bar.Toolbar!;
        var page = (ViewerPageCommand)toolbar.Find(ViewerCommandIds.PageNumber)!;
        Assert.Equal(("1", 6), (page.Text, page.PageCount));

        // A click on the default "next page" button.
        var next = (Button)bar.BarItems.Single(item => ((Element)item).AutomationId == ViewerCommandIds.PageNext);
        ((IButtonController)next).SendClicked();
        await SettleAsync(view);
        Assert.Equal(1, view.State.PageIndex);
        Assert.Equal("2", page.Text);

        Assert.True(page.TrySubmit("4"));
        await SettleAsync(view);
        Assert.Equal(3, view.State.PageIndex);
        Assert.False(page.TrySubmit("9"));
        Assert.Equal(3, view.State.PageIndex);
        Assert.NotNull(page.Error);

        double start = view.State.Zoom;
        ((ViewerButtonCommand)toolbar.Find(ViewerCommandIds.ZoomIn)!).Execute();
        await SettleAsync(view);
        Assert.Equal(start * 1.25, view.State.Zoom, 3);

        var picker = (ViewerChoiceCommand)toolbar.Find(ViewerCommandIds.ZoomMode)!;
        picker.Selected = 4; // 200 %
        await SettleAsync(view);
        Assert.Equal(2, view.State.Zoom, 3);
        Assert.Equal(4, ((Picker)bar.BarItems.Single(item => ((Element)item).AutomationId == ViewerCommandIds.ZoomMode)).SelectedIndex);
    });

    [Fact]
    public void Without_a_document_the_built_in_controls_are_disabled_and_compact_options_fill_the_overflow_row()
    {
        using var view = new PdfView();
        using var bar = new PdfToolbar { Viewer = view, Options = new ViewerToolbarOptions().WithCompact(true) };

        Assert.All(bar.Toolbar!.Items, item => Assert.False(item.IsEnabled, item.Id));
        Assert.Contains(bar.OverflowItems, item => ((Element)item).AutomationId == ViewerCommandIds.Rotate);
        Assert.Contains(bar.BarItems, item => ((Element)item).AutomationId == "toolbar.more");

        int shared = 0;
        bar.Toolbar.Insert(0, new ViewerButtonCommand("app.share", "Share", () => shared++));
        ((IButtonController)bar.BarItems.Single(item => ((Element)item).AutomationId == "app.share")).SendClicked();
        Assert.Equal(1, shared);

        bar.ItemTemplate = new DataTemplate(() => new Label { Text = "custom" });
        Assert.All(bar.BarItems.Where(item => ((Element)item).AutomationId != "toolbar.more"), item => Assert.IsType<Label>(item));
        Assert.IsType<ViewerCommand>(((Label)bar.BarItems[0]).BindingContext, exactMatch: false);

        bar.Viewer = null;
        Assert.Null(bar.Toolbar);
        Assert.Empty(bar.BarItems);
    }

    private static async Task<PdfView> ShowAsync(DocumentSession session)
    {
        ManualDispatcher.Install();
        var view = new PdfView();
#pragma warning disable CS0618 // sizes the view without a platform: Layout raises SizeAllocated, which the view resizes from
        view.Layout(new Rect(0, 0, 400, 600));
#pragma warning restore CS0618
        view.Session = session;
        await SettleAsync(view);
        Assert.True(view.State.PageCount > 0, "the view did not open the document");
        return view;
    }

    private static async Task SettleAsync(PdfView view)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int quiet = 0;
        while (watch.Elapsed < TimeSpan.FromMinutes(1))
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
            bool more = view.RunFrame();
            bool loading = view.State.IsLoading || view.State.PageCount == 0 || view.IsLoadingPages;
            quiet = more || loading ? 0 : quiet + 1;
            if (quiet >= 3)
            {
                return;
            }
        }

        Assert.Fail("The view did not settle.");
    }

    /// <summary>
    /// A dispatcher for a view without a platform: work runs inline and the frame timer never ticks by itself (the test runs frames
    /// through <see cref="PdfView.RunFrame"/>).
    /// </summary>
    private sealed class ManualDispatcher : IDispatcher, IDispatcherProvider
    {
        private static readonly ManualDispatcher Instance = new();
        private static int _installed;

        public bool IsDispatchRequired => false;

        public static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) == 0 && DispatcherProvider.Current.GetForCurrentThread() is null)
            {
                DispatcherProvider.SetCurrent(Instance);
            }
        }

        public IDispatcher? GetForCurrentThread() => this;

        public bool Dispatch(Action action)
        {
            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action) => Dispatch(action);

        public IDispatcherTimer CreateTimer() => new Timer();

        private sealed class Timer : IDispatcherTimer
        {
            public event EventHandler? Tick
            {
                add { }
                remove { }
            }

            public TimeSpan Interval { get; set; }

            public bool IsRepeating { get; set; }

            public bool IsRunning { get; private set; }

            public void Start() => IsRunning = true;

            public void Stop() => IsRunning = false;
        }
    }
}
