using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Uno.Tests;

/// <summary>The Uno shell's pointer, wheel, key and frame mapping onto the viewer core, through the controller both heads share.</summary>
public sealed class ViewerControllerTests
{
    [Fact]
    public void The_sample_textbook_opens_at_the_top_of_page_one_at_fit_width_and_draws_its_text() => UiThread.Run(async () =>
    {
        // The size arrives after the document (as Uno lays out after the source is set): the first real size still shows page 1's top.
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"), sizeFirst: false);
        Viewport viewport = shell.Viewport;
        Assert.Equal(0, viewport.X);
        Assert.Equal(0, viewport.Y);
        Assert.Equal(shell.Layout.GetZoom(ZoomMode.FitWidth, viewport.Width, viewport.Height, 0, shell.Ratio * 96 / 72), viewport.Zoom, 9);
        Assert.Equal(new PdfViewState(0, 8, viewport.Zoom / (shell.Ratio * 96 / 72), false, false), shell.Controller.State);

        Raster frame = shell.Draw();
        LayoutRectangle page = shell.Layout.PageToViewport(viewport, 0, shell.Layout.GetPageMetrics(0).CropBox);
        Assert.Equal(ViewerColors.Background, frame[1, 1]);
        (int dark, int white) = Count(frame, page);

        // Page 1's heading, objectives and text: plenty of black text on white paper, with every visible tile drawn sharp.
        Assert.True(dark > 300, $"dark samples: {dark}");
        Assert.True(white > dark * 3, $"white samples: {white}");
        Assert.DoesNotContain(shell.Events, e => e.Kind == TileEventKind.Failed);
    });

    [Fact]
    public void A_mouse_drag_over_text_selects_it_and_a_touch_drag_pans() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        (double x, double y) = FindText(shell);
        Viewport before = shell.Viewport;
        shell.Drag(1, PointerKind.Mouse, x, y, x + 120, y + 40);
        Assert.False(shell.Controller.Selection!.IsEmpty);
        Assert.Equal(before, shell.Viewport);
        await shell.SettleAsync();
        Assert.Contains(shell.Controller.LatestFrame.Items.ToArray(), i => i.Kind == FrameItemKind.Selection);
        Assert.DoesNotContain(shell.Controller.LatestFrame.Items.ToArray(), i => i.Kind == FrameItemKind.Handle);
        string? text = await shell.Controller.GetSelectedTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(text));

        // A double click selects a word.
        shell.Clock.Advance(TimeSpan.FromSeconds(1));
        shell.Down(2, PointerKind.Mouse, x, y);
        shell.Up(2, x, y);
        shell.Down(2, PointerKind.Mouse, x, y);
        shell.Up(2, x, y);
        Assert.Equal(SelectionGranularity.Word, shell.Controller.Selection.Granularity);

        shell.Drag(3, PointerKind.Touch, x + 30, y + 40, x + 30, y - 60);
        Assert.True(shell.Controller.Selection.IsEmpty);
        await shell.SettleAsync();
        Assert.True(shell.Viewport.Y > before.Y);
    });

    [Fact]
    public void A_long_press_on_text_selects_a_word_and_its_handle_extends_the_selection() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        (double x, double y) = FindText(shell);
        Viewport before = shell.Viewport;

        shell.Down(1, PointerKind.Touch, x, y);
        shell.Clock.Advance(ViewerController.LongPress + TimeSpan.FromMilliseconds(20));
        shell.Controller.Frame();
        shell.Up(1, x, y);
        TextSelection selection = shell.Controller.Selection!;
        Assert.False(selection.IsEmpty);
        Assert.Equal(SelectionGranularity.Word, selection.Granularity);
        Assert.Equal(before, shell.Viewport);
        TextPosition end = selection.End;

        // The end handle sits under the last quad's bottom-right corner; dragging it down a few lines extends the selection.
        await shell.SettleAsync();
        Assert.Contains(shell.Controller.LatestFrame.Items.ToArray(), i => i.Kind == FrameItemKind.Handle);
        var quads = new Quad[16];
        int count = selection.GetQuads(0, quads);
        (double hx, double hy) = shell.ToView(0, quads[count - 1].BottomRight.X, quads[count - 1].BottomRight.Y);
        shell.Drag(2, PointerKind.Touch, hx, hy + 6, hx + 20, hy + 60);
        Assert.True(selection.End.Offset > end.Offset, $"{selection.End} after {end}");
        Assert.Equal(before, shell.Viewport);
    });

    [Fact]
    public void Wheel_ctrl_wheel_keys_and_magnify_scroll_and_zoom_through_the_input_model() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(ManyPagesDocument.Create(6));
        Assert.True(shell.Controller.KeyDown("PageDown", InputModifiers.None));
        await shell.SettleAsync();
        Assert.True(shell.Viewport.Y > 0);

        Assert.True(shell.Controller.KeyDown("End", InputModifiers.None));
        await shell.SettleAsync();
        Assert.Equal(5, shell.Controller.Input!.CurrentPage);
        Assert.Equal(5, shell.Controller.State.PageIndex);

        Assert.True(shell.Controller.KeyDown("Home", InputModifiers.None));
        await shell.SettleAsync();
        double y = shell.Viewport.Y;

        // One wheel notch down is three lines.
        shell.Controller.Wheel(0, 3, WheelDeltaMode.Line, InputModifiers.None, 100, 100);
        await shell.SettleAsync();
        Assert.Equal(y + (3 * InputModel.LineScroll * shell.Ratio), shell.Viewport.Y, 3);

        y = shell.Viewport.Y;
        shell.Controller.ScrollBy(0, 50);
        await shell.SettleAsync();
        Assert.Equal(y + (50 * shell.Ratio), shell.Viewport.Y, 3);

        double zoom = shell.Viewport.Zoom;
        shell.Controller.Wheel(0, -100, WheelDeltaMode.Pixel, InputModifiers.Control, 100, 100);
        shell.Clock.Advance(TimeSpan.FromMilliseconds(400));
        await shell.SettleAsync();
        Assert.Equal(zoom * Math.Exp(100 * 0.002), shell.Viewport.Zoom, 6);

        zoom = shell.Viewport.Zoom;
        Assert.True(shell.Controller.KeyDown("+", InputModifiers.None));
        await shell.SettleAsync();
        Assert.Equal(zoom * 1.25, shell.Viewport.Zoom, 6);

        zoom = shell.Viewport.Zoom;
        shell.Controller.Magnify(1.5, 100, 100);
        shell.Clock.Advance(TimeSpan.FromMilliseconds(400));
        await shell.SettleAsync();
        Assert.Equal(zoom * 1.5, shell.Viewport.Zoom, 6);
        Assert.Equal(shell.Viewport.Zoom / (shell.Ratio * 96 / 72), shell.Controller.State.Zoom, 9);

        Assert.False(shell.Controller.KeyDown("F13", InputModifiers.None));
    });

    [Fact]
    public void A_search_highlights_every_match_and_scrolls_the_first_one_into_view_without_drawing_a_page_again() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        DocumentSearch search = shell.Controller.Search!;
        shell.Events.Clear();
        shell.Controller.Find("the");
        await shell.SettleAsync(() => search.IsSearchComplete);
        Assert.True(search.MatchCount > 3, $"matches: {search.MatchCount}");
        SearchMatch first = search.Current!;
        AssertShown(shell, first);
        FrameItem[] items = shell.Controller.LatestFrame.Items.ToArray();
        Assert.Contains(items, i => i.Kind == FrameItemKind.SearchMatch);
        Assert.Contains(items, i => i.Kind == FrameItemKind.CurrentMatch);

        // Moving through matches scrolls; highlighting draws no tile again.
        SearchMatch? next = null;
        for (int i = 0; i < search.MatchCount && (next is null || next.PageIndex == first.PageIndex); i++)
        {
            next = shell.Controller.FindNext();
        }

        await shell.SettleAsync();
        AssertShown(shell, next!);
        Assert.DoesNotContain(shell.Events, e => e.Kind == TileEventKind.Failed);
        Assert.True(shell.Controller.KeyDown("F3", InputModifiers.Shift));

        shell.Controller.ClearFind();
        await shell.SettleAsync();
        Assert.DoesNotContain(shell.Controller.LatestFrame.Items.ToArray(), i => i.Kind is FrameItemKind.SearchMatch or FrameItemKind.CurrentMatch);

        static void AssertShown(Shell shell, SearchMatch match)
        {
            LayoutRectangle box = shell.Layout.PageToViewport(shell.Viewport, match.PageIndex, match.Bounds);
            Assert.InRange(box.Y, 0, shell.Viewport.Height - box.Height);
            Assert.InRange(box.X, 0, shell.Viewport.Width - box.Width);
        }
    });

    [Fact]
    public void Dark_mode_redraws_the_tiles_from_the_cached_lists_with_black_paper_and_back() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.SampleDocument("textbook.pdf"));
        LayoutRectangle page = shell.Layout.PageToViewport(shell.Viewport, 0, shell.Layout.GetPageMetrics(0).CropBox);
        long misses = shell.Controller.Session!.Document.CacheStatistics.DisplayLists.Misses;

        shell.Controller.SetColorFilter(ColorFilter.DarkMode);
        await shell.SettleAsync();
        Assert.True(shell.Controller.LatestFrame.IsDark);
        Raster dark = shell.Draw();
        Assert.Equal(ViewerColors.DarkBackground, dark[1, 1]);
        (int darkSamples, int whiteSamples) = Count(dark, page);
        Assert.True(darkSamples > whiteSamples * 3, $"dark {darkSamples}, white {whiteSamples}");
        Assert.Equal(misses, shell.Controller.Session.Document.CacheStatistics.DisplayLists.Misses);

        shell.Controller.SetColorFilter(ColorFilter.None);
        await shell.SettleAsync();
        (darkSamples, whiteSamples) = Count(shell.Draw(), page);
        Assert.True(whiteSamples > darkSamples * 3, $"dark {darkSamples}, white {whiteSamples}");
    });

    [Fact]
    public void A_uri_link_is_raised_never_followed_and_an_internal_link_navigates() => UiThread.Run(async () =>
    {
        using (Shell shell = await Shell.OpenAsync(Shell.CorpusFile("annotations-link.pdf")))
        {
            Viewport before = shell.Viewport;
            (double x, double y) = shell.ToView(0, 100, 705);
            Assert.Equal(PointerShape.Hand, shell.Controller.GetPointerShape(x, y));
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
    public void Tab_focuses_form_fields_opens_the_editor_commits_and_escape_closes_it() => UiThread.Run(async () =>
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
        Assert.True(shell.Controller.CommitFieldText("Ada"));
        await shell.SettleAsync();
        Assert.All(
            shell.Events.Where(e => e.Kind is TileEventKind.Started or TileEventKind.Presented),
            e => Assert.Equal(DisplayLayers.None, e.Tile.Layers & DisplayLayers.Content));
        Assert.Equal("Ada", ((PdfTextField)layer.FocusedField!).Value);

        Assert.True(shell.Controller.KeyDown("Escape", InputModifiers.None));
        Assert.Null(shell.Editors[^1]);
        await shell.SettleAsync();
    });

    [Fact]
    public void Select_all_and_copy_put_the_text_on_the_hosts_clipboard() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        Assert.False(shell.Controller.KeyDown("c", InputModifiers.Meta));
        Assert.True(shell.Controller.KeyDown("a", InputModifiers.Meta));
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
    public void The_accessible_text_of_a_loaded_page_is_its_content_in_reading_order_with_bounds_in_view_units() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("tagged-structure.pdf"));
        Assert.Contains(0, shell.Controller.AccessiblePages);
        Assert.Contains(shell.AccessibilityChanges, c => c.Kind == AccessibilityChangeKind.ChildrenChanged);
        AccessibilityBridge bridge = shell.Controller.Bridge!;

        List<string> text = AccessibleText.ReadingOrder(bridge, 0);
        int heading = text.IndexOf("Broadside");
        int paragraph = text.FindIndex(t => t.Contains("A paragraph with a link", StringComparison.Ordinal));
        Assert.True(heading >= 0 && paragraph > heading, string.Join(" | ", text));
        Assert.Equal("Page 1", AccessibleText.Describe(bridge, bridge.GetPage(0)));

        AccessibleNode node = Find(bridge.GetPage(0), n => n.Role == AccessibleRole.Heading)!;
        Assert.True(shell.Controller.TryGetNodeBounds(node, out LayoutRectangle bounds));
        Assert.True(bridge.TryGetBounds(node, out LayoutRectangle pixels));
        Assert.Equal(pixels.X / shell.Ratio, bounds.X, 6);
        Assert.Equal(pixels.Width / shell.Ratio, bounds.Width, 6);

        Assert.True(shell.Controller.KeyDown("h", InputModifiers.None));
        Assert.Contains(shell.AccessibilityChanges, c => c.Kind == AccessibilityChangeKind.FocusChanged && c.Node?.Role == AccessibleRole.Heading);

        static AccessibleNode? Find(AccessibleNode node, Func<AccessibleNode, bool> match) =>
            match(node) ? node : node.Children.Select(child => Find(child, match)).FirstOrDefault(found => found is not null);
    });

    [Fact]
    public void Untagged_two_columns_read_column_by_column() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("layout-two-column-untagged.pdf"));
        List<string> text = AccessibleText.ReadingOrder(shell.Controller.Bridge!, 0);
        string joined = string.Join(" ", string.Join(" ", text).Split((char[])['\n', ' '], StringSplitOptions.RemoveEmptyEntries));

        // The content draws each line across both columns in turn; the readable text runs down the first column first.
        Assert.Contains("Columns are found from the white space that runs down the page", joined, StringComparison.Ordinal);
        Assert.True(joined.IndexOf("down the page", StringComparison.Ordinal) < joined.IndexOf("The second column", StringComparison.Ordinal), joined);
    });

    [Fact]
    public void Retired_tiles_are_released_once_a_frame_without_them_was_shown() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("layout-three-column.pdf"));
        TilePixels[] shown = [.. shell.Controller.LatestFrame.Items.ToArray().Where(i => i.Kind == FrameItemKind.Tile).Select(i => i.Pixels!)];
        Assert.NotEmpty(shown);
        shell.Controller.CloseDocument();
        Assert.All(shown, t => Assert.False(t.IsReleased));
        shell.Controller.Frame();
        Assert.All(shown, t => Assert.True(t.IsReleased));
        Assert.Empty(shell.Controller.Surface.Retired);
        Raster frame = shell.Draw();
        Assert.Equal(ViewerColors.Background, frame[frame.Width / 2, frame.Height / 2]);
        Assert.Equal(0, shell.Controller.State.PageCount);
    });

    [Fact]
    public void A_background_session_renders_on_workers_and_wakes_the_ui_thread_to_present() => UiThread.Run(async () =>
    {
        var host = new WakeCountingHost();
        using var controller = new ViewerController(host);
        controller.Resize(400, 600, 2);
#pragma warning disable CA2000 // the controller owns the session
        DocumentSession session = DocumentSession.Open(Shell.SampleDocument("textbook.pdf"), Shell.Options(ViewerThreading.Background));
#pragma warning restore CA2000
        await controller.OpenAsync(_ => Task.FromResult(session), ownsSession: true, null);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromMinutes(1) && (controller.Frame() || controller.State.IsRendering || controller.IsLoadingPages || !controller.LatestFrame.Items.ToArray().Any(i => i.Kind == FrameItemKind.Tile)))
        {
            await Task.Delay(5);
        }

        Assert.Contains(controller.LatestFrame.Items.ToArray(), i => i.Kind == FrameItemKind.Tile);
        Assert.True(host.FrameRequests > 1);
        Assert.Empty(host.Errors);
    });

    [Fact]
    public void A_tile_a_closed_session_presents_late_never_reaches_the_next_document()
    {
        var surface = new TileSurface(TimeProvider.System);
        IRenderSurface closed = surface.ForCurrentSession();
        var tile = new Tile(0, 1, new PixelRectangle(0, 0, 4, 4));
        using var pixels = new Bitmap(4, 4);
        closed.Present(tile, pixels);
        surface.Drain();
        Assert.True(surface.TryGetImage(tile, out _));

        surface.Clear();
        IRenderSurface next = surface.ForCurrentSession();
        closed.Present(tile, pixels); // late: the old session's worker
        surface.Drain();
        Assert.False(surface.TryGetImage(tile, out _));

        next.Present(tile, pixels);
        surface.Drain();
        Assert.True(surface.TryGetImage(tile, out TilePixels? copy));
        Assert.Equal(16 * 4, copy!.Pixels.Length);
        surface.Clear();
        surface.ReleaseRetired();
        Assert.True(copy.IsReleased);
    }

    private static (int Dark, int White) Count(Raster frame, LayoutRectangle page)
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
                white += c == 0xFFFFFFFF ? 1 : 0;
            }
        }

        return (dark, white);
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

    private sealed class WakeCountingHost : IViewerHost
    {
        public int FrameRequests { get; private set; }

        public List<Exception> Errors { get; } = [];

        public void RequestFrame() => FrameRequests++;

        public void RequestPaint()
        {
        }

        public void StateChanged(PdfViewState state)
        {
        }

        public void ActionRequested(ViewerActionEventArgs e)
        {
        }

        public void Error(Exception exception) => Errors.Add(exception);

        public void EditorChanged(FieldEditor? editor)
        {
        }

        public void EditorMoved(double x, double y, double width, double height)
        {
        }

        public void AccessibilityChanged(AccessibilityChange change)
        {
        }

        public void CopyText(string text)
        {
        }
    }
}
