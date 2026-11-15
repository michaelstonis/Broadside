using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Broadside.Graphics;
using Broadside.Rendering;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Broadside.Viewer.Uno.Sample;

/// <summary>
/// The sample's hooks for its UI tests (tests/Broadside.Viewer.Uno.UITests).
/// </summary>
/// <remarks>
/// <para>
/// WebAssembly head (with <c>?automation=1</c> in the address): every 100 ms the page publishes what the viewer shows as
/// <c>globalThis.broadsideState</c> (page, zoom, scroll offset, tiles on screen, selected text, search matches, dark mode, page 1's
/// automation-peer text) and takes commands pushed to <c>globalThis.broadsideCommands</c> (<c>{"open":"paper.pdf"}</c>). The tests
/// drive everything else with real mouse, wheel and keyboard input through Playwright.
/// </para>
/// <para>
/// Desktop head (with the environment variable <c>BROADSIDE_UNO_SCRIPT=1</c>): the app runs a script through the view's own input
/// path (pointer, wheel and key events as the view's handlers deliver them) and its public API: open, scroll, zoom, select, search,
/// dark mode and the automation peers; prints one <c>BROADSIDE_STEP</c> line per step and <c>BROADSIDE_SCRIPT_OK</c> or
/// <c>BROADSIDE_SCRIPT_FAILED</c>, then exits with 0 or 1.
/// </para>
/// </remarks>
internal static partial class Automation
{
    private static string? s_selectedText;
    private static readonly List<string> s_errors = [];
    private static readonly List<string> s_pointers = [];

    public static void Start(MainPage page)
    {
        page.Viewer.Error += (_, e) => s_errors.Add(e.Exception.Message);
        page.Viewer.StateChanged += (_, _) => WatchSelection(page.Viewer);

        // The last pointer presses the view saw (device, button, position), for a failing UI test's message.
        page.Viewer.AddHandler(
            Microsoft.UI.Xaml.UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
            {
                Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(page.Viewer);
                s_pointers.Add($"{e.Pointer.PointerDeviceType} left={point.Properties.IsLeftButtonPressed} at {point.Position.X:0},{point.Position.Y:0}");
                if (s_pointers.Count > 8)
                {
                    s_pointers.RemoveAt(0);
                }
            }),
            handledEventsToo: true);
        StartPlatform(page);
    }

    static partial void StartPlatform(MainPage page);

    private static void WatchSelection(PdfView view)
    {
        if (view.Selection is { } selection && !ReferenceEquals(selection, s_watched))
        {
            s_watched = selection;
            selection.Changed += async (_, _) =>
            {
                try
                {
                    s_selectedText = selection.IsEmpty ? null : await selection.GetTextAsync();
                }
                catch (OperationCanceledException)
                {
                }
            };
        }
    }

    private static TextSelection? s_watched;

    /// <summary>What the viewer shows, as JSON.</summary>
    public static string State(MainPage page)
    {
        PdfView view = page.Viewer;
        ViewerController controller = view.Controller;
        PdfViewState state = view.State;
        Viewport viewport = controller.Input?.Viewport ?? default;
        int tiles = 0;
        int shown = 0;
        foreach (FrameItem item in controller.LatestFrame.Items)
        {
            if (item.Kind == FrameItemKind.Tile)
            {
                tiles++;
                shown += item.Pixels?.Image is not null ? 1 : 0;
            }
        }

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("document", page.DocumentName);
            json.WriteNumber("page", state.PageIndex);
            json.WriteNumber("pageCount", state.PageCount);
            json.WriteNumber("zoom", state.Zoom);
            json.WriteBoolean("loading", state.IsLoading);
            json.WriteBoolean("rendering", state.IsRendering);
            json.WriteNumber("scrollX", viewport.X / Math.Max(controller.PixelRatio, 1e-9));
            json.WriteNumber("scrollY", viewport.Y / Math.Max(controller.PixelRatio, 1e-9));
            json.WriteNumber("width", controller.Width);
            json.WriteNumber("height", controller.Height);
            json.WriteNumber("tiles", tiles);
            json.WriteNumber("tilesShown", shown);
            json.WriteString("selection", s_selectedText);
            json.WriteNumber("matches", view.Search?.MatchCount ?? 0);
            json.WriteNumber("currentMatch", view.Search?.CurrentIndex ?? -1);
            json.WriteNumber("currentMatchPage", view.Search?.Current?.PageIndex ?? -1);
            json.WriteBoolean("searchComplete", view.Search?.IsSearchComplete ?? false);
            json.WriteString("query", view.Search?.Query);
            json.WriteBoolean("dark", controller.LatestFrame.IsDark);
            json.WriteBoolean("textLoaded", controller.Selection?.TryGetPage(state.PageIndex, out _) ?? false);
            Windows.Foundation.Point origin = view.TransformToVisual(null).TransformPoint(default);
            json.WriteNumber("viewerX", origin.X);
            json.WriteNumber("viewerY", origin.Y);
            if (FindText(controller, state.PageIndex) is (double textX, double textY))
            {
                // A point over text on the current page, in the viewer's units: where a mouse drag starts a selection.
                json.WriteNumber("textX", textX);
                json.WriteNumber("textY", textY);
            }

            json.WriteStartArray("readingOrder");
            foreach (string text in ReadingOrder(view, 0).Take(40))
            {
                json.WriteStringValue(text);
            }

            json.WriteEndArray();
            json.WriteStartArray("pointers");
            foreach (string pointer in s_pointers)
            {
                json.WriteStringValue(pointer);
            }

            json.WriteEndArray();
            json.WriteStartArray("errors");
            foreach (string error in s_errors)
            {
                json.WriteStringValue(error);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Page <paramref name="pageIndex"/>'s text as its automation peers read it: the view's peer, its page peer, the page's nodes depth first.</summary>
    public static List<string> ReadingOrder(PdfView view, int pageIndex)
    {
        var text = new List<string>();
        if ((FrameworkElementAutomationPeer.FromElement(view) ?? FrameworkElementAutomationPeer.CreatePeerForElement(view)) is not PdfViewAutomationPeer peer)
        {
            return text;
        }

        foreach (AutomationPeer page in peer.GetChildren() ?? [])
        {
            if (page is AccessibleNodeAutomationPeer { Node.PageIndex: var index } && index == pageIndex)
            {
                Walk(page, text);
            }
        }

        return text;

        static void Walk(AutomationPeer node, List<string> text)
        {
            IList<AutomationPeer>? children = node.GetChildren();
            if (children is null || children.Count == 0)
            {
                string name = node.GetName();
                if (!string.IsNullOrEmpty(name))
                {
                    text.Add(name);
                }

                return;
            }

            foreach (AutomationPeer child in children)
            {
                Walk(child, text);
            }
        }
    }

    /// <summary>Applies one command (from the browser tests): open a document, or set dark mode.</summary>
    public static void Apply(MainPage page, JsonElement command)
    {
        if (command.TryGetProperty("open", out JsonElement open) && open.GetString() is { } name)
        {
            page.Open(name);
        }

        if (command.TryGetProperty("dark", out JsonElement dark))
        {
            page.Viewer.ColorFilter = dark.GetBoolean() ? ColorFilter.DarkMode : ColorFilter.None;
        }
    }

    /// <summary>The desktop script: every acceptance step through the view, one line each. Returns whether all passed.</summary>
    public static async Task<bool> RunScriptAsync(MainPage page, TextWriter log)
    {
        PdfView view = page.Viewer;
        ViewerController controller = view.Controller;
        bool ok = true;

        async Task Step(string name, Func<Task<string?>> body)
        {
            string? failure;
            var watch = Stopwatch.StartNew();
            try
            {
                failure = await body();
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
            }

            ok &= failure is null;
            await log.WriteLineAsync($"BROADSIDE_STEP {name} {(failure is null ? "ok" : "FAILED: " + failure)} ({watch.ElapsedMilliseconds} ms) {State(page)}");
            await log.FlushAsync();
        }

        await Step("open", async () =>
        {
            page.Open("textbook.pdf");
            string? drawn = await Until(() => view.State.PageCount == 8 && Settled(controller) && controller.Selection?.TryGetPage(0, out _) == true, "page 1 drawn");
            if (drawn is not null)
            {
                return drawn;
            }

            // What Uno actually shows: black text on white paper (tiles uploaded in the right channel order).
            (int dark, int white, int colored) = await SampleAsync(view);
            return dark > 200 && white > dark * 3 && colored < white ? null : $"page pixels: dark {dark}, white {white}, colored {colored}";
        });

        await Step("scroll", async () =>
        {
            double before = controller.Input!.Viewport.Y;
            for (int i = 0; i < 10; i++)
            {
                controller.Wheel(0, 3, WheelDeltaMode.Line, InputModifiers.None, 200, 200); // a wheel notch down, as OnWheel maps it
            }

            string? moved = await Until(() => controller.Input.Viewport.Y > before && Settled(controller), "scrolled and drawn");
            view.ScrollBy(0, 4000);
            return moved ?? await Until(() => view.State.PageIndex > 0 && Settled(controller), "a later page shown");
        });

        await Step("zoom", async () =>
        {
            double before = view.State.Zoom;
            controller.Wheel(0, -100, WheelDeltaMode.Pixel, InputModifiers.Control, 200, 200);
            controller.KeyDown("+", InputModifiers.None);
            return await Until(() => view.State.Zoom > before * 1.3 && Settled(controller), "zoomed in and drawn");
        });

        await Step("select", async () =>
        {
            view.ZoomTo(ZoomMode.FitWidth);
            view.GoToPage(0);
            await Until(() => view.State.PageIndex == 0 && Settled(controller) && controller.Selection?.TryGetPage(0, out _) == true, "page 1 with its text");
            if (FindText(controller, 0) is not (double x, double y))
            {
                return "no text under the pointer on page 1";
            }

            controller.PointerDown(1, PointerKind.Mouse, x, y, InputModifiers.None);
            controller.PointerMove(1, PointerKind.Mouse, x + 60, y + 30, inContact: true);
            controller.PointerMove(1, PointerKind.Mouse, x + 150, y + 60, inContact: true);
            controller.PointerUp(1, x + 150, y + 60);
            return await Until(() => !string.IsNullOrWhiteSpace(s_selectedText), "selected text");
        });

        await Step("search", async () =>
        {
            view.Find("the");
            string? found = await Until(() => view.Search!.IsSearchComplete && view.Search.MatchCount > 0 && Settled(controller), "matches");
            if (found is not null)
            {
                return found;
            }

            SearchMatch? next = view.FindNext();
            return next is null ? "no next match" : await Until(() => view.State.PageIndex == next.PageIndex && Settled(controller), "the next match shown");
        });

        await Step("dark", async () =>
        {
            view.ColorFilter = ColorFilter.DarkMode;
            string? dark = await Until(() => controller.LatestFrame.IsDark && Settled(controller), "dark tiles");
            if (dark is null)
            {
                (int darkPixels, int whitePixels, _) = await SampleAsync(view);
                dark = darkPixels > whitePixels * 3 ? null : $"dark mode pixels: dark {darkPixels}, white {whitePixels}";
            }

            view.ColorFilter = ColorFilter.None;
            return dark ?? await Until(() => !controller.LatestFrame.IsDark && Settled(controller), "light again");
        });

        await Step("accessibility", async () =>
        {
            page.Open("tagged-structure.pdf");
            string? loaded = await Until(() => controller.AccessiblePages.Contains(0) && ReadingOrder(view, 0).Count > 0, "page 1's peers");
            if (loaded is not null)
            {
                return loaded;
            }

            List<string> text = ReadingOrder(view, 0);
            int heading = text.IndexOf("Broadside");
            int paragraph = text.FindIndex(t => t.Contains("A paragraph with a link", StringComparison.Ordinal));
            return heading >= 0 && paragraph > heading ? null : "reading order: " + string.Join(" | ", text);
        });

        await log.WriteLineAsync(ok ? "BROADSIDE_SCRIPT_OK" : "BROADSIDE_SCRIPT_FAILED");
        await log.FlushAsync();
        return ok;
    }

    /// <summary>Whether the view drew everything it wants and showed it through Uno (every tile uploaded into a bitmap).</summary>
    private static bool Settled(ViewerController controller)
    {
        if (controller.State.IsRendering || controller.IsLoadingPages)
        {
            return false;
        }

        int tiles = 0;
        foreach (FrameItem item in controller.LatestFrame.Items)
        {
            if (item.Kind == FrameItemKind.Tile)
            {
                tiles++;
                if (item.Pixels?.Image is null)
                {
                    return false;
                }
            }
        }

        return tiles > 0;
    }

    /// <summary>Renders the view through Uno and counts dark, white and strongly colored pixels over the first page shown.</summary>
    private static async Task<(int Dark, int White, int Colored)> SampleAsync(PdfView view)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(view);
        byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        ViewerFrame frame = view.Controller.LatestFrame;
        LayoutRectangle paper = default;
        foreach (FrameItem item in frame.Items)
        {
            if (item.Kind == FrameItemKind.Paper)
            {
                paper = item.Bounds;
                break;
            }
        }

        // Frame items are in the view's device pixels; the bitmap may be at another scale.
        double scale = width / Math.Max(frame.Width, 1);
        int dark = 0;
        int white = 0;
        int colored = 0;
        for (int y = (int)Math.Max(paper.Y * scale, 0) + 4; y < Math.Min(paper.Bottom * scale, height) - 4; y += 3)
        {
            for (int x = (int)Math.Max(paper.X * scale, 0) + 4; x < Math.Min(paper.Right * scale, width) - 4; x += 3)
            {
                int o = ((y * width) + x) * 4;
                int b = pixels[o];
                int g = pixels[o + 1];
                int r = pixels[o + 2];
                dark += r < 96 && g < 96 && b < 96 ? 1 : 0;
                white += r > 240 && g > 240 && b > 240 ? 1 : 0;
                colored += Math.Abs(r - b) > 80 ? 1 : 0;
            }
        }

        return (dark, white, colored);
    }

    private static async Task<string?> Until(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (condition())
            {
                return null;
            }

            await Task.Delay(50);
        }

        return "timed out waiting for " + what;
    }

    private static (double X, double Y)? FindText(ViewerController controller, int pageIndex)
    {
        if (controller.Selection?.TryGetPage(pageIndex, out SelectableText? text) != true || controller.Zoom is not { } zoom || controller.Input is not { } input)
        {
            return null;
        }

        PdfRectangle crop = zoom.Layout.GetPageMetrics(pageIndex).CropBox;
        // Body text (below a chapter's display heading), where a drag spans several lines of ordinary size.
        for (double y = crop.Top - (crop.Height * 0.35); y > crop.Bottom; y -= 4)
        {
            for (double x = crop.Left + 20; x < crop.Right - 120; x += 4)
            {
                if (text!.GetGlyphAt(new PathPoint(x, y)) >= 0 && text.GetGlyphAt(new PathPoint(x + 30, y)) >= 0)
                {
                    PathPoint p = zoom.Layout.PageToViewport(input.Viewport, pageIndex, x, y);
                    double vx = p.X / controller.PixelRatio;
                    double vy = p.Y / controller.PixelRatio;
                    if (vx > 0 && vy > 0 && vx < controller.Width - 160 && vy < controller.Height - 80)
                    {
                        return (vx, vy);
                    }
                }
            }
        }

        return null;
    }
}
