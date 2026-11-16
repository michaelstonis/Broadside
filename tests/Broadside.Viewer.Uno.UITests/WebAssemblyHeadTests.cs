using System.Text.Json;
using Microsoft.Playwright;

namespace Broadside.Viewer.Uno.UITests;

/// <summary>
/// The WebAssembly head in a real browser: opens, scrolls, zooms, selects and searches through Playwright's mouse, wheel and keyboard,
/// and reads page text through the view's automation peers. The page reports what the viewer shows as <c>globalThis.broadsideState</c>.
/// </summary>
public sealed class WebAssemblyHeadTests(UnoHost host)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    public static TheoryData<string> Browsers => ["chromium", "webkit"];

    [Theory]
    [MemberData(nameof(Browsers))]
    public async Task The_sample_opens_scrolls_zooms_selects_and_searches(string browserName)
    {
        UnoHost.Require(UnoHost.WasmDirectory, "WebAssembly head");
        IBrowser browser = await host.GetBrowserAsync(browserName);
        await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1100, Height = 800 } });
        IPage page = await context.NewPageAsync();
        var console = new List<string>();
        page.Console += (_, message) => console.Add(message.Text);
        await page.GotoAsync(new Uri(host.BaseAddress, "?automation=1").ToString());

        // Open: the first document draws page 1 at fit-width, every visible tile shown.
        JsonElement state = await WaitAsync(page, s => s.GetProperty("pageCount").GetInt32() == 8 && Settled(s), "the textbook drawn", console);
        Assert.Equal("textbook.pdf", state.GetProperty("document").GetString());
        Assert.Equal(0, state.GetProperty("page").GetInt32());
        double viewerX = state.GetProperty("viewerX").GetDouble();
        double viewerY = state.GetProperty("viewerY").GetDouble();
        double centerX = viewerX + (state.GetProperty("width").GetDouble() / 2);
        double centerY = viewerY + (state.GetProperty("height").GetDouble() / 2);

        // Scroll: wheel notches over the viewer.
        double scrolled = state.GetProperty("scrollY").GetDouble();
        await page.Mouse.MoveAsync((float)centerX, (float)centerY);
        for (int i = 0; i < 6; i++)
        {
            await page.Mouse.WheelAsync(0, 120);
        }

        state = await WaitAsync(page, s => s.GetProperty("scrollY").GetDouble() > scrolled + 100 && Settled(s), "scrolled down", console);

        // Zoom: Ctrl+wheel up zooms in around the pointer.
        double zoom = state.GetProperty("zoom").GetDouble();
        await page.Keyboard.DownAsync("Control");
        await page.Mouse.WheelAsync(0, -120);
        await page.Keyboard.UpAsync("Control");
        state = await WaitAsync(page, s => s.GetProperty("zoom").GetDouble() > zoom * 1.05 && Settled(s), "zoomed in", console);

        // Keys reach the viewer once it has focus (a click on the margin beside the page): Page Down scrolls a screen.
        await page.Mouse.ClickAsync((float)(viewerX + state.GetProperty("width").GetDouble() - 4), (float)centerY);
        scrolled = state.GetProperty("scrollY").GetDouble();
        await page.Keyboard.PressAsync("PageDown");
        state = await WaitAsync(page, s => s.GetProperty("scrollY").GetDouble() > scrolled + 200 && Settled(s), "Page Down", console);

        // Back out (Ctrl+wheel down) and to the top: the text of page 1 is where the page says it is.
        zoom = state.GetProperty("zoom").GetDouble();
        await page.Mouse.MoveAsync((float)centerX, (float)centerY);
        await page.Keyboard.DownAsync("Control");
        await page.Mouse.WheelAsync(0, 120);
        await page.Keyboard.UpAsync("Control");
        await WaitAsync(page, s => s.GetProperty("zoom").GetDouble() < zoom / 1.05 && Settled(s), "zoomed out", console);
        await page.Keyboard.PressAsync("Home");
        state = await WaitAsync(page, s => s.GetProperty("page").GetInt32() == 0 && s.GetProperty("scrollY").GetDouble() < 1 && s.GetProperty("textLoaded").GetBoolean() && s.TryGetProperty("textX", out _) && Settled(s), "page 1's text", console);

        // Select: a mouse drag over the text selects it; a double click selects a word.
        double x = viewerX + state.GetProperty("textX").GetDouble();
        double y = viewerY + state.GetProperty("textY").GetDouble();
        await page.Mouse.MoveAsync((float)x, (float)y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(x + 80), (float)(y + 20), new MouseMoveOptions { Steps = 5 });
        await page.Mouse.MoveAsync((float)(x + 160), (float)(y + 60), new MouseMoveOptions { Steps = 5 });
        await page.Mouse.UpAsync();
        state = await WaitAsync(page, s => s.GetProperty("selection").GetString() is { Length: > 3 }, "a selection", console);
        // Escape clears the drag's selection, so the double click's word is seen even when the drag happened to select that same word.
        await page.Keyboard.PressAsync("Escape");
        await WaitAsync(page, s => s.GetProperty("selection").GetString() is not { Length: > 0 }, "the selection cleared", console);
        await Task.Delay(600, TestContext.Current.CancellationToken); // past the multi-click interval: the drag's press does not count towards the double click
        await page.Mouse.DblClickAsync((float)x, (float)y);
        state = await WaitAsync(page, s => s.GetProperty("selection").GetString() is { Length: > 0 } text && !text.Contains(' ', StringComparison.Ordinal), "a selected word", console);

        // Search: Ctrl+F focuses the find box; typing and Enter highlight every match and show the first.
        await page.Keyboard.PressAsync("Control+f");
        await page.Keyboard.TypeAsync("the");
        await page.Keyboard.PressAsync("Enter");
        state = await WaitAsync(page, s => s.GetProperty("query").GetString() == "the" && s.GetProperty("searchComplete").GetBoolean() && s.GetProperty("matches").GetInt32() > 3 && Settled(s), "search results", console);
        int matches = state.GetProperty("matches").GetInt32();
        await page.Keyboard.PressAsync("Enter");
        await WaitAsync(page, s => s.GetProperty("currentMatch").GetInt32() == 1 && s.GetProperty("matches").GetInt32() == matches, "the next match", console);

        // Dark mode redraws the tiles; the automation peers read a tagged page in reading order.
        await page.EvaluateAsync("globalThis.broadsideCommands.push({ dark: true })");
        await WaitAsync(page, s => s.GetProperty("dark").GetBoolean() && Settled(s), "dark mode", console);
        await page.EvaluateAsync("globalThis.broadsideCommands.push({ dark: false, open: 'tagged-structure.pdf' })");
        state = await WaitAsync(page, s => s.GetProperty("document").GetString() == "tagged-structure.pdf" && s.GetProperty("readingOrder").GetArrayLength() > 0, "the tagged page's peers", console);
        List<string> text = [.. state.GetProperty("readingOrder").EnumerateArray().Select(e => e.GetString()!)];
        int heading = text.IndexOf("Broadside");
        int paragraph = text.FindIndex(t => t.Contains("A paragraph with a link", StringComparison.Ordinal));
        Assert.True(heading >= 0 && paragraph > heading, string.Join(" | ", text));
        Assert.Empty(state.GetProperty("errors").EnumerateArray());
    }

    private static bool Settled(JsonElement state) =>
        !state.GetProperty("loading").GetBoolean() && !state.GetProperty("rendering").GetBoolean()
        && state.GetProperty("tiles").GetInt32() > 0 && state.GetProperty("tilesShown").GetInt32() == state.GetProperty("tiles").GetInt32();

    private static async Task<JsonElement> WaitAsync(IPage page, Func<JsonElement, bool> condition, string what, List<string> console)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string? last = null;
        while (watch.Elapsed < Patience)
        {
            last = await page.EvaluateAsync<string?>("globalThis.broadsideState ? JSON.stringify(globalThis.broadsideState) : null");
            if (last is not null)
            {
                using JsonDocument document = JsonDocument.Parse(last);
                if (condition(document.RootElement))
                {
                    return document.RootElement.Clone();
                }
            }

            await Task.Delay(100);
        }

        Assert.Fail($"Timed out waiting for {what}. Last state: {last}. Console: {string.Join(Environment.NewLine, console.TakeLast(30))}");
        return default;
    }
}
