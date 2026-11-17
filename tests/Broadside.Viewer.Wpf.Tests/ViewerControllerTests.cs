using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Wpf.Tests;

/// <summary>The WPF shell's pointer, wheel, key, search and dark-mode mapping onto the viewer core, through its framework-neutral controller.</summary>
public sealed class ViewerControllerTests
{
    [Fact]
    public void A_copy_still_loading_its_pages_when_the_document_is_replaced_or_the_view_closes_copies_nothing_and_does_not_throw() => UiThread.Run(async () =>
    {
        // Select all of a long document, then Copy: its pages load one by one. Opening another document, or closing the view, stops
        // the loads; the copy has nothing to copy and returns null rather than throwing into the host's click handler.
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
    public void The_sample_textbook_opens_at_the_top_of_page_one_at_fit_width_with_every_visible_tile_drawn() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        Viewport viewport = shell.Viewport;
        Assert.Equal(0, viewport.X);
        Assert.Equal(0, viewport.Y);
        Assert.Equal(shell.Layout.GetZoom(ZoomMode.FitWidth, viewport.Width, viewport.Height, 0, shell.Ratio * 96 / 72), viewport.Zoom, 9);
        Assert.Equal(new PdfViewState(0, 8, viewport.Zoom / (shell.Ratio * 96 / 72), false, false), shell.Controller.State);

        // The frame is in device pixels: paper for each visible page, then its tiles, which cover the page and show black text on white.
        ViewerFrame frame = shell.Frame;
        Assert.Equal(400 * shell.Ratio, frame.Width);
        Assert.False(frame.IsDark);
        Assert.True(shell.Count(ViewerFrame.ItemKind.Paper) >= 1);
        double white = 0;
        double dark = 0;
        int tiles = 0;
        foreach (ViewerFrame.Item item in frame.Items)
        {
            if (item.Kind == ViewerFrame.ItemKind.Tile && item.Image is TileCopy tile)
            {
                tiles++;
                Assert.True(tile.Width > 0 && item.Destination.Width > 0);
                (double w, double d) = tile.Tones();
                white += w;
                dark += d;
            }
        }

        Assert.True(tiles > 0);
        Assert.True(dark > 0.002 * tiles, $"dark share {dark / tiles}");
        Assert.True(white > dark * 3, $"white {white}, dark {dark}");
        Assert.DoesNotContain(shell.Events, e => e.Kind == TileEventKind.Failed);
    });

    [Fact]
    public void A_mouse_drag_over_text_selects_it_and_a_touch_drag_pans() => UiThread.Run(async () =>
    {
        // A view shorter than the page, so there is something to pan.
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"), height: 300);
        (double x, double y) = FindText(shell);
        Viewport before = shell.Viewport;

        shell.Drag(1, PointerKind.Mouse, x, y, x + 60, y);
        Assert.False(shell.Controller.Selection!.IsEmpty);
        Assert.Equal(before, shell.Viewport);
        shell.Controller.Frame();
        Assert.True(shell.Count(ViewerFrame.ItemKind.Selection) > 0);
        Assert.Equal(0, shell.Count(ViewerFrame.ItemKind.Handle)); // a mouse drags the selection itself: no touch handles

        shell.Drag(2, PointerKind.Touch, x + 30, y + 40, x + 30, y - 60);
        Assert.True(shell.Controller.Selection.IsEmpty);
        await shell.SettleAsync();
        Assert.True(shell.Viewport.Y > before.Y, $"{before} -> {shell.Viewport}, text at ({x}, {y}), scroll {shell.Controller.ScrollPosition()}");
    });

    [Fact]
    public void A_mouse_release_away_from_the_press_selects_up_to_it_when_no_move_arrived_between() => UiThread.Run(async () =>
    {
        // A fast drag can end before a move reaches the view at the release point: the release still ends the selection there.
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        (double x, double y) = FindText(shell);
        Viewport before = shell.Viewport;

        shell.Controller.PointerDown(1, PointerKind.Mouse, x, y, 1, InputModifiers.None);
        Assert.True(shell.Controller.Selection!.IsEmpty);
        shell.Controller.PointerUp(1, x + 60, y);
        Assert.False(shell.Controller.Selection.IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(await shell.Controller.GetSelectedTextAsync()));
        Assert.Equal(before, shell.Viewport);
    });

    [Fact]
    public void A_long_press_on_text_selects_a_word_and_its_handle_extends_the_selection() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        (double x, double y) = FindText(shell);
        Viewport before = shell.Viewport;

        shell.Controller.PointerDown(1, PointerKind.Touch, x, y, 1, InputModifiers.None);
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
        Assert.Equal(2, shell.Count(ViewerFrame.ItemKind.Handle));
        var quads = new Quad[16];
        int count = selection.GetQuads(0, quads);
        (double hx, double hy) = shell.ToView(0, quads[count - 1].BottomRight.X, quads[count - 1].BottomRight.Y);
        shell.Drag(2, PointerKind.Touch, hx, hy + 6, hx + 20, hy + 60);
        Assert.True(selection.End.Offset > end.Offset, $"{selection.End} after {end}");
        Assert.Equal(before, shell.Viewport);
    });

    [Fact]
    public void A_double_click_selects_a_word_and_shift_click_extends_the_selection() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        (double x, double y) = FindText(shell);
        shell.Controller.PointerDown(1, PointerKind.Mouse, x, y, 2, InputModifiers.None);
        shell.Controller.PointerUp(1, x, y);
        TextSelection selection = shell.Controller.Selection!;
        Assert.Equal(SelectionGranularity.Word, selection.Granularity);
        TextPosition end = selection.End;

        // A shift-click on a glyph further on in the text extends the selection to it.
        (double laterX, double laterY) = FindGlyphAfter(shell, end.Offset + 3);
        shell.Controller.PointerDown(1, PointerKind.Mouse, laterX, laterY, 1, InputModifiers.Shift);
        shell.Controller.PointerUp(1, laterX, laterY);
        Assert.True(selection.End.Offset > end.Offset, $"{selection.Start}-{selection.End} after {end}");
        await shell.SettleAsync();
    });

    [Fact]
    public void Keys_and_the_wheel_map_onto_the_input_model() => UiThread.Run(async () =>
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

        // One notch down (WPF's -120) is three lines.
        double y = shell.Viewport.Y;
        shell.Controller.Wheel(0, 120 / 40.0, WheelDeltaMode.Line, InputModifiers.None, 100, 100);
        await shell.SettleAsync();
        Assert.Equal(y + (3 * InputModel.LineScroll * shell.Ratio), shell.Viewport.Y, 3);

        double zoom = shell.Viewport.Zoom;
        Assert.True(shell.Controller.KeyDown("+", InputModifiers.None));
        await shell.SettleAsync();
        Assert.Equal(zoom * 1.25, shell.Viewport.Zoom, 6);

        // Ctrl+wheel zooms around the pointer.
        zoom = shell.Viewport.Zoom;
        shell.Controller.Wheel(0, -120 / 40.0, WheelDeltaMode.Line, InputModifiers.Control, 100, 100);
        await shell.SettleAsync();
        Assert.True(shell.Viewport.Zoom > zoom);

        Assert.False(shell.Controller.KeyDown("F13", InputModifiers.None));
    });

    [Fact]
    public void Scrolling_to_a_fraction_reports_the_same_fraction_back() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(ManyPagesDocument.Create(6));
        (double h, double v, double hView, double vView) = shell.Controller.ScrollPosition();
        Assert.Equal(-1, h); // fit-width: nothing to scroll sideways
        Assert.Equal(0, v);
        Assert.Equal(1, hView, 6);
        Assert.True(vView is > 0 and < 1);

        shell.Controller.ScrollToFraction(-1, 0.5);
        await shell.SettleAsync();
        Assert.Equal(0.5, shell.Controller.ScrollPosition().Vertical, 6);
        Assert.InRange(shell.Controller.Input!.CurrentPage, 2, 3);

        shell.Controller.ScrollToFraction(-1, 1);
        await shell.SettleAsync();
        Assert.Equal(1, shell.Controller.ScrollPosition().Vertical, 6);
        Assert.Equal(5, shell.Controller.Input.CurrentPage);
    });

    [Fact]
    public void Search_highlights_matches_over_the_tiles_and_find_next_scrolls_to_the_next_page_with_one() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        DocumentSearch search = shell.Controller.Search!;
        int presented = shell.Controller.Surface.PresentedCount;

        // "the" is on every page of the textbook, several times.
        search.Search("the");
        await shell.SettleAsync();
        while (!search.IsSearchComplete)
        {
            await Task.Delay(5);
            await shell.SettleAsync();
        }

        Assert.True(search.MatchCount >= 8, $"{search.MatchCount} matches");
        Assert.True(shell.Count(ViewerFrame.ItemKind.SearchMatch) > 0);
        Assert.True(shell.Count(ViewerFrame.ItemKind.CurrentSearchMatch) > 0);
        Assert.Equal(presented, shell.Controller.Surface.PresentedCount); // highlighting draws no page again

        // F3 steps through the matches and scrolls each into view; Shift+F3 goes back.
        SearchMatch first = search.Current!;
        int page = first.PageIndex;
        while (search.Current!.PageIndex == page)
        {
            Assert.True(shell.Controller.KeyDown("F3", InputModifiers.None));
        }

        await shell.SettleAsync();
        SearchMatch next = search.Current;
        Assert.True(next.PageIndex > page);
        LayoutRectangle box = shell.Layout.PageToViewport(shell.Viewport, next.PageIndex, next.Bounds);
        Assert.True(box.Y >= 0 && box.Bottom <= shell.Viewport.Height, $"{box} in {shell.Viewport}");
        Assert.True(shell.Controller.KeyDown("F3", InputModifiers.Shift));
        Assert.NotSame(next, search.Current);

        search.Clear();
        await shell.SettleAsync();
        Assert.Equal(0, shell.Count(ViewerFrame.ItemKind.SearchMatch));
    });

    [Fact]
    public void Dark_mode_draws_every_tile_again_from_the_cached_display_lists_with_dark_paper() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        long misses = shell.Controller.Session!.Document.CacheStatistics.DisplayLists.Misses;
        int presented = shell.Controller.Surface.PresentedCount;

        shell.Controller.SetColorFilter(ColorFilter.DarkMode);
        Assert.Equal(ColorFilter.DarkMode, shell.Controller.Session.ColorFilter);
        await shell.SettleAsync();
        Assert.True(shell.Frame.IsDark);
        Assert.True(shell.Controller.Surface.PresentedCount > presented);
        Assert.Equal(misses, shell.Controller.Session.Document.CacheStatistics.DisplayLists.Misses);

        double dark = 0;
        double white = 0;
        foreach (ViewerFrame.Item item in shell.Frame.Items)
        {
            if (item.Kind == ViewerFrame.ItemKind.Tile && item.Image is TileCopy tile)
            {
                (double w, double d) = tile.Tones();
                white += w;
                dark += d;
            }
        }

        Assert.True(dark > white * 3, $"dark {dark}, white {white}: the paper turned black");

        // Null keeps the session's filter; a new document opened in the same view gets the view's filter.
        shell.Controller.SetColorFilter(null);
        Assert.Equal(ColorFilter.DarkMode, shell.Controller.Session.ColorFilter);
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
            shell.Controller.PointerDown(1, PointerKind.Mouse, x, y, 1, InputModifiers.None);
            shell.Controller.PointerUp(1, x, y);
            await shell.SettleAsync();
            Assert.Single(shell.Actions);
            Assert.Equal(5, shell.Controller.Input!.CurrentPage);
        }
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

        Assert.Equal(await FormFillingScenario.ExpectedAsync(TestContext.Current.CancellationToken), FormFillingScenario.Describe(form));
        await shell.SettleAsync();
    });

    [Fact]
    public void Tab_focuses_form_fields_and_opens_the_editor_whose_commit_redraws_only_overlay_tiles() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("acroform-fields.pdf"));
        AnnotationLayer layer = shell.Controller.Annotations!;
        while (layer.FocusedField is not PdfTextField)
        {
            Assert.True(shell.Controller.KeyDown("Tab", InputModifiers.None));
        }

        FieldEditor editor = Assert.IsType<FieldEditor>(shell.Editors[^1]);
        Assert.Equal(FieldEditorKind.Text, editor.Kind);
        shell.Events.Clear();
        Assert.True(shell.Controller.CommitFieldText("Ada Lovelace"));
        await shell.SettleAsync();
        Assert.Contains(shell.Events, e => e.Kind == TileEventKind.Presented);
        Assert.All(
            shell.Events.Where(e => e.Kind is TileEventKind.Started or TileEventKind.Presented or TileEventKind.Evicted),
            e => Assert.Equal(DisplayLayers.None, e.Tile.Layers & DisplayLayers.Content));
        Assert.Equal("Ada Lovelace", ((PdfTextField)((PdfWidgetAnnotation)layer.GetAnnotation(editor.Target)).Field!).Value);

        shell.Controller.EndFieldEdit();
        Assert.Null(shell.Editors[^1]);
        Assert.True(shell.Controller.KeyDown("Escape", InputModifiers.None));
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
    public void Closing_the_document_empties_the_frame_and_a_tile_a_closed_session_presents_late_is_dropped() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("layout-three-column.pdf"));
        Assert.True(shell.Count(ViewerFrame.ItemKind.Tile) > 0);
        IRenderSurface closed = shell.Controller.Surface.ForCurrentSession();
        shell.Controller.CloseDocument();
        shell.Controller.Frame();
        Assert.Equal(0, shell.Frame.Count);
        Assert.Equal(0, shell.Controller.Surface.Count);

        var tile = new Tile(0, 1, new PixelRectangle(0, 0, 4, 4));
        using var pixels = new Bitmap(4, 4);
        closed.Present(tile, pixels); // late: the closed session's worker
        shell.Controller.Surface.Drain();
        Assert.False(shell.Controller.Surface.TryGetImage(tile, out _));
    });

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

    private static (double X, double Y) FindGlyphAfter(Shell shell, int glyph)
    {
        Assert.True(shell.Controller.Selection!.TryGetPage(0, out SelectableText? text));
        PdfRectangle crop = shell.Layout.GetPageMetrics(0).CropBox;
        for (double y = crop.Top - 20; y > crop.Bottom; y -= 4)
        {
            for (double x = crop.Left + 20; x < crop.Right - 20; x += 4)
            {
                if (text.GetGlyphAt(new PathPoint(x, y)) >= glyph)
                {
                    return shell.ToView(0, x, y);
                }
            }
        }

        throw new InvalidOperationException($"No glyph from {glyph} on page 1.");
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
}
