using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.WinUI.Tests;

/// <summary>The WinUI shell's pointer, key, search, dark-mode and frame mapping onto the viewer core, through the controller PdfView hosts.</summary>
public sealed class ViewerControllerTests
{
    [Fact]
    public void The_sample_textbook_opens_at_the_top_of_page_one_at_fit_width_and_draws_it() => UiThread.Run(async () =>
    {
        // The size arrives after the document (as WinUI lays out after the source is set): the first real size still shows page 1's top.
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"), sizeFirst: false);
        Viewport viewport = shell.Viewport;
        Assert.Equal(0, viewport.X);
        Assert.Equal(0, viewport.Y);
        Assert.Equal(shell.Layout.GetZoom(ZoomMode.FitWidth, viewport.Width, viewport.Height, 0, shell.Ratio * 96 / 72), viewport.Zoom, 9);
        Assert.Equal(new PdfViewState(0, 8, viewport.Zoom / (shell.Ratio * 96 / 72), false, false), shell.Controller.State);

        Shell.Pixels frame = shell.Draw();
        LayoutRectangle page = shell.Layout.PageToViewport(viewport, 0, shell.Layout.GetPageMetrics(0).CropBox);
        Assert.Equal(Shell.Background, frame[1, 1]);
        (int dark, int white) = CountInk(frame, page);

        // Page 1's heading, objectives and text: plenty of black text on white paper, with every visible tile drawn.
        Assert.True(dark > 300, $"dark samples: {dark}");
        Assert.True(white > dark * 3, $"white samples: {white}");
        Assert.DoesNotContain(shell.Events, e => e.Kind == TileEventKind.Failed);
    });

    [Fact]
    public void Pinch_and_double_tap_traces_are_identical_at_any_pixel_ratio() => UiThread.Run(async () =>
    {
        byte[] document = ManyPagesDocument.Create(6);
        double[][] traces = new double[3][];
        double[] ratios = [1, 1.5, 2.25];
        for (int i = 0; i < ratios.Length; i++)
        {
            using Shell shell = await Shell.OpenAsync(document, 390, 844, ratios[i]);
            var trace = new List<double>();
            void Record()
            {
                Viewport v = shell.Viewport;
                trace.AddRange([v.X / shell.Ratio, v.Y / shell.Ratio, v.Zoom / shell.Ratio]);
            }

            // Two-finger pinch out around (200, 300): the second finger turns the pan into a pinch (the input model's own rule).
            shell.Press(1, PointerKind.Touch, 150, 300);
            shell.Press(2, PointerKind.Touch, 250, 300);
            for (int step = 1; step <= 10; step++)
            {
                shell.Clock.Advance(TimeSpan.FromMilliseconds(16));
                shell.Controller.PointerMoved(1, PointerKind.Touch, 150 - (step * 8), 300, inContact: true);
                shell.Controller.PointerMoved(2, PointerKind.Touch, 250 + (step * 8), 300, inContact: true);
                Record();
            }

            shell.Controller.PointerUp(2, 330, 300);
            shell.Controller.PointerUp(1, 70, 300);
            await shell.SettleAsync();
            Record();

            // Double-tap: at fit-width it zooms to twice fit-width anchored at the tap; again, back to fit-width.
            shell.Controller.ZoomTo(ZoomMode.FitWidth);
            await shell.SettleAsync();
            Record();
            shell.Tap(3, 120, 400);
            shell.Clock.Advance(TimeSpan.FromMilliseconds(100));
            shell.Tap(3, 120, 400);
            await shell.SettleAsync();
            Record();
            shell.Tap(4, 120, 400);
            shell.Clock.Advance(TimeSpan.FromMilliseconds(100));
            shell.Tap(4, 120, 400);
            await shell.SettleAsync();
            Record();
            traces[i] = [.. trace];
        }

        Assert.Equal(traces[0].Length, traces[1].Length);
        for (int k = 0; k < traces[0].Length; k++)
        {
            Assert.Equal(traces[0][k], traces[1][k], 6);
            Assert.Equal(traces[0][k], traces[2][k], 6);
        }

        int n = traces[0].Length;
        double fitWidth = traces[0][n - 7];
        Assert.True(traces[0][29] > traces[0][2] * 1.5);
        Assert.Equal(fitWidth * 2, traces[0][n - 4], 6);
        Assert.Equal(fitWidth, traces[0][n - 1], 6);
    });

    [Fact]
    public void A_mouse_drag_over_text_selects_it_and_a_touch_drag_pans() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        (double x, double y) = FindText(shell);
        Viewport before = shell.Viewport;

        shell.Drag(1, PointerKind.Mouse, x, y, x + 60, y);
        Assert.False(shell.Controller.Selection!.IsEmpty);
        Assert.Equal(before, shell.Viewport);
        shell.Controller.Frame();
        Assert.Contains(shell.Controller.LatestFrame.Items.ToArray(), item => item.Kind == ViewerFrame.ItemKind.Selection);
        Assert.DoesNotContain(shell.Controller.LatestFrame.Items.ToArray(), item => item.Kind == ViewerFrame.ItemKind.Handle);

        shell.Drag(2, PointerKind.Touch, x + 30, y + 40, x + 30, y - 60);
        Assert.True(shell.Controller.Selection.IsEmpty);
        await shell.SettleAsync();
        Assert.True(shell.Viewport.Y > before.Y);
    });

    [Fact]
    public void A_mouse_release_away_from_the_press_selects_up_to_it_when_no_move_arrived_between() => UiThread.Run(async () =>
    {
        // Windows does not always deliver a move before the release (input that only repositions the cursor, a move folded into the
        // release): the release point still ends the selection.
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        (double x, double y) = FindText(shell);
        Viewport before = shell.Viewport;

        shell.Press(1, PointerKind.Mouse, x, y);
        Assert.True(shell.Controller.Selection!.IsEmpty);
        shell.Controller.PointerUp(1, x + 60, y);
        Assert.False(shell.Controller.Selection.IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(await shell.Controller.GetSelectedTextAsync()));
        Assert.Equal(before, shell.Viewport);
    });

    [Fact]
    public void A_copy_still_loading_its_pages_when_the_document_is_replaced_or_the_view_closes_copies_nothing_and_does_not_throw() => UiThread.Run(async () =>
    {
        // Select all of a long document, then Copy: its pages load one by one. Opening another document, or closing the view (the
        // sample's window closing), stops the loads; the copy has nothing to copy and returns null rather than throwing into the
        // host's click handler.
        Shell shell = await Shell.OpenAsync(ManyPagesDocument.CreateWithText(40));
        Task<string?> copy;
        try
        {
            shell.Controller.Selection!.SelectAll();
            copy = shell.Controller.GetSelectedTextAsync();
            Assert.False(copy.IsCompleted);
            await shell.Controller.OpenAsync(_ => Task.FromResult(DocumentSession.Open(ManyPagesDocument.CreateWithText(40), Shell.Options())), ownsSession: true, null);
            Assert.Null(await copy);

            await shell.SettleAsync();
            shell.Controller.Selection!.SelectAll();
            copy = shell.Controller.GetSelectedTextAsync();
            Assert.False(copy.IsCompleted);
        }
        finally
        {
            shell.Dispose();
        }

        Assert.Null(await copy);
    });

    [Fact]
    public void A_double_click_selects_a_word_and_a_right_click_is_left_to_the_host() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        (double x, double y) = FindText(shell);
        Assert.False(shell.Controller.PointerPressed(1, PointerKind.Mouse, x, y, InputModifiers.None, isPrimaryButton: false));
        Assert.True(shell.Controller.Selection!.IsEmpty);

        shell.Press(1, PointerKind.Mouse, x, y);
        shell.Controller.PointerUp(1, x, y);
        shell.Clock.Advance(TimeSpan.FromMilliseconds(120));
        shell.Press(1, PointerKind.Mouse, x, y);
        shell.Controller.PointerUp(1, x, y);
        Assert.Equal(SelectionGranularity.Word, shell.Controller.Selection.Granularity);
        Assert.False(shell.Controller.Selection.IsEmpty);
    });

    [Fact]
    public void A_long_press_on_text_selects_a_word_and_its_handle_extends_the_selection() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        (double x, double y) = FindText(shell);
        Viewport before = shell.Viewport;

        shell.Press(1, PointerKind.Touch, x, y);
        shell.Clock.Advance(ViewerController.LongPress + TimeSpan.FromMilliseconds(20));
        shell.Controller.Frame();
        shell.Controller.PointerUp(1, x, y);
        TextSelection selection = shell.Controller.Selection!;
        Assert.False(selection.IsEmpty);
        Assert.Equal(SelectionGranularity.Word, selection.Granularity);
        Assert.Equal(before, shell.Viewport);
        TextPosition end = selection.End;

        // The end handle sits under the last quad's bottom-right corner; dragging it down a few lines extends the selection.
        await shell.SettleAsync();
        Assert.Contains(shell.Controller.LatestFrame.Items.ToArray(), item => item.Kind == ViewerFrame.ItemKind.Handle);
        var quads = new Quad[16];
        int count = selection.GetQuads(0, quads);
        (double hx, double hy) = shell.ToView(0, quads[count - 1].BottomRight.X, quads[count - 1].BottomRight.Y);
        shell.Drag(2, PointerKind.Touch, hx, hy + 6, hx + 20, hy + 60);
        Assert.True(selection.End.Offset > end.Offset, $"{selection.End} after {end}");
        Assert.Equal(before, shell.Viewport);
    });

    [Fact]
    public void A_uri_link_is_raised_never_followed_and_an_internal_link_navigates() => UiThread.Run(async () =>
    {
        using (Shell shell = await Shell.OpenAsync(Shell.CorpusFile("annotations-link.pdf")))
        {
            Viewport before = shell.Viewport;
            (double x, double y) = shell.ToView(0, 100, 705);
            shell.Tap(1, x, y);
            ViewerActionEventArgs action = Assert.Single(shell.Actions);
            Assert.Equal(new Uri("https://example.com/"), action.Uri);
            Assert.Equal(before, shell.Viewport);
        }

        using (Shell shell = await Shell.OpenAsync(ManyPagesDocument.Create(8)))
        {
            AnnotationLayer layer = shell.Controller.Annotations!;
            PdfPage target = shell.Controller.Session!.Document.Pages[5];
            Assert.NotNull(layer.Add(0, page => page.AddLinkAnnotation(new PdfRectangle(72, 600, 300, 640), target)));
            await shell.SettleAsync();
            (double x, double y) = shell.ToView(0, 150, 620);
            shell.Tap(1, x, y);
            await shell.SettleAsync();
            Assert.Single(shell.Actions);
            Assert.Equal(5, shell.Controller.Input!.CurrentPage);
        }
    });

    [Fact]
    public void A_form_edit_redraws_only_overlay_tiles_and_keeps_the_old_overlay_until_then() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("acroform-fields.pdf"));
        AnnotationLayer layer = shell.Controller.Annotations!;
        AnnotationTarget field = FindField<PdfTextField>(layer);
        LayoutRectangle box = layer.GetScreenRectangle(field, shell.Layout, shell.Viewport);
        shell.Tap(1, (box.X + (box.Width / 2)) / shell.Ratio, (box.Y + (box.Height / 2)) / shell.Ratio);
        FieldEditor editor = Assert.IsType<FieldEditor>(shell.Editors[^1]);
        Assert.Equal(FieldEditorKind.Text, editor.Kind);
        Assert.Equal(field, editor.Target);

        int overlaysBefore = OverlayItems(shell);
        Assert.True(overlaysBefore > 0);
        shell.Events.Clear();
        Assert.True(shell.Controller.CommitFieldText("Ada Lovelace"));

        // The evicted overlay tiles stay on screen until their replacements arrive: the composed frame never loses them.
        shell.Controller.Frame();
        Assert.True(OverlayItems(shell) >= overlaysBefore);
        await shell.SettleAsync();

        Assert.Contains(shell.Events, e => e.Kind == TileEventKind.Presented);
        Assert.All(
            shell.Events.Where(e => e.Kind is TileEventKind.Started or TileEventKind.Presented or TileEventKind.Evicted),
            e => Assert.Equal(DisplayLayers.None, e.Tile.Layers & DisplayLayers.Content));
        var widget = (PdfWidgetAnnotation)layer.GetAnnotation(field);
        Assert.Equal("Ada Lovelace", ((PdfTextField)widget.Field!).Value);
    });

    [Fact]
    public void A_resize_keeps_fit_width_and_the_scaled_tiles_on_screen() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("newsletter.pdf"));
        Assert.True(TileItems(shell) > 0);
        shell.Events.Clear();

        shell.Controller.Resize(520, 600, shell.Ratio);
        shell.Controller.Frame();
        Assert.DoesNotContain(shell.Events, e => e.Kind == TileEventKind.Evicted);
        Assert.True(TileItems(shell) > 0, "the frame after a resize shows the tiles it had, scaled");

        await shell.SettleAsync();
        Viewport v = shell.Viewport;
        Assert.Equal(shell.Layout.GetZoom(ZoomMode.FitWidth, v.Width, v.Height, 0, shell.Ratio * 96 / 72), v.Zoom, 6);
    });

    [Fact]
    public void Keys_wheel_notches_and_magnify_map_onto_the_input_model() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(ManyPagesDocument.Create(6));
        Assert.True(shell.Controller.KeyDown("PageDown", InputModifiers.None));
        await shell.SettleAsync();
        Assert.True(shell.Viewport.Y > 0);

        Assert.True(shell.Controller.KeyDown("End", InputModifiers.None));
        await shell.SettleAsync();
        Assert.Equal(5, shell.Controller.Input!.CurrentPage);

        Assert.True(shell.Controller.KeyDown("Home", InputModifiers.None));
        await shell.SettleAsync();
        double y = shell.Viewport.Y;

        // One notch towards the user (WinUI's MouseWheelDelta -120) scrolls three lines down.
        shell.Controller.MouseWheel(-120, horizontal: false, InputModifiers.None, 100, 100);
        await shell.SettleAsync();
        Assert.Equal(y + (3 * InputModel.LineScroll * shell.Ratio), shell.Viewport.Y, 3);

        double zoom = shell.Viewport.Zoom;
        Assert.True(shell.Controller.KeyDown("+", InputModifiers.None));
        await shell.SettleAsync();
        Assert.Equal(zoom * 1.25, shell.Viewport.Zoom, 6);

        zoom = shell.Viewport.Zoom;
        shell.Controller.Magnify(1.5, 100, 100);
        shell.Clock.Advance(TimeSpan.FromMilliseconds(400));
        await shell.SettleAsync();
        Assert.Equal(zoom * 1.5, shell.Viewport.Zoom, 6);

        Assert.False(shell.Controller.KeyDown("F13", InputModifiers.None));
    });

    [Fact]
    public void Select_all_and_copy_put_the_text_on_the_hosts_clipboard() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        Assert.False(shell.Controller.KeyDown("c", InputModifiers.Control));
        Assert.True(shell.Controller.KeyDown("a", InputModifiers.Control));
        Assert.False(shell.Controller.Selection!.IsEmpty);
        Assert.True(shell.Controller.KeyDown("c", InputModifiers.Control));
        for (int i = 0; i < 500 && shell.Copied.Count == 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(await shell.Controller.Selection.GetTextAsync(), Assert.Single(shell.Copied));
        Assert.NotEmpty(shell.Copied[0]);
    });

    [Fact]
    public void Tab_focuses_form_fields_and_opens_the_editor_and_escape_closes_it() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("acroform-fields.pdf"));
        AnnotationLayer layer = shell.Controller.Annotations!;
        while (layer.FocusedField is not PdfTextField)
        {
            Assert.True(shell.Controller.KeyDown("Tab", InputModifiers.None));
        }

        Assert.Equal(FieldEditorKind.Text, Assert.IsType<FieldEditor>(shell.Editors[^1]).Kind);
        Assert.True(shell.Controller.KeyDown("Escape", InputModifiers.None));
        Assert.Null(shell.Editors[^1]);
        await shell.SettleAsync();
    });

    [Fact]
    public void The_shared_field_set_fills_through_the_editor_and_keys_as_the_form_api_does() => UiThread.Run(async () =>
    {
        // #543: the editor over a widget is built from the core form filling and only feeds it input; the form filling cuts the
        // text at MaxLen and per comb, refuses the read-only field and writes each field once, as in every other shell.
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile(FormFillingScenario.FileName));
        ViewerController controller = shell.Controller;
        AnnotationLayer layer = controller.Annotations!;
        PdfAcroForm form = layer.Session.Document.AcroForm!;
        Assert.Same(layer, controller.Forms!.Layer);
        FieldEditor? Open(string name, int widget = 0)
        {
            AnnotationTarget target = FormFillingScenario.FindWidget(
                form, name, Enumerable.Range(0, layer.GetCount(0)).Select(i => (new AnnotationTarget(0, i), layer.GetAnnotation(new AnnotationTarget(0, i)))), widget);
            layer.Focus(target);
            Assert.Equal(target, controller.Forms.Target);
            return shell.Editors[^1];
        }

        // Escape abandons an edit: the field keeps its value.
        Assert.Equal((FieldEditorKind.Text, "Ada", 20), Open("name") is { } editor ? (editor.Kind, editor.Text, editor.MaxLength) : default);
        Assert.True(controller.EditorInput("Changed"));
        Assert.True(controller.CancelFieldEdit());
        controller.EndFieldEdit();
        Assert.Null(shell.Editors[^1]);
        Assert.Equal("Ada", ((PdfTextField)form.FindField("name")!).Value);

        Assert.NotNull(Open("name"));
        Assert.True(controller.EditorInput(FormFillingScenario.NameTyped));
        Assert.True(controller.CommitFieldEdit());
        controller.EndFieldEdit();
        Assert.Equal(5, Open("code")!.MaxLength);
        Assert.True(controller.CommitFieldText(FormFillingScenario.CodeTyped));
        controller.EndFieldEdit();

        // A read-only field, a check box and a radio button open no editor; Space toggles the two buttons.
        Assert.Null(Open("ref"));
        Assert.False(controller.EditorInput("R-2"));
        Assert.Null(Open("agree"));
        Assert.True(controller.KeyDown(" ", InputModifiers.None));
        Assert.Null(Open("size", FormFillingScenario.SizeWidget));
        Assert.True(controller.KeyDown(" ", InputModifiers.None));

        FieldEditor fruit = Open("fruit")!;
        Assert.Equal((FieldEditorKind.Choice, 0, false), (fruit.Kind, fruit.SelectedIndex, fruit.IsEditable));
        Assert.Equal(["Apple", "Banana", "Cherry"], fruit.Options);
        Assert.True(controller.CommitFieldChoice(FormFillingScenario.Fruit));
        Assert.Equal(["United States", "Canada", "Mexico"], Open("country")!.Options);
        Assert.True(controller.CommitFieldChoice(FormFillingScenario.Country));
        FieldEditor city = Open("city")!;
        Assert.Equal((FieldEditorKind.Choice, "Lyon", -1, true), (city.Kind, city.Text, city.SelectedIndex, city.IsEditable));
        Assert.True(controller.EditorInput(FormFillingScenario.City));
        controller.EndFieldEdit(); // focus leaving the field commits it
        Assert.Null(shell.Editors[^1]);

        // #560: a multi-select list box opens a multi-select list; each click hands the list's whole selection to the form filling,
        // which keeps the editor open and writes the picks once, when focus leaves the field.
        FieldEditor colors = Open("colors")!;
        Assert.Equal((FieldEditorKind.Choice, true, false), (colors.Kind, colors.IsMultiSelect, colors.IsEditable));
        Assert.Equal(["Red", "Green", "Blue"], colors.Options);
        Assert.Equal([0], colors.SelectedIndices);
        Assert.False(fruit.IsMultiSelect);
        var colorsField = (PdfChoiceField)form.FindField("colors")!;
        foreach (IReadOnlyList<int> picked in FormFillingScenario.ColorsAfterEachToggle)
        {
            Assert.True(controller.EditorSelectedOptions(picked));
            Assert.Equal(picked, controller.Forms.SelectedOptions);
            Assert.Same(colors, shell.Editors[^1]);
            Assert.Equal([0], colorsField.SelectedIndices);
        }

        controller.EndFieldEdit();
        Assert.Null(shell.Editors[^1]);

        Assert.Equal(await FormFillingScenario.ExpectedAsync(TestContext.Current.CancellationToken), FormFillingScenario.Describe(form));
        await shell.SettleAsync();
    });

    [Fact]
    public void The_accessibility_tree_of_visible_pages_reaches_the_host_in_reading_order_with_bounds_in_view_units() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("tagged-structure.pdf"));
        Assert.Contains(0, shell.Controller.AccessiblePages);
        Assert.Contains(shell.AccessibilityChanges, c => c.Kind == AccessibilityChangeKind.ChildrenChanged);
        AccessibilityBridge bridge = shell.Controller.Bridge!;

        // What the automation peers expose: the page's nodes depth first, named by AccessibleText, in the bridge's reading order.
        string[] names = [.. AccessibleText.ReadingOrder(bridge.GetPage(0)).Select(AccessibleText.GetName).Where(name => name.Length > 0)];
        var reference = new AccessibilityBridge(shell.Controller.Session!);
        string[] expected = [.. Flatten(await reference.LoadPageAsync(0)).Select(n => (n.Name ?? n.Text).Trim()).Where(name => name.Length > 0)];
        Assert.Equal(expected, names);
        Assert.Equal("Page 1", AccessibleText.GetName(bridge.GetPage(0)));

        AccessibleNode heading = Find(bridge.GetPage(0), n => n.Role == AccessibleRole.Heading)!;
        Assert.True(heading.Level > 0);
        Assert.True(shell.Controller.TryGetNodeBounds(heading, out LayoutRectangle bounds));
        Assert.True(bridge.TryGetBounds(heading, out LayoutRectangle pixels));
        Assert.Equal(pixels.X / shell.Ratio, bounds.X, 6);
        Assert.Equal(pixels.Width / shell.Ratio, bounds.Width, 6);

        Assert.True(shell.Controller.KeyDown("h", InputModifiers.None));
        Assert.Contains(shell.AccessibilityChanges, c => c.Kind == AccessibilityChangeKind.FocusChanged && c.Node?.Role == AccessibleRole.Heading);

        static IEnumerable<AccessibleNode> Flatten(AccessibleNode node) => node.Children.SelectMany(child => (IEnumerable<AccessibleNode>)[child, .. Flatten(child)]);

        static AccessibleNode? Find(AccessibleNode node, Func<AccessibleNode, bool> match) =>
            match(node) ? node : node.Children.Select(child => Find(child, match)).FirstOrDefault(found => found is not null);
    });

    [Fact]
    public void Search_highlights_matches_over_the_tiles_without_drawing_a_page_and_reveal_scrolls_to_one() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        DocumentSearch search = shell.Controller.Search!;
        shell.Events.Clear();
        search.Search("Kepler");
        await shell.SettleAsync();
        Assert.True(search.IsSearchComplete);
        Assert.True(search.MatchCount > 3, $"matches: {search.MatchCount}");
        ViewerFrame.Item[] items = shell.Controller.LatestFrame.Items.ToArray();
        Assert.Contains(items, item => item.Kind == ViewerFrame.ItemKind.SearchMatch);
        Assert.Contains(items, item => item.Kind == ViewerFrame.ItemKind.CurrentMatch);
        Assert.DoesNotContain(shell.Events, e => e.Kind == TileEventKind.Started);

        // A word only page 6 has: the first match becomes current; revealing it scrolls page 6 into view and outlines it there.
        search.Search("pins", new TextSearchOptions().WithWholeWord());
        await shell.SettleAsync();
        SearchMatch current = Assert.IsType<SearchMatch>(search.Current);
        Assert.All(search.Matches, match => Assert.Equal(5, match.PageIndex));
        shell.Controller.Reveal(current.PageIndex, current.Bounds);
        await shell.SettleAsync();
        Assert.Equal(5, shell.Controller.State.PageIndex);
        ViewerFrame.Item outline = Assert.Single(shell.Controller.LatestFrame.Items.ToArray(), item => item.Kind == ViewerFrame.ItemKind.CurrentMatch && item.A.Y > 0 && item.A.Y < shell.Viewport.Height);
        Assert.InRange(outline.A.X, 0, shell.Viewport.Width);
    });

    [Fact]
    public void Dark_mode_redraws_the_tiles_from_the_cached_display_lists_on_black_paper() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        DocumentSession session = shell.Controller.Session!;
        LayoutRectangle page = shell.Layout.PageToViewport(shell.Viewport, 0, shell.Layout.GetPageMetrics(0).CropBox);
        (int darkBefore, int whiteBefore) = CountInk(shell.Draw(), page);
        long builds = session.Document.CacheStatistics.DisplayLists.Misses;

        shell.Events.Clear();
        shell.Controller.SetColorFilter(ColorFilter.DarkMode);
        Assert.Equal(ColorFilter.DarkMode, session.ColorFilter);
        await shell.SettleAsync();
        Assert.Contains(shell.Events, e => e.Kind == TileEventKind.Presented);
        Assert.Equal(builds, session.Document.CacheStatistics.DisplayLists.Misses);
        Assert.True(shell.Controller.LatestFrame.IsDark);

        // Light text on black paper: the counts of dark and white samples swap.
        Shell.Pixels frame = shell.Draw();
        Assert.Equal(0xFF2B2B2Bu, frame[1, 1]);
        (int dark, int white) = CountInk(frame, page);
        Assert.True(dark > whiteBefore / 2, $"dark samples: {dark}, white before: {whiteBefore}");
        Assert.True(white < darkBefore * 2, $"white samples: {white}, dark before: {darkBefore}");

        // A document opened later keeps the filter; null leaves each session's own.
        shell.Controller.SetColorFilter(null);
        Assert.Equal(ColorFilter.DarkMode, session.ColorFilter);
    });

    [Fact]
    public void Closing_the_document_drops_every_tile_and_shows_an_empty_frame() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("layout-three-column.pdf"));
        Assert.True(TileItems(shell) > 0);
        shell.Controller.CloseDocument();
        shell.Controller.Frame();
        Assert.Equal(0, shell.Controller.Surface.Count);
        Assert.Equal(0, shell.Controller.LatestFrame.Count);
        Assert.Equal(Shell.Background, shell.Draw()[8, 8]);
        Assert.Null(shell.Controller.Search);
    });

    [Fact]
    public void A_tile_a_closed_session_presents_late_never_reaches_the_next_document()
    {
        using var shell = new TileFactory();
        var store = new TileStore(shell, TimeProvider.System);
        IRenderSurface closed = store.ForCurrentSession();
        var tile = new Tile(0, 1, new PixelRectangle(0, 0, 4, 4));
        using var pixels = new Bitmap(4, 4);
        closed.Present(tile, pixels);
        store.Drain();
        Assert.True(store.TryGetImage(tile, out _));

        store.Clear();
        IRenderSurface next = store.ForCurrentSession();
        closed.Present(tile, pixels); // late: the old session's worker
        store.Drain();
        Assert.False(store.TryGetImage(tile, out _));

        next.Present(tile, pixels);
        store.Drain();
        Assert.True(store.TryGetImage(tile, out _));
        store.Clear();
    }

    [Fact]
    public void A_document_opened_while_a_late_page_of_the_previous_one_loads_never_sees_that_page_index() => UiThread.Run(async () =>
    {
        // #358: the outline moved a long novel to page 202; opening the 8-page textbook while a late page's text, links and
        // accessibility nodes still loaded handed that page's index to the textbook's layers ("pageIndex (201) must be less than 8").
        // The host's sessions belong to it: the novel's stays open until the view has moved to the textbook.
        await using DocumentSession novel = DocumentSession.Open(ManyPagesDocument.Create(250), Shell.Options());
        using Shell shell = await Shell.OpenAsync(novel, ownsSession: false);
        shell.Controller.GoToPage(201);

        // Frames until the scroll reaches page 202 and its pages start loading (a busy frame leaves the loads to the next one).
        for (int i = 0; i < 200 && !(shell.Controller.State.PageIndex == 201 && shell.Controller.IsLoadingPages); i++)
        {
            shell.Clock.Advance(TimeSpan.FromMilliseconds(16));
            shell.Controller.Frame();
        }

        Assert.True(shell.Controller.State.PageIndex == 201 && shell.Controller.IsLoadingPages);

#pragma warning disable CA2000 // the controller owns the session (ownsSession: true) and disposes it
        DocumentSession next = DocumentSession.Open(Shell.SampleDocument("textbook.pdf"), Shell.Options());
#pragma warning restore CA2000
        await shell.Controller.OpenAsync(_ => Task.FromResult(next), ownsSession: true, null);
        await shell.SettleAsync();

        Assert.Empty(shell.Errors);
        Assert.Equal(new PdfViewState(0, 8, shell.Controller.State.Zoom, false, false), shell.Controller.State);
        Assert.All(shell.Controller.AccessiblePages, page => Assert.InRange(page, 0, 7));
    });

    private static (int Dark, int White) CountInk(Shell.Pixels frame, LayoutRectangle page)
    {
        int dark = 0;
        int white = 0;
        for (int y = (int)page.Y + 4; y < Math.Min(page.Bottom, frame.Height) - 4; y += 3)
        {
            for (int x = (int)page.X + 4; x < Math.Min(page.Right, frame.Width) - 4; x += 3)
            {
                uint c = frame[x, y];
                uint r = (c >> 16) & 0xFF;
                uint g = (c >> 8) & 0xFF;
                uint b = c & 0xFF;
                dark += r < 96 && g < 96 && b < 96 ? 1 : 0;
                white += c == 0xFFFFFFFFu ? 1 : 0;
            }
        }

        return (dark, white);
    }

    private static int TileItems(Shell shell) => CountItems(shell, _ => true);

    private static int OverlayItems(Shell shell) => CountItems(shell, shell.Controller.Surface.IsOverlay);

    private static int CountItems(Shell shell, Func<ITileImage, bool> match)
    {
        int count = 0;
        foreach (ViewerFrame.Item item in shell.Controller.LatestFrame.Items)
        {
            count += item.Kind == ViewerFrame.ItemKind.Tile && item.Image is { } image && match(image) ? 1 : 0;
        }

        return count;
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

        throw new InvalidOperationException("No such field on page 1.");
    }

    private static (double X, double Y) FindText(Shell shell)
    {
        Assert.True(shell.Controller.Selection!.TryGetPage(0, out SelectableText? text));
        PdfRectangle crop = shell.Layout.GetPageMetrics(0).CropBox;
        for (double y = crop.Top - 20; y > crop.Bottom; y -= 4)
        {
            for (double x = crop.Left + 20; x < crop.Right - 120; x += 4)
            {
                if (text.GetGlyphAt(new PathPoint(x, y)) >= 0 && text.GetGlyphAt(new PathPoint(x + 30, y)) >= 0)
                {
                    return shell.ToView(0, x, y);
                }
            }
        }

        throw new InvalidOperationException("No text on page 1.");
    }

    private sealed class TileFactory : ITileImageFactory, IDisposable
    {
        public ITileImage Create(int width, int height, ReadOnlySpan<byte> bgra, int stride) => new Shell.TestImage(width, height);

        public void Dispose()
        {
        }
    }
}
