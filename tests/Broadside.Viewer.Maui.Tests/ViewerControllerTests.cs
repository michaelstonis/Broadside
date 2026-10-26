using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Broadside.Viewer.Maui.Tests;

/// <summary>The MAUI shell's gesture, key and frame mapping onto the viewer core, through the controller every platform shares.</summary>
public sealed class ViewerControllerTests
{
    [Fact]
    public void TraceMonkey_opens_at_the_top_of_page_one_at_fit_width_and_draws_it_with_skia() => UiThread.Run(async () =>
    {
        // The size arrives after the document (as MAUI lays out after the source is set): the first real size still shows page 1's top.
        Shell.RequireTraceMonkey();
        using Shell shell = await Shell.OpenAsync(Shell.TraceMonkey, sizeFirst: false);
        Viewport viewport = shell.Viewport;
        Assert.Equal(0, viewport.X);
        Assert.Equal(0, viewport.Y);
        Assert.Equal(shell.Layout.GetZoom(ZoomMode.FitWidth, viewport.Width, viewport.Height, 0, shell.Ratio * 96 / 72), viewport.Zoom, 9);
        Assert.Equal(new PdfViewState(0, 14, viewport.Zoom / (shell.Ratio * 96 / 72), false, false), shell.Controller.State);

        using SKBitmap frame = shell.Draw();
        LayoutRectangle page = shell.Layout.PageToViewport(viewport, 0, shell.Layout.GetPageMetrics(0).CropBox);
        Assert.Equal(ViewerFrame.Background, frame.GetPixel(1, 1));
        int dark = 0;
        int white = 0;
        for (int y = (int)page.Y + 4; y < Math.Min(page.Bottom, frame.Height) - 4; y += 3)
        {
            for (int x = (int)page.X + 4; x < page.Right - 4; x += 3)
            {
                SKColor c = frame.GetPixel(x, y);
                dark += c.Red < 96 && c.Green < 96 && c.Blue < 96 ? 1 : 0;
                white += c == SKColors.White ? 1 : 0;
            }
        }

        // Page 1's title and abstract: plenty of black text on white paper, with every visible tile drawn sharp.
        Assert.True(dark > 300, $"dark samples: {dark}");
        Assert.True(white > dark * 3, $"white samples: {white}");
        Assert.DoesNotContain(shell.Events, e => e.Kind == TileEventKind.Failed);
    });

    [Fact]
    public void Pinch_and_double_tap_traces_are_identical_at_any_pixel_ratio_and_match_the_input_model() => UiThread.Run(async () =>
    {
        byte[] document = ManyPagesDocument.Create(6);
        double[][] traces = new double[3][];
        double[] ratios = [3, 2.625, 1];
        for (int i = 0; i < ratios.Length; i++)
        {
            using Shell shell = await Shell.OpenAsync(document, 390, 844, ratios[i]);
            var trace = new List<double>();
            void Record()
            {
                Viewport v = shell.Viewport;
                trace.AddRange([v.X / shell.Ratio, v.Y / shell.Ratio, v.Zoom / shell.Ratio]);
            }

            // Two-finger pinch out around (200, 300): the second finger turns the pan into a pinch (InputModel's own rule).
            shell.Touch(1, SKTouchAction.Pressed, SKTouchDeviceType.Touch, 150, 300);
            shell.Touch(2, SKTouchAction.Pressed, SKTouchDeviceType.Touch, 250, 300);
            for (int step = 1; step <= 10; step++)
            {
                shell.Clock.Advance(TimeSpan.FromMilliseconds(16));
                shell.Touch(1, SKTouchAction.Moved, SKTouchDeviceType.Touch, 150 - (step * 8), 300);
                shell.Touch(2, SKTouchAction.Moved, SKTouchDeviceType.Touch, 250 + (step * 8), 300);
                Record();
            }

            shell.Touch(2, SKTouchAction.Released, SKTouchDeviceType.Touch, 330, 300);
            shell.Touch(1, SKTouchAction.Released, SKTouchDeviceType.Touch, 70, 300);
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

        // The pinch zoomed in, the double-tap doubled fit-width, the second double-tap went back.
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

        shell.Drag(1, SKTouchDeviceType.Mouse, x, y, x + 60, y);
        Assert.False(shell.Controller.Selection!.IsEmpty);
        Assert.Equal(before, shell.Viewport);

        shell.Drag(2, SKTouchDeviceType.Touch, x + 30, y + 40, x + 30, y - 60);
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

        shell.Touch(1, SKTouchAction.Pressed, SKTouchDeviceType.Touch, x, y);
        shell.Clock.Advance(ViewerController.LongPress + TimeSpan.FromMilliseconds(20));
        shell.Controller.Frame();
        shell.Touch(1, SKTouchAction.Released, SKTouchDeviceType.Touch, x, y);
        TextSelection selection = shell.Controller.Selection!;
        Assert.False(selection.IsEmpty);
        Assert.Equal(SelectionGranularity.Word, selection.Granularity);
        Assert.Equal(before, shell.Viewport);
        TextPosition end = selection.End;

        // The end handle sits under the last quad's bottom-right corner; dragging it down a few lines extends the selection.
        await shell.SettleAsync();
        var quads = new Quad[16];
        int count = selection.GetQuads(0, quads);
        (double hx, double hy) = shell.ToView(0, quads[count - 1].BottomRight.X, quads[count - 1].BottomRight.Y);
        shell.Drag(2, SKTouchDeviceType.Touch, hx, hy + 6, hx + 20, hy + 60);
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
        Assert.True(shell.Controller.CommitFieldText("Ada"));

        // The evicted overlay tiles stay on screen until their replacements arrive: the composed frame never loses them.
        shell.Controller.Frame();
        Assert.True(OverlayItems(shell) >= overlaysBefore);
        await shell.SettleAsync();

        Assert.Contains(shell.Events, e => e.Kind == TileEventKind.Presented);
        Assert.All(
            shell.Events.Where(e => e.Kind is TileEventKind.Started or TileEventKind.Presented or TileEventKind.Evicted),
            e => Assert.Equal(DisplayLayers.None, e.Tile.Layers & DisplayLayers.Content));
        var widget = (PdfWidgetAnnotation)layer.GetAnnotation(field);
        Assert.Equal("Ada", ((PdfTextField)widget.Field!).Value);
    });

    [Fact]
    public void A_resize_keeps_the_scaled_tiles_on_screen_and_never_shows_black() => UiThread.Run(async () =>
    {
        // A heavy enough document that the first frame after the resize cannot finish every new tile within its budget.
        Shell.RequireTraceMonkey();
        using Shell shell = await Shell.OpenAsync(Shell.TraceMonkey);
        int tilesBefore = TileItems(shell);
        Assert.True(tilesBefore > 0);
        shell.Events.Clear();

        shell.Controller.Resize(520, 600, shell.Ratio);
        shell.Controller.Frame();
        Assert.DoesNotContain(shell.Events, e => e.Kind == TileEventKind.Evicted);
        Assert.True(TileItems(shell) > 0, "the frame after a resize shows the tiles it had, scaled");
        using (SKBitmap frame = shell.Draw())
        {
            // The background around the page and the page's own text: nothing black, nothing blank.
            Assert.Equal(ViewerFrame.Background, frame.GetPixel(0, 0));
            int dark = 0;
            int black = 0;
            for (int y = 0; y < frame.Height; y += 4)
            {
                for (int x = 0; x < frame.Width; x += 4)
                {
                    SKColor c = frame.GetPixel(x, y);
                    dark += c.Red < 96 && c.Green < 96 && c.Blue < 96 ? 1 : 0;
                    black += c == SKColors.Black ? 1 : 0;
                }
            }

            Assert.True(dark > 100, $"dark samples: {dark}");
            Assert.True(black < dark, "no black areas beyond the text");
        }

        // Fit-width holds across the resize (the zoom follows the new width).
        await shell.SettleAsync();
        Viewport v = shell.Viewport;
        Assert.Equal(shell.Layout.GetZoom(ZoomMode.FitWidth, v.Width, v.Height, 0, shell.Ratio * 96 / 72), v.Zoom, 6);
    });

    [Fact]
    public void Keys_wheel_and_magnify_map_onto_the_input_model() => UiThread.Run(async () =>
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
        SKTouchEventArgs wheel = shell.Touch(9, SKTouchAction.WheelChanged, SKTouchDeviceType.Mouse, 100, 100, wheelDelta: -120);
        Assert.True(wheel.Handled);
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
    public void The_accessibility_tree_of_visible_pages_reaches_the_host_with_bounds_in_view_units() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("tagged-structure.pdf"));
        Assert.Contains(0, shell.Controller.AccessiblePages);
        Assert.Contains(shell.AccessibilityChanges, c => c.Kind == AccessibilityChangeKind.ChildrenChanged);
        AccessibilityBridge bridge = shell.Controller.Bridge!;
        AccessibleNode heading = Find(bridge.GetPage(0), n => n.Role == AccessibleRole.Heading)!;
        Assert.Equal(heading.Name, SemanticLayer.Describe(bridge, heading));
        Assert.True(shell.Controller.TryGetNodeBounds(heading, out LayoutRectangle bounds));
        Assert.True(bridge.TryGetBounds(heading, out LayoutRectangle pixels));
        Assert.Equal(pixels.X / shell.Ratio, bounds.X, 6);
        Assert.Equal(pixels.Width / shell.Ratio, bounds.Width, 6);

        Assert.True(shell.Controller.KeyDown("h", InputModifiers.None));
        Assert.Contains(shell.AccessibilityChanges, c => c.Kind == AccessibilityChangeKind.FocusChanged && c.Node?.Role == AccessibleRole.Heading);

        static AccessibleNode? Find(AccessibleNode node, Func<AccessibleNode, bool> match) =>
            match(node) ? node : node.Children.Select(child => Find(child, match)).FirstOrDefault(found => found is not null);
    });

    [Fact]
    public void The_presenter_releases_retired_tile_images_only_when_no_frame_shows_them() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("layout-three-column.pdf"));
        int images = TileItems(shell);
        Assert.True(images > 0);
        shell.Controller.CloseDocument();
        Assert.True(shell.Controller.Surface.Retired.Count >= images);
        shell.Controller.Frame();
        Assert.Empty(shell.Controller.Surface.Retired);
        using SKBitmap frame = shell.Draw();
        Assert.Equal(ViewerFrame.Background, frame.GetPixel(frame.Width / 2, frame.Height / 2));
    });

    private static int TileItems(Shell shell) => CountItems(shell, _ => true);

    [Fact]
    public void A_tile_a_closed_session_presents_late_never_reaches_the_next_document()
    {
        // Review #364 (P1): a worker can present after the surface was cleared for the next document; that tile is dropped.
        var surface = new TileImageSurface(TimeProvider.System);
        IRenderSurface closed = surface.ForCurrentSession();
        var tile = new Tile(0, 1, new PixelRectangle(0, 0, 4, 4));
        using var pixels = new Broadside.Rendering.Bitmap(4, 4);
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
        Assert.True(surface.TryGetImage(tile, out _));
        surface.Clear();
    }

    private static int OverlayItems(Shell shell) => CountItems(shell, shell.Controller.Surface.IsOverlay);

    private static int CountItems(Shell shell, Func<SKImage, bool> match)
    {
        int count = 0;
        foreach (ViewerFrame.Item item in shell.Controller.Presenter.Latest!.Items)
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
}
