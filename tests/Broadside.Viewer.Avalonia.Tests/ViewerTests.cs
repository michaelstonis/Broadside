using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Avalonia.Tests;

/// <summary>The Avalonia viewer driven through real (headless) input: selection, search, touch gestures, keys, links, forms, dark mode, threading.</summary>
public sealed class ViewerTests
{
    [Fact]
    public Task A_document_opens_at_the_top_of_page_one_at_fit_width() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("text-standard14.pdf"), scaling: 2);
        Viewport viewport = viewer.Viewport;
        Assert.Equal(1200, viewport.Width);
        Assert.Equal(0, viewport.Y);
        Assert.Equal(viewer.Layout.GetZoom(ZoomMode.FitWidth, viewport.Width, viewport.Height, 0, 2 * 96.0 / 72), viewport.Zoom, 9);
        Assert.Equal(new PdfViewState(0, 1, viewport.Zoom / (2 * 96.0 / 72), false, false), viewer.Viewer.State);
        PinnedSubsetTests.AssertTilesOnScreen(viewer, 0);
    });

    [Fact]
    public Task A_copy_still_loading_its_pages_when_the_document_is_replaced_or_the_view_closes_copies_nothing_and_does_not_throw() => Ui.RunAsync(async () =>
    {
        // Select all of a long document, then Copy: its pages load one by one. Opening another document, or closing the view, stops
        // the loads; the copy has nothing to copy and returns null rather than throwing into the host's click handler.
        string path = Path.Combine(Path.GetTempPath(), $"broadside-copy-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, ManyPagesDocument.CreateWithText(40), TestContext.Current.CancellationToken);
        try
        {
            ViewerHarness viewer = await ViewerHarness.OpenAsync(path);
            Task<string?> copy;
            try
            {
                viewer.Viewer.Selection!.SelectAll();
                copy = viewer.Viewer.CopySelectionAsync();
                Assert.False(copy.IsCompleted);
                await viewer.Controller.OpenAsync(_ => Task.FromResult(DocumentSession.Open(ManyPagesDocument.CreateWithText(40), viewer.Viewer.Options)), ownsSession: true, null);
                Assert.Null(await copy);

                await viewer.SettleAsync();
                viewer.Viewer.Selection!.SelectAll();
                copy = viewer.Viewer.CopySelectionAsync();
                Assert.False(copy.IsCompleted);
            }
            finally
            {
                viewer.Dispose();
            }

            Assert.Null(await copy);
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public Task A_mouse_drag_over_text_selects_it_and_copy_puts_it_on_the_clipboard() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("text-render-modes.pdf"));
        Point start = FindText(viewer);
        Viewport before = viewer.Viewport;

        viewer.Drag(start, start + new Point(80, 0));
        await viewer.SettleAsync();
        TextSelection selection = viewer.Viewer.Selection!;
        Assert.False(selection.IsEmpty);
        Assert.Equal(before, viewer.Viewport);
        string? text = await selection.GetTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(text));

        // The selection is drawn over the tiles (blue tint at the text).
        FramePixels frame = viewer.Capture();
        (byte b, byte g, byte r, _) = frame[(int)((start.X + 4) * viewer.Scaling), (int)(start.Y * viewer.Scaling)];
        Assert.True(b > r, $"selection tint expected, got ({r},{g},{b})");

        viewer.Key(Key.C, RawInputModifiers.Control);
        await viewer.SettleAsync();
        IClipboard clipboard = viewer.Window.Clipboard!;
        Assert.Equal(text, await clipboard.TryGetTextAsync());

        // A drag that starts off the text pans instead (touch pans everywhere).
        viewer.Viewer.ZoomBy(3);
        await viewer.SettleAsync();
        Viewport zoomed = viewer.Viewport;
        IHeadlessTouchPointer finger = viewer.Window.TouchBegin(new Point(300, 500), RawInputModifiers.None);
        for (int i = 1; i <= 8; i++)
        {
            viewer.Clock.Advance(TimeSpan.FromMilliseconds(16));
            viewer.Window.TouchMove(finger, new Point(300, 500 - (i * 20)), RawInputModifiers.None);
        }

        viewer.Window.TouchEnd(finger, new Point(300, 340), RawInputModifiers.None);
        await viewer.SettleAsync();
        Assert.True(selection.IsEmpty);
        Assert.True(viewer.Viewport.Y > zoomed.Y, $"{viewer.Viewport} after {zoomed}");
    });

    [Fact]
    public Task A_mouse_release_away_from_the_press_selects_up_to_it_when_no_move_arrived_between() => Ui.RunAsync(async () =>
    {
        // A fast drag can end before a move reaches the view at the release point: the release still ends the selection there.
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("text-render-modes.pdf"));
        Point start = FindText(viewer);
        Viewport before = viewer.Viewport;

        viewer.Window.MouseDown(start, MouseButton.Left);
        TextSelection selection = viewer.Viewer.Selection!;
        Assert.True(selection.IsEmpty);
        viewer.Window.MouseUp(start + new Point(80, 0), MouseButton.Left);
        await viewer.SettleAsync();
        Assert.False(selection.IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(await selection.GetTextAsync()));
        Assert.Equal(before, viewer.Viewport);
    });

    [Fact]
    public Task A_double_click_selects_a_word_and_ctrl_a_selects_the_page() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("text-standard14.pdf"));
        Point word = FindText(viewer);
        viewer.Click(word);
        viewer.Clock.Advance(TimeSpan.FromMilliseconds(100));
        viewer.Click(word);
        await viewer.SettleAsync();
        TextSelection selection = viewer.Viewer.Selection!;
        Assert.Equal(SelectionGranularity.Word, selection.Granularity);
        string first = (await selection.GetTextAsync())!;
        Assert.Contains("Hello", first, StringComparison.Ordinal);

        viewer.Key(Key.A, RawInputModifiers.Control);
        await viewer.SettleAsync();
        Assert.Equal("Hello, Broadside", (await selection.GetTextAsync())!.Trim());
    });

    [Fact]
    public Task Search_highlights_every_match_reveals_the_current_one_and_steps_with_F3() => Ui.RunAsync(async () =>
    {
        string path = ViewerHarness.RealWorldFile("pdfjs/tracemonkey.pdf") ?? ViewerHarness.CorpusFile("text-standard14.pdf");
        bool real = !path.EndsWith("text-standard14.pdf", StringComparison.Ordinal);
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(path);
        string query = real ? "trace" : "Broadside";

        viewer.Viewer.Find(query);
        await WaitForAsync(viewer, () => viewer.Viewer.Search!.IsSearchComplete);
        DocumentSearch search = viewer.Viewer.Search!;
        Assert.True(search.MatchCount >= (real ? 10 : 1), $"{search.MatchCount} matches");
        SearchMatch current = Assert.IsType<SearchMatch>(search.Current);
        AssertShown(viewer, current);
        AssertTinted(viewer, current, expectHighlight: true);

        if (real)
        {
            // F3 walks the matches; one far down the document is scrolled into view.
            viewer.Viewer.Focus();
            for (int i = 0; i < 12; i++)
            {
                viewer.Key(Key.F3);
            }

            await viewer.SettleAsync();
            SearchMatch later = search.Current!;
            Assert.NotSame(current, later);
            AssertShown(viewer, later);
            AssertTinted(viewer, later, expectHighlight: true);
            viewer.Key(Key.F3, RawInputModifiers.Shift);
            await viewer.SettleAsync();
            Assert.Equal(search.Matches.ToList().IndexOf(later) - 1, search.CurrentIndex);
        }

        // Clearing removes the highlights without drawing any tile again.
        viewer.ClearEvents();
        SearchMatch shown = search.Current!;
        viewer.Viewer.ClearSearch();
        await viewer.SettleAsync();
        Assert.Equal(0, search.MatchCount);
        Assert.DoesNotContain(viewer.Events, e => e.Kind == TileEventKind.Started);
        AssertTinted(viewer, shown, expectHighlight: false);

        static void AssertShown(ViewerHarness viewer, SearchMatch match)
        {
            LayoutRectangle box = viewer.Layout.PageToViewport(viewer.Viewport, match.PageIndex, match.Bounds);
            Assert.True(box.Y >= 0 && box.Bottom <= viewer.Viewport.Height && box.X >= 0 && box.Right <= viewer.Viewport.Width, $"match at {box} outside {viewer.Viewport}");
        }

        static void AssertTinted(ViewerHarness viewer, SearchMatch match, bool expectHighlight)
        {
            // The match's box: highlighted text is yellow-orange over white paper (blue well below red); cleared, it is paper and ink again.
            LayoutRectangle box = viewer.Layout.PageToViewport(viewer.Viewport, match.PageIndex, match.Bounds);
            FramePixels frame = viewer.Capture();
            int tinted = 0;
            int total = 0;
            for (double y = box.Y + 1; y < box.Bottom - 1; y += 2)
            {
                for (double x = box.X + 1; x < box.Right - 1; x += 2)
                {
                    (byte b, _, byte r, _) = frame[(int)x, (int)y];
                    total++;
                    tinted += r - b > 60 ? 1 : 0;
                }
            }

            Assert.True(total > 0);
            Assert.True(expectHighlight ? tinted > total / 2 : tinted == 0, $"{tinted} of {total} tinted");
        }
    });

    [Fact]
    public Task A_touch_pinch_zooms_around_the_fingers_and_a_double_tap_toggles_twice_fit_width() => Ui.RunAsync(async () =>
    {
        // A document taller than the window: a page shorter than the window is centred, which no zoom anchor can keep.
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.SampleDocument("textbook.pdf"), 390, 844);
        double fit = viewer.Viewport.Zoom;
        PagePoint anchorBefore = PageAt(viewer, new Point(200, 300));

        IHeadlessTouchPointer a = viewer.Window.TouchBegin(new Point(150, 300), RawInputModifiers.None);
        IHeadlessTouchPointer b = viewer.Window.TouchBegin(new Point(250, 300), RawInputModifiers.None);
        for (int step = 1; step <= 10; step++)
        {
            viewer.Clock.Advance(TimeSpan.FromMilliseconds(16));
            viewer.Window.TouchMove(a, new Point(150 - (step * 8), 300), RawInputModifiers.None);
            viewer.Window.TouchMove(b, new Point(250 + (step * 8), 300), RawInputModifiers.None);
        }

        // Mid-gesture the page point between the fingers stays between them: exactly across the motion, within a few points along it
        // (each finger moves in turn, so the centroid wobbles by half a step while the zoom changes).
        Assert.True(viewer.Viewport.Zoom > fit * 1.5, $"zoom {viewer.Viewport.Zoom} after {fit}");
        PagePoint anchorAfter = PageAt(viewer, new Point(200, 300));
        Assert.True(Math.Abs(anchorBefore.X - anchorAfter.X) < 8 && Math.Abs(anchorBefore.Y - anchorAfter.Y) < 0.01, $"{anchorAfter} after {anchorBefore}");
        viewer.Window.TouchEnd(b, new Point(330, 300), RawInputModifiers.None);
        viewer.Window.TouchEnd(a, new Point(70, 300), RawInputModifiers.None);
        await viewer.SettleAsync();
        Assert.True(viewer.Viewport.Zoom > fit * 1.5, $"zoom {viewer.Viewport.Zoom} after {fit}");

        viewer.Viewer.ZoomTo(ZoomMode.FitWidth);
        await viewer.SettleAsync();
        Assert.Equal(fit, viewer.Viewport.Zoom, 6);
        await DoubleTapAsync(viewer, new Point(120, 200));
        Assert.Equal(fit * 2, viewer.Viewport.Zoom, 6);
        await DoubleTapAsync(viewer, new Point(120, 200));
        Assert.Equal(fit, viewer.Viewport.Zoom, 6);
        PinnedSubsetTests.AssertTilesOnScreen(viewer, 0);

        static async Task DoubleTapAsync(ViewerHarness viewer, Point at)
        {
            for (int i = 0; i < 2; i++)
            {
                IHeadlessTouchPointer finger = viewer.Window.TouchBegin(at, RawInputModifiers.None);
                viewer.Clock.Advance(TimeSpan.FromMilliseconds(40));
                viewer.Window.TouchEnd(finger, at, RawInputModifiers.None);
                viewer.Clock.Advance(TimeSpan.FromMilliseconds(100));
            }

            await viewer.SettleAsync();
        }
    });

    [Fact]
    public Task Keys_page_through_a_document_and_ctrl_wheel_zooms() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.SampleDocument("textbook.pdf"));
        viewer.Viewer.Focus();
        viewer.Key(Key.End);
        await viewer.SettleAsync();
        Assert.Equal(viewer.Viewer.State.PageCount - 1, viewer.Viewer.State.PageIndex);
        viewer.Key(Key.Home);
        await viewer.SettleAsync();
        Assert.Equal(0, viewer.Viewer.State.PageIndex);
        double y = viewer.Viewport.Y;
        viewer.Key(Key.PageDown);
        await viewer.SettleAsync();
        Assert.True(viewer.Viewport.Y > y);

        double zoom = viewer.Viewport.Zoom;
        viewer.Window.MouseWheel(new Point(300, 300), new Vector(0, 2), RawInputModifiers.Control);
        await viewer.SettleAsync();
        Assert.True(viewer.Viewport.Zoom > zoom, $"zoom {viewer.Viewport.Zoom} after {zoom}");
        PinnedSubsetTests.AssertTilesOnScreen(viewer, viewer.Viewer.State.PageIndex);
    });

    [Fact]
    public Task A_uri_link_is_raised_and_never_followed() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("annotations-link.pdf"));
        Viewport before = viewer.Viewport;
        viewer.Click(viewer.ToView(0, 100, 705));
        await viewer.SettleAsync();
        ViewerActionEventArgs action = Assert.Single(viewer.Actions);
        Assert.Equal(new Uri("https://example.com/"), action.Uri);
        Assert.Equal(before, viewer.Viewport);
    });

    [Fact]
    public Task Clicking_a_text_field_opens_an_editor_whose_text_commits_to_the_field() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("acroform-fields.pdf"));
        AnnotationLayer layer = viewer.Viewer.Annotations!;
        AnnotationTarget field = FindField<PdfTextField>(layer);
        LayoutRectangle box = layer.GetScreenRectangle(field, viewer.Layout, viewer.Viewport);
        viewer.Click(new Point((box.X + (box.Width / 2)) / viewer.Scaling, (box.Y + (box.Height / 2)) / viewer.Scaling));
        await viewer.SettleAsync();

        TextBox editor = Assert.IsType<TextBox>(viewer.Viewer.EditorView);
        Assert.True(editor.IsVisible);
        Assert.Equal(box.X / viewer.Scaling, editor.Bounds.X, 0);
        editor.Text = "Ada";
        viewer.Key(Key.Enter);
        await viewer.SettleAsync();
        Assert.Null(viewer.Viewer.EditorView);
        var widget = (PdfWidgetAnnotation)layer.GetAnnotation(field);
        Assert.Equal("Ada", ((PdfTextField)widget.Field!).Value);
    });

    [Fact]
    public Task The_shared_field_set_fills_through_native_editors_as_the_form_api_does() => Ui.RunAsync(async () =>
    {
        // #543: the text boxes and combo boxes over the widgets only take input; the core form filling cuts the text at MaxLen and
        // per comb, refuses the read-only field and writes each field once, so the form ends as the form API leaves it.
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile(FormFillingScenario.FileName));
        AnnotationLayer layer = viewer.Viewer.Annotations!;
        PdfAcroForm form = layer.Session.Document.AcroForm!;
        Assert.Same(layer, viewer.Viewer.Forms!.Layer);
        IEnumerable<(AnnotationTarget, PdfAnnotation)> Targets() =>
            Enumerable.Range(0, layer.GetCount(0)).Select(i => (new AnnotationTarget(0, i), layer.GetAnnotation(new AnnotationTarget(0, i))));

        async Task<T?> ClickAsync<T>(string field, int widget = 0)
            where T : Control
        {
            AnnotationTarget target = FormFillingScenario.FindWidget(form, field, Targets(), widget);
            LayoutRectangle box = layer.GetScreenRectangle(target, viewer.Layout, viewer.Viewport);
            viewer.Click(new Point((box.X + (box.Width / 2)) / viewer.Scaling, (box.Y + (box.Height / 2)) / viewer.Scaling));
            await viewer.SettleAsync();
            Assert.Equal(target, layer.Focused);
            return viewer.Viewer.EditorView is null ? null : Assert.IsType<T>(viewer.Viewer.EditorView);
        }

        async Task TypeAsync(string field, string text)
        {
            TextBox editor = (await ClickAsync<TextBox>(field))!;
            editor.Text = text;
            viewer.Key(Key.Enter);
            await viewer.SettleAsync();
            Assert.Null(viewer.Viewer.EditorView);
        }

        await TypeAsync("name", FormFillingScenario.NameTyped);
        await TypeAsync("code", FormFillingScenario.CodeTyped);
        Assert.Null(await ClickAsync<Control>("ref"));
        Assert.Null(await ClickAsync<Control>("agree"));
        Assert.Null(await ClickAsync<Control>("size", FormFillingScenario.SizeWidget));

        ComboBox fruit = (await ClickAsync<ComboBox>("fruit"))!;
        fruit.SelectedIndex = FormFillingScenario.Fruit;
        ComboBox country = (await ClickAsync<ComboBox>("country"))!;
        country.SelectedIndex = FormFillingScenario.Country;
        ComboBox city = (await ClickAsync<ComboBox>("city"))!;
        Assert.True(city.IsEditable);
        Assert.Equal("Lyon", city.Text);
        city.Text = FormFillingScenario.City;
        viewer.Controller.EndFieldEdit();
        await viewer.SettleAsync();

        // #560: a multi-select list box opens a native list whose mouse clicks toggle options; the form filling holds every change
        // and writes the picks once, when focus leaves the list.
        var colors = (PdfChoiceField)form.FindField("colors")!;
        ListBox list = (await ClickAsync<ListBox>("colors"))!;
        Assert.Equal(["Red", "Green", "Blue"], list.Items.Cast<string>());
        Assert.Equal([0], list.Selection.SelectedIndexes);
        for (int i = 0; i < FormFillingScenario.ColorsToggled.Count; i++)
        {
            Control option = list.ContainerFromIndex(FormFillingScenario.ColorsToggled[i])!;
            viewer.Click(option.TranslatePoint(new Point(option.Bounds.Width / 2, option.Bounds.Height / 2), viewer.Window)!.Value);
            await viewer.SettleAsync();
            Assert.Same(list, viewer.Viewer.EditorView);
            Assert.Equal(FormFillingScenario.ColorsAfterEachToggle[i], list.Selection.SelectedIndexes);
            Assert.Equal(FormFillingScenario.ColorsAfterEachToggle[i], viewer.Viewer.Forms!.SelectedOptions);
            Assert.Equal([0], colors.SelectedIndices);
        }

        viewer.Controller.EndFieldEdit();
        await viewer.SettleAsync();
        Assert.Null(viewer.Viewer.EditorView);

        Assert.Equal(await FormFillingScenario.ExpectedAsync(TestContext.Current.CancellationToken), FormFillingScenario.Describe(form));
    });

    [Fact]
    public Task Escape_in_a_text_editor_leaves_the_field_unchanged() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile(FormFillingScenario.FileName));
        AnnotationLayer layer = viewer.Viewer.Annotations!;
        AnnotationTarget field = FindField<PdfTextField>(layer);
        LayoutRectangle box = layer.GetScreenRectangle(field, viewer.Layout, viewer.Viewport);
        viewer.Click(new Point((box.X + (box.Width / 2)) / viewer.Scaling, (box.Y + (box.Height / 2)) / viewer.Scaling));
        await viewer.SettleAsync();

        TextBox editor = Assert.IsType<TextBox>(viewer.Viewer.EditorView);
        editor.Text = "Changed";
        viewer.Tick();
        Assert.Equal("Changed", viewer.Viewer.Forms!.Text); // held by the form filling, not yet written
        viewer.Key(Key.Escape);
        await viewer.SettleAsync();
        Assert.Null(viewer.Viewer.EditorView);
        Assert.Equal("Ada", ((PdfTextField)((PdfWidgetAnnotation)layer.GetAnnotation(field)).Field!).Value);
    });

    [Fact]
    public Task Dark_mode_draws_dark_paper_from_the_cached_display_lists() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("text-standard14.pdf"));
        LayoutRectangle page = viewer.Layout.PageToViewport(viewer.Viewport, 0, viewer.Layout.GetPageMetrics(0).CropBox);
        int x = (int)page.X + 6;
        int y = (int)page.Y + 6;
        Assert.Equal((255, 255, 255), Rgb(viewer.Capture()[x, y]));
        long misses = viewer.Controller.Session!.Document.CacheStatistics.DisplayLists.Misses;

        viewer.Viewer.ColorFilter = ColorFilter.DarkMode;
        await viewer.SettleAsync();
        FramePixels dark = viewer.Capture();
        (int r, int g, int b) paper = Rgb(dark[x, y]);
        Assert.True(paper.r < 40 && paper.g < 40 && paper.b < 40, $"paper {paper}");
        Assert.Equal((0x2B, 0x2B, 0x2B), Rgb(dark[1, 1]));
        Assert.Equal(misses, viewer.Controller.Session.Document.CacheStatistics.DisplayLists.Misses);
        PinnedSubsetTests.AssertTilesOnScreen(viewer, 0);

        viewer.Viewer.ColorFilter = ColorFilter.None;
        await viewer.SettleAsync();
        Assert.Equal((255, 255, 255), Rgb(viewer.Capture()[x, y]));

        static (int, int, int) Rgb((byte B, byte G, byte R, byte A) p) => (p.R, p.G, p.B);
    });

    [Fact]
    public Task Tiles_render_on_worker_threads_by_default_and_reach_the_screen() => Ui.RunAsync(async () =>
    {
        int ui = Environment.CurrentManagedThreadId;
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("text-standard14.pdf"), threading: ViewerThreading.Background);
        Assert.NotEmpty(viewer.PresentingThreads);
        Assert.DoesNotContain(ui, viewer.PresentingThreads);
        PinnedSubsetTests.AssertTilesOnScreen(viewer, 0);
    });

    [Fact]
    public Task The_automation_peer_exposes_page_text_in_reading_order_with_structure() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("tagged-structure.pdf"));
        AutomationPeer document = ControlAutomationPeer.CreatePeerForElement(viewer.Viewer);
        Assert.IsType<PdfViewAutomationPeer>(document);
        Assert.Equal(AutomationControlType.Document, document.GetAutomationControlType());

        AutomationPeer page = Assert.Single(document.GetChildren());
        Assert.Equal("Page 1", page.GetName());
        Assert.Same(document, page.GetParent());

        // The peers, depth first, are the bridge's nodes in reading order (structure order on this tagged page), named by their text.
        var peers = new List<AutomationPeer>();
        Flatten(page, peers);
        AccessibilityBridge bridge = viewer.Viewer.Accessibility!;
        var nodes = new List<AccessibleNode>();
        for (AccessibleNode? node = bridge.FindNext(null, AccessibleNavigation.Element); node is not null; node = bridge.FindNext(node, AccessibleNavigation.Element))
        {
            nodes.Add(node);
        }

        Assert.Equal(17, nodes.Count);
        Assert.Equal(nodes.Count, peers.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            Assert.Equal(nodes[i].Name is { Length: > 0 } name ? name : nodes[i].Children.Count == 0 ? nodes[i].Text : string.Empty, peers[i].GetName());
        }

        Assert.Equal(
            ["Broadside", "A paragraph with a link.", "1. First item", "1.", "First item", "2. Second item", "2.", "Second item", "Name", "Value", "Alpha", "1"],
            peers.Select(p => p.GetName()).Where(n => !string.IsNullOrEmpty(n)).ToList());

        // Structure: a region landmark, a level-1 heading, a list, a table with column headers.
        AutomationPeer section = page.GetChildren()[0];
        Assert.Equal(AutomationLandmarkType.Region, section.GetLandmarkType());
        AutomationPeer heading = section.GetChildren()[0];
        Assert.Equal(1, heading.GetHeadingLevel());
        Assert.Equal("heading", heading.GetLocalizedControlType());
        Assert.Contains(peers, p => p.GetAutomationControlType() == AutomationControlType.List);
        Assert.Contains(peers, p => p.GetAutomationControlType() == AutomationControlType.Table);
        Assert.Equal(["Name", "Value"], peers.Where(p => p.GetAutomationControlType() == AutomationControlType.HeaderItem).Select(p => p.GetName()));

        // Bounds are on screen, inside the viewer, where the bridge places the nodes.
        Rect bounds = heading.GetBoundingRectangle();
        Assert.True(bounds.Width > 0 && bounds.Height > 0 && new Rect(viewer.Viewer.Bounds.Size).Contains(bounds), $"{bounds}");

        // Focusing a node through automation moves the bridge's focus.
        heading.SetFocus();
        Assert.True(heading.HasKeyboardFocus());
        Assert.Equal("Broadside", bridge.Focus!.Name);

        static void Flatten(AutomationPeer peer, List<AutomationPeer> into)
        {
            foreach (AutomationPeer child in peer.GetChildren())
            {
                into.Add(child);
                Flatten(child, into);
            }
        }
    });

    [Fact]
    public Task Pages_are_named_by_their_page_labels() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("page-labels.pdf"));
        Assert.NotNull(viewer.Viewer.PageLabels);
        AutomationPeer document = ControlAutomationPeer.CreatePeerForElement(viewer.Viewer);
        Assert.Equal(
            ["Page i", "Page ii", "Page iii", "Page 1", "Page 2", "Page IV", "Page V", "Page Z", "Page AA", "Page A-zz", "Page A-aaa", "Page Cover"],
            document.GetChildren().Select(page => page.GetName()));
    });

    [Fact]
    public Task Going_to_a_destination_shows_its_page_at_the_view_it_asks_for() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.SampleDocument("textbook.pdf"));
        viewer.Viewer.GoTo(new ViewTarget(5, PdfDestinationView.Fit, null, null, null, null, null));
        await viewer.SettleAsync();
        Assert.Equal(5, viewer.Viewer.State.PageIndex);
        Viewport viewport = viewer.Viewport;
        Assert.Equal(viewer.Layout.GetZoom(ZoomMode.FitPage, viewport.Width, viewport.Height, 5, 96.0 / 72), viewport.Zoom, 6);
        PinnedSubsetTests.AssertTilesOnScreen(viewer, 5);
    });

    [Fact]
    public Task Pages_a_screen_reader_asks_for_load_and_announce_their_children() => Ui.RunAsync(async () =>
    {
        using ViewerHarness viewer = await ViewerHarness.OpenAsync(ViewerHarness.CorpusFile("text-tagged-spanning.pdf"), 600, 300);
        AutomationPeer document = ControlAutomationPeer.CreatePeerForElement(viewer.Viewer);
        IReadOnlyList<AutomationPeer> pages = document.GetChildren();
        Assert.True(pages.Count >= 2);
        AutomationPeer last = pages[^1];
        bool changed = false;
        last.ChildrenChanged += (_, _) => changed = true;
        _ = last.GetChildren();
        await viewer.SettleAsync();
        Assert.True(changed);
        Assert.NotEmpty(last.GetChildren());
    });

    private static Point FindText(ViewerHarness viewer)
    {
        Assert.True(viewer.Viewer.Selection!.TryGetPage(0, out SelectableText? text));
        PdfRectangle crop = viewer.Layout.GetPageMetrics(0).CropBox;
        for (double y = crop.Top - 20; y > crop.Bottom; y -= 2)
        {
            for (double x = crop.Left + 20; x < crop.Right - 120; x += 2)
            {
                // A point well inside a glyph (not on its edge, which a round trip through the view's units may miss) with text to its right.
                if (text.GetGlyphAt(new PathPoint(x, y)) >= 0 && text.GetGlyphAt(new PathPoint(x + 3, y - 3)) >= 0 && text.GetGlyphAt(new PathPoint(x + 23, y - 3)) >= 0)
                {
                    return viewer.ToView(0, x + 3, y - 3);
                }
            }
        }

        throw new InvalidOperationException("No text on page 1.");
    }

    private static PagePoint PageAt(ViewerHarness viewer, Point view)
    {
        Assert.True(viewer.Layout.TryGetPageAt(viewer.Viewport, view.X * viewer.Scaling, view.Y * viewer.Scaling, out PagePoint point));
        return point;
    }

    private static AnnotationTarget FindField<T>(AnnotationLayer layer)
        where T : PdfTerminalField
    {
        for (int i = 0; i < layer.GetCount(0); i++)
        {
            var target = new AnnotationTarget(0, i);
            if (layer.GetAnnotation(target) is PdfWidgetAnnotation { Field: T { IsReadOnly: false } })
            {
                return target;
            }
        }

        throw new InvalidOperationException("no such field");
    }

    private static async Task WaitForAsync(ViewerHarness viewer, Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromMinutes(1), "timed out");
            viewer.Tick();
            await Task.Delay(2);
        }

        await viewer.SettleAsync();
    }
}
