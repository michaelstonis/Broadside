using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>The headless toolbar model (#632): every built-in command driven against a fake <see cref="IDocumentViewer"/>.</summary>
public sealed class ViewerToolbarTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void The_built_in_commands_come_in_the_default_order_with_their_kinds()
    {
        var viewer = new FakeViewer();
        using var toolbar = new ViewerToolbar(viewer);

        Assert.Equal(ViewerCommandIds.All, toolbar.Items.Select(item => item.Id));
        Assert.IsType<ViewerButtonCommand>(toolbar.Find(ViewerCommandIds.PagePrevious));
        Assert.IsType<ViewerButtonCommand>(toolbar.Find(ViewerCommandIds.PageNext));
        Assert.IsType<ViewerPageCommand>(toolbar.Find(ViewerCommandIds.PageNumber));
        Assert.IsType<ViewerButtonCommand>(toolbar.Find(ViewerCommandIds.ZoomOut));
        Assert.IsType<ViewerButtonCommand>(toolbar.Find(ViewerCommandIds.ZoomIn));
        Assert.IsType<ViewerChoiceCommand>(toolbar.Find(ViewerCommandIds.ZoomMode));
        Assert.IsType<ViewerChoiceCommand>(toolbar.Find(ViewerCommandIds.LayoutMode));
        Assert.IsType<ViewerButtonCommand>(toolbar.Find(ViewerCommandIds.Rotate));
        Assert.IsType<ViewerSearchCommand>(toolbar.Find(ViewerCommandIds.Search));
        Assert.IsType<ViewerToggleCommand>(toolbar.Find(ViewerCommandIds.DarkMode));
        Assert.All(toolbar.Items, item => Assert.Equal(ViewerCommandPlacement.Primary, item.Placement));
        Assert.All(toolbar.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Label)));
        Assert.Null(toolbar.Find("nope"));
        Assert.True(toolbar.IsVisible);
    }

    [Fact]
    public async Task Without_a_session_every_built_in_command_is_disabled_and_an_apps_own_command_decides_for_itself()
    {
        var viewer = new FakeViewer();
        using var toolbar = new ViewerToolbar(viewer);
        int ran = 0;
        var custom = new ViewerButtonCommand("app.share", "Share", () => ran++);
        toolbar.Insert(0, custom);

        Assert.All(toolbar.Items.Skip(1), item => Assert.False(item.IsEnabled, item.Id));
        Assert.True(custom.IsEnabled);
        custom.Execute();
        Assert.Equal(1, ran);

        // Nothing happens through a disabled command, and nothing throws.
        ((ViewerButtonCommand)toolbar.Find(ViewerCommandIds.PageNext)!).Execute();
        Assert.False(((ViewerPageCommand)toolbar.Find(ViewerCommandIds.PageNumber)!).TrySubmit("2"));
        ((ViewerSearchCommand)toolbar.Find(ViewerCommandIds.Search)!).Submit("fox");
        ((ViewerToggleCommand)toolbar.Find(ViewerCommandIds.DarkMode)!).IsChecked = true;
        ((ViewerChoiceCommand)toolbar.Find(ViewerCommandIds.ZoomMode)!).Selected = 0;
        Assert.Empty(viewer.Calls);

        await using DocumentSession session = Open(ManyPagesDocument.Create(3));
        viewer.Open(session);
        Assert.All(toolbar.Items, item => Assert.True(item.IsEnabled || item.Id == ViewerCommandIds.PagePrevious, item.Id));
    }

    [Fact]
    public async Task The_page_buttons_step_through_the_pages_and_disable_at_the_ends()
    {
        await using DocumentSession session = Open(ManyPagesDocument.Create(3));
        var viewer = new FakeViewer();
        viewer.Open(session);
        using var toolbar = new ViewerToolbar(viewer);
        var previous = (ViewerButtonCommand)toolbar.Find(ViewerCommandIds.PagePrevious)!;
        var next = (ViewerButtonCommand)toolbar.Find(ViewerCommandIds.PageNext)!;
        var page = (ViewerPageCommand)toolbar.Find(ViewerCommandIds.PageNumber)!;

        Assert.False(previous.IsEnabled);
        Assert.True(next.IsEnabled);
        Assert.Equal(("1", 3), (page.Text, page.PageCount));

        next.Execute();
        next.Execute();
        Assert.Equal(2, viewer.PageIndex);
        Assert.Equal("3", page.Text);
        Assert.False(next.IsEnabled);
        Assert.True(previous.IsEnabled);

        previous.Execute();
        Assert.Equal(1, viewer.PageIndex);
        Assert.Equal(["NextPage", "NextPage", "PreviousPage"], viewer.Calls);
        Assert.Equal(("PageUp", "PageDown"), (previous.Shortcut, next.Shortcut));
    }

    [Fact]
    public async Task The_page_box_takes_numbers_and_labels_and_refuses_what_names_no_page_without_moving()
    {
        await using DocumentSession session = Open(Corpus.Bytes("page-labels.pdf"));
        var viewer = new FakeViewer();
        viewer.Open(session);
        using var toolbar = new ViewerToolbar(viewer);
        var page = (ViewerPageCommand)toolbar.Find(ViewerCommandIds.PageNumber)!;

        // The viewer has no labels of its own: the toolbar reads them through the session.
        await toolbar.DocumentRead;
        Assert.Equal("i", page.Text);

        Assert.True(page.TrySubmit(" iii "));
        Assert.Equal(2, viewer.PageIndex);
        Assert.Equal("iii", page.Text);
        Assert.True(page.TrySubmit("iv")); // ignoring case, the first page with it (IV)
        Assert.Equal(5, viewer.PageIndex);
        Assert.True(page.TrySubmit("1")); // the label "1" (the fourth page) wins over the page number
        Assert.Equal(3, viewer.PageIndex);
        Assert.True(page.TrySubmit("12")); // no page is labeled 12: the twelfth page
        Assert.Equal(11, viewer.PageIndex);
        Assert.Equal("Cover", page.Text);
        Assert.Null(page.Error);

        int changes = 0;
        page.Changed += (_, _) => changes++;
        Assert.False(page.TrySubmit("nope"));
        Assert.False(page.TrySubmit("13"));
        Assert.False(page.TrySubmit("0"));
        Assert.False(page.TrySubmit(""));
        Assert.Equal(11, viewer.PageIndex);
        Assert.Equal("Type a page number or label.", page.Error);
        Assert.True(changes >= 1);

        Assert.True(page.TrySubmit("A-zz"));
        Assert.Equal(9, viewer.PageIndex);
        Assert.Null(page.Error);
    }

    [Fact]
    public async Task The_page_box_uses_the_viewers_labels_when_it_has_them()
    {
        await using DocumentSession session = Open(Corpus.Bytes("page-labels.pdf"));
        var viewer = new FakeViewer();
        viewer.Open(session);
        viewer.PageLabels = await PageLabelTable.ReadAsync(session, Token);
        using var toolbar = new ViewerToolbar(viewer);
        var page = (ViewerPageCommand)toolbar.Find(ViewerCommandIds.PageNumber)!;

        Assert.Equal("i", page.Text);
        Assert.True(page.TrySubmit("V"));
        Assert.Equal(6, viewer.PageIndex);
    }

    [Fact]
    public async Task While_a_remote_document_is_still_opening_the_page_commands_wait_for_its_page_count()
    {
        await using DocumentSession session = Open(ManyPagesDocument.Create(4));
        var viewer = new FakeViewer();
        viewer.Open(session, pageCount: 0);
        using var toolbar = new ViewerToolbar(viewer);
        var page = (ViewerPageCommand)toolbar.Find(ViewerCommandIds.PageNumber)!;

        Assert.False(page.IsEnabled);
        Assert.Equal((string.Empty, 0), (page.Text, page.PageCount));
        Assert.False(toolbar.Find(ViewerCommandIds.PageNext)!.IsEnabled);
        Assert.False(toolbar.Find(ViewerCommandIds.ZoomIn)!.IsEnabled);

        int changed = 0;
        toolbar.Changed += (_, _) => changed++;
        viewer.PageCount = 4;
        viewer.Raise();
        Assert.Equal(1, changed); // one notification however many items changed
        Assert.True(page.IsEnabled);
        Assert.Equal(("1", 4), (page.Text, page.PageCount));
        Assert.True(toolbar.Find(ViewerCommandIds.PageNext)!.IsEnabled);
    }

    [Fact]
    public async Task Zoom_steps_by_a_quarter_and_the_picker_offers_the_fit_modes_and_the_presets()
    {
        await using DocumentSession session = Open(ManyPagesDocument.Create(2));
        var viewer = new FakeViewer();
        viewer.Open(session);
        using var toolbar = new ViewerToolbar(viewer, new ViewerToolbarOptions().WithZoomPresets(0.5, 1, 2));
        var zoomIn = (ViewerButtonCommand)toolbar.Find(ViewerCommandIds.ZoomIn)!;
        var zoomOut = (ViewerButtonCommand)toolbar.Find(ViewerCommandIds.ZoomOut)!;
        var picker = (ViewerChoiceCommand)toolbar.Find(ViewerCommandIds.ZoomMode)!;

        Assert.Equal(["Fit width", "Fit page", "Actual size", "50%", "100%", "200%"], picker.Choices);
        Assert.Equal(4, picker.Selected); // 100 % is a preset
        Assert.Equal("100%", picker.Text);

        zoomIn.Execute();
        Assert.Equal(1.25, viewer.Zoom, 9);
        Assert.Equal(-1, picker.Selected);
        Assert.Equal("125%", picker.Text);
        zoomOut.Execute();
        zoomOut.Execute();
        Assert.Equal(0.8, viewer.Zoom, 9);

        picker.Selected = 5;
        Assert.Equal(2, viewer.Zoom, 9);
        Assert.Equal((5, "200%"), (picker.Selected, picker.Text));

        picker.Selected = 0;
        Assert.Equal(ZoomMode.FitWidth, viewer.LastZoomMode);
        Assert.Equal(0, picker.Selected); // the fit mode stays selected while the view fits
        Assert.Equal("130%", picker.Text);
        picker.Selected = 1;
        Assert.Equal(ZoomMode.FitPage, viewer.LastZoomMode);
        picker.Selected = 2;
        Assert.Equal(ZoomMode.ActualSize, viewer.LastZoomMode);
        Assert.Equal(2, picker.Selected);
        zoomIn.Execute();
        Assert.Equal(-1, picker.Selected);

        Assert.Throws<ArgumentOutOfRangeException>(() => picker.Selected = 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewerToolbarOptions().WithZoomPresets(0));
        Assert.Equal(ViewerToolbarOptions.DefaultZoomPresets, new ViewerToolbarOptions().ZoomPresets);
    }

    [Fact]
    public async Task The_layout_picker_and_rotate_change_the_layout_and_keep_its_other_settings()
    {
        await using DocumentSession session = Open(ManyPagesDocument.Create(2));
        var viewer = new FakeViewer();
        viewer.Open(session);
        using var toolbar = new ViewerToolbar(viewer);
        var layout = (ViewerChoiceCommand)toolbar.Find(ViewerCommandIds.LayoutMode)!;
        var rotate = (ViewerButtonCommand)toolbar.Find(ViewerCommandIds.Rotate)!;

        Assert.Equal(Enum.GetValues<PageLayoutMode>().Length, layout.Choices.Count);
        Assert.Equal((int)PageLayoutMode.Continuous, layout.Selected); // the default layout
        viewer.SetLayout(new PageLayoutOptions().WithCover(true).WithGap(3));

        layout.Selected = (int)PageLayoutMode.TwoUp;
        Assert.Equal(PageLayoutMode.TwoUp, viewer.LayoutOptions!.Mode);
        Assert.True(viewer.LayoutOptions.ShowCover);
        Assert.Equal(3, viewer.LayoutOptions.Gap);
        Assert.Equal(((int)PageLayoutMode.TwoUp, "Two-up"), (layout.Selected, layout.Text));

        rotate.Execute();
        Assert.Equal(90, viewer.LayoutOptions.Rotation);
        Assert.Equal(PageLayoutMode.TwoUp, viewer.LayoutOptions.Mode);
        rotate.Execute();
        rotate.Execute();
        rotate.Execute();
        Assert.Equal(0, viewer.LayoutOptions.Rotation);
    }

    [Fact]
    public async Task Dark_mode_sets_the_viewers_color_filter_and_follows_the_session()
    {
        await using DocumentSession session = Open(ManyPagesDocument.Create(1));
        var viewer = new FakeViewer();
        viewer.Open(session);
        using var toolbar = new ViewerToolbar(viewer);
        var dark = (ViewerToggleCommand)toolbar.Find(ViewerCommandIds.DarkMode)!;

        Assert.False(dark.IsChecked);
        dark.IsChecked = true;
        Assert.Equal(ColorFilter.DarkMode, viewer.ColorFilter);
        Assert.Equal(ColorFilter.DarkMode, session.ColorFilter);
        Assert.True(dark.IsChecked);

        dark.IsChecked = false;
        Assert.Equal(ColorFilter.None, session.ColorFilter);
        Assert.False(dark.IsChecked);

        // A filter set elsewhere (the app, the session's options) shows in the toggle.
        session.ColorFilter = ColorFilter.DarkMode;
        viewer.Raise();
        Assert.True(dark.IsChecked);
    }

    [Fact]
    public void Find_reports_progress_steps_through_the_matches_and_scrolls_each_into_view()
    {
        // Matches arrive on the search's workers; the toolbar hands them to the thread it was created on (here a pumped context).
        using DocumentSession session = Open(ManyPagesDocument.CreateWithText(2, lines: 2), ViewerThreading.Background);
        using var ui = new PumpedContext();
        var viewer = new FakeViewer();
        viewer.Open(session);
        using var toolbar = new ViewerToolbar(viewer);
        var find = (ViewerSearchCommand)toolbar.Find(ViewerCommandIds.Search)!;
        Assert.Equal(string.Empty, find.Status);

        find.Submit("fox");
        ui.WaitUntil(() => find.Status == "1 of 4" && !viewer.Search!.IsIndexing);
        Assert.Equal("fox", find.Query);
        Assert.Single(viewer.Revealed);
        Assert.Equal(0, viewer.Revealed[0].PageIndex);

        find.Next();
        Assert.Equal("2 of 4", find.Status);
        find.Next();
        Assert.Equal("3 of 4", find.Status);
        Assert.Equal(1, viewer.Revealed[^1].PageIndex);
        find.Previous();
        find.Previous();
        find.Previous();
        Assert.Equal("4 of 4", find.Status); // wraps around
        Assert.Equal(6, viewer.Revealed.Count);

        find.Submit("unicorn");
        ui.WaitUntil(() => find.Status == "No matches");

        find.Submit(" ");
        Assert.Equal((string.Empty, string.Empty), (find.Query, find.Status));
    }

    [Fact]
    public void Find_in_a_document_without_text_says_so()
    {
        using DocumentSession session = Open(ManyPagesDocument.Create(2), ViewerThreading.Background);
        using var ui = new PumpedContext();
        var viewer = new FakeViewer();
        viewer.Open(session);
        using var toolbar = new ViewerToolbar(viewer);
        var find = (ViewerSearchCommand)toolbar.Find(ViewerCommandIds.Search)!;

        find.Submit("anything");
        ui.WaitUntil(() => find.Status == "No text in this document");
        Assert.Empty(viewer.Revealed);
    }

    [Fact]
    public void Options_choose_the_built_in_commands_their_order_and_a_compact_layout()
    {
        var viewer = new FakeViewer();
        using var toolbar = new ViewerToolbar(
            viewer, new ViewerToolbarOptions().WithItems(ViewerCommandIds.Search, ViewerCommandIds.PageNumber, ViewerCommandIds.ZoomIn).WithCompact(true));

        Assert.Equal([ViewerCommandIds.Search, ViewerCommandIds.PageNumber, ViewerCommandIds.ZoomIn], toolbar.Items.Select(item => item.Id));
        Assert.Equal(
            [ViewerCommandPlacement.Primary, ViewerCommandPlacement.Primary, ViewerCommandPlacement.Overflow],
            toolbar.Items.Select(item => item.Placement));

        Assert.Throws<ArgumentException>(() => new ViewerToolbarOptions().WithItems("page.unknown"));
        Assert.Throws<ArgumentException>(() => new ViewerToolbarOptions().WithItems(ViewerCommandIds.Rotate, ViewerCommandIds.Rotate));
        Assert.False(new ViewerToolbarOptions().Compact);
        Assert.True(new ViewerToolbarOptions().HonorHideToolbar);
    }

    [Fact]
    public async Task Apps_add_reorder_hide_and_remove_commands_and_every_change_is_announced()
    {
        await using DocumentSession session = Open(ManyPagesDocument.Create(2));
        var viewer = new FakeViewer();
        viewer.Open(session);
        using var toolbar = new ViewerToolbar(viewer);
        int changes = 0;
        toolbar.Changed += (_, _) => changes++;

        bool canShare = false;
        var share = new ViewerButtonCommand("app.share", "Share", () => { }, () => canShare) { Glyph = "⇪", ToolTip = "Share the file" };
        bool grid = false;
        var toggle = new ViewerToggleCommand("app.grid", "Grid", () => grid, value => grid = value);
        int picked = 0;
        var choice = new ViewerChoiceCommand("app.theme", "Theme", ["Light", "Sepia"], () => picked, value => picked = value);
        toolbar.Insert(toolbar.Items.Count, share);
        toolbar.Insert(0, toggle);
        toolbar.Insert(1, choice);
        Assert.Equal(3, changes);
        Assert.Equal(["app.grid", "app.theme", ViewerCommandIds.PagePrevious], toolbar.Items.Take(3).Select(item => item.Id));
        Assert.Same(share, toolbar.Items[^1]);
        Assert.False(share.IsEnabled);

        canShare = true;
        toolbar.Refresh();
        Assert.True(share.IsEnabled);
        toggle.IsChecked = true;
        Assert.True(grid);
        Assert.True(toggle.IsChecked);
        choice.Selected = 1;
        Assert.Equal((1, "Sepia"), (choice.Selected, choice.Text));

        changes = 0;
        toolbar.Find(ViewerCommandIds.Rotate)!.Placement = ViewerCommandPlacement.Hidden;
        Assert.Equal(1, changes);
        Assert.Throws<ArgumentException>(() => toolbar.Insert(0, new ViewerButtonCommand("app.share", "Again", () => { })));
        Assert.Throws<ArgumentOutOfRangeException>(() => toolbar.Insert(99, new ViewerButtonCommand("app.other", "Other", () => { })));

        Assert.True(toolbar.Remove(ViewerCommandIds.DarkMode));
        Assert.False(toolbar.Remove(ViewerCommandIds.DarkMode));
        Assert.Null(toolbar.Find(ViewerCommandIds.DarkMode));
        ViewerCommand dark = toolbar.CreateBuiltIn(ViewerCommandIds.DarkMode);
        toolbar.Insert(0, dark);
        Assert.True(dark.IsEnabled);
        Assert.Throws<ArgumentException>(() => toolbar.CreateBuiltIn("nope"));

        using var other = new ViewerToolbar(viewer);
        Assert.Throws<ArgumentException>(() => other.Insert(0, share));
    }

    [Fact]
    public async Task A_document_that_asks_to_hide_the_toolbar_hides_it_unless_the_options_say_otherwise()
    {
        await using DocumentSession session = Open(Corpus.Bytes("viewer-preferences.pdf"));
        Assert.True(session.Document.ViewerPreferences!.HideToolbar);
        var viewer = new FakeViewer();
        using var toolbar = new ViewerToolbar(viewer);
        using var shown = new ViewerToolbar(viewer, new ViewerToolbarOptions().WithHonorHideToolbar(false));
        int changes = 0;
        toolbar.Changed += (_, _) => changes++;

        viewer.Open(session);
        await toolbar.DocumentRead;
        await shown.DocumentRead;
        Assert.False(toolbar.IsVisible);
        Assert.True(changes >= 1);
        Assert.True(shown.IsVisible);

        viewer.Close();
        Assert.True(toolbar.IsVisible);
    }

    [Fact]
    public async Task A_disposed_toolbar_stops_following_the_viewer()
    {
        await using DocumentSession session = Open(ManyPagesDocument.Create(3));
        var viewer = new FakeViewer();
        viewer.Open(session);
        var toolbar = new ViewerToolbar(viewer);
        var page = (ViewerPageCommand)toolbar.Find(ViewerCommandIds.PageNumber)!;
        toolbar.Dispose();
        toolbar.Dispose();

        viewer.GoToPage(2);
        Assert.Equal("1", page.Text);
        Assert.False(viewer.HasViewChangedHandlers);
    }

    [Fact]
    public async Task The_undo_and_redo_commands_follow_the_views_edit_history()
    {
        Assert.DoesNotContain(ViewerCommandIds.EditUndo, ViewerCommandIds.All);
        var viewer = new FakeViewer();
        using var toolbar = new ViewerToolbar(viewer, new ViewerToolbarOptions().WithItems([.. ViewerCommandIds.All, .. ViewerCommandIds.Editing]));
        var undo = (ViewerButtonCommand)toolbar.Find(ViewerCommandIds.EditUndo)!;
        var redo = (ViewerButtonCommand)toolbar.Find(ViewerCommandIds.EditRedo)!;
        Assert.False(undo.IsEnabled || redo.IsEnabled);

        await using DocumentSession session = Open(AnnotationSamples.Grid(3, 1));
        var layer = new AnnotationLayer(session) { History = new EditHistory(session) };
        await layer.ReadPageAsync(0, Token);
        viewer.Annotations = layer;
        viewer.Open(session);
        Assert.False(undo.IsEnabled);

        AnnotationTarget square = Assert.NotNull(layer.HitTest(new PagePoint(0, 234, 30)));
        PdfRectangle rect = layer.GetRectangle(square);
        Assert.True(layer.Move(square, 10, 0));
        Assert.True(undo.IsEnabled);
        Assert.False(redo.IsEnabled);

        undo.Execute();
        Assert.Equal(rect, layer.GetRectangle(square));
        Assert.False(undo.IsEnabled);
        Assert.True(redo.IsEnabled);
        redo.Execute();
        Assert.Equal(rect.Left + 10, layer.GetRectangle(square).Left, 6);
        Assert.True(undo.IsEnabled);
    }

    private static DocumentSession Open(byte[] bytes, ViewerThreading threading = ViewerThreading.Cooperative) =>
        DocumentSession.Open(bytes, new ViewerOptions().WithDocumentOptions(new PdfOptions().UseStandard14Fonts()).WithThreading(threading));

    /// <summary>A UI thread stand-in: posts queue up until the test pumps them, on its own thread.</summary>
    private sealed class PumpedContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly SynchronizationContext? _previous = Current;

        public PumpedContext() => SetSynchronizationContext(this);

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void WaitUntil(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                while (_queue.TryDequeue(out (SendOrPostCallback Callback, object? State) item))
                {
                    item.Callback(item.State);
                }

                if (condition())
                {
                    return;
                }

                Assert.True(DateTime.UtcNow < deadline, "timed out");
                Thread.Sleep(5);
            }
        }

        public void Dispose() => SetSynchronizationContext(_previous);
    }

    /// <summary>A viewer without a view: it records the calls and moves as the toolbar asks.</summary>
    private sealed class FakeViewer : IDocumentViewer
    {
        private ColorFilter? _colorFilter;

        public event EventHandler? ViewChanged;

        public DocumentSession? Session { get; private set; }

        public int PageIndex { get; private set; }

        public int PageCount { get; set; }

        public double Zoom { get; private set; } = 1;

        public TextSelection? Selection => null;

        public DocumentSearch? Search { get; private set; }

        public AnnotationLayer? Annotations { get; set; }

        public PageLabelTable? PageLabels { get; set; }

        public PageLayoutOptions? LayoutOptions { get; private set; }

        public ColorFilter? ColorFilter
        {
            get => _colorFilter;
            set
            {
                _colorFilter = value;
                if (value is { } filter && Session is { } session)
                {
                    session.ColorFilter = filter;
                }

                Raise();
            }
        }

        public List<string> Calls { get; } = [];

        public List<(int PageIndex, PdfRectangle Area)> Revealed { get; } = [];

        public ZoomMode? LastZoomMode { get; private set; }

        public bool HasViewChangedHandlers => ViewChanged is not null;

        public void Open(DocumentSession session, int? pageCount = null)
        {
            Session = session;
            PageCount = pageCount ?? session.PageCount;
            PageIndex = 0;
            Search = new DocumentSearch(session);
            Raise();
        }

        public void Close()
        {
            Search?.Dispose();
            (Session, Search, PageCount, PageIndex) = (null, null, 0, 0);
            Raise();
        }

        public void Raise() => ViewChanged?.Invoke(this, EventArgs.Empty);

        public void GoToPage(int pageIndex)
        {
            Calls.Add($"GoToPage {pageIndex}");
            PageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, PageCount - 1));
            Raise();
        }

        public void NextPage()
        {
            Calls.Add("NextPage");
            PageIndex = Math.Min(PageIndex + 1, PageCount - 1);
            Raise();
        }

        public void PreviousPage()
        {
            Calls.Add("PreviousPage");
            PageIndex = Math.Max(PageIndex - 1, 0);
            Raise();
        }

        public void GoTo(ViewTarget target) => GoToPage(target.PageIndex);

        public void ZoomTo(ZoomMode mode)
        {
            Calls.Add($"ZoomTo {mode}");
            LastZoomMode = mode;
            Zoom = mode switch
            {
                ZoomMode.FitWidth => 1.3,
                ZoomMode.FitPage => 0.7,
                _ => 1,
            };
            Raise();
        }

        public void ZoomBy(double factor)
        {
            Calls.Add("ZoomBy");
            Zoom *= factor;
            Raise();
        }

        public void SetLayout(PageLayoutOptions options)
        {
            LayoutOptions = options;
            Raise();
        }

        public void Reveal(int pageIndex, PdfRectangle area) => Revealed.Add((pageIndex, area));
    }
}
