using System.Text.Json;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>Regressions the owner found in the hosted demo that only a browser shows (issues #346, #349, #350).</summary>
public sealed class ViewerRegressionTests(BrowserHost host)
{
    private static readonly PageWaitForFunctionOptions Slow = new() { Timeout = 120_000 };

    [Fact]
    public async Task Resizing_the_viewer_never_shows_a_cleared_black_canvas()
    {
        // Issue #346: setting a canvas's size clears it to black (it has no alpha) until the next animation frame redraws it.
        BrowserHost.RequireApp();
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 800 } });
        IPage page = await OpenAsync(context, host.AppAddress.ToString());
        // Created after the viewer's own observer, so it runs after the viewer resized the canvas, before the browser paints.
        await page.EvaluateAsync("""
            () => {
              const host = document.querySelector('.bs-viewer');
              const canvas = host.querySelector('canvas');
              const before = canvas.width;
              window.bsResized = new Promise(resolve => {
                const observer = new ResizeObserver(() => {
                  if (canvas.width === before) return;
                  observer.disconnect();
                  const d = canvas.getContext('2d').getImageData(1, 1, 1, 1).data;
                  resolve([d[0], d[1], d[2]]);
                });
                observer.observe(host);
                setTimeout(() => resolve(null), 20000);
              });
            }
            """);
        await page.SetViewportSizeAsync(800, 700); // a window resize or a phone turning
        int[]? pixel = await page.EvaluateAsync<int[]?>("() => window.bsResized");
        Assert.NotNull(pixel);
        Assert.NotEqual([0, 0, 0], pixel);
    }

    [Fact]
    public async Task Scrolling_pages_into_view_moves_no_focus_onto_the_page_content()
    {
        // Issue #349: the accessible focus follows the viewer's page, but scrolling must not pull the keyboard focus (and a focus ring)
        // onto the text layer of a pointer user.
        BrowserHost.RequireApp();
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 800 } });
        IPage page = await OpenAsync(context, host.AppAddress.ToString());
        LocatorBoundingBoxResult box = (await page.Locator(".bs-viewer").BoundingBoxAsync())!;
        await page.Mouse.ClickAsync(box.X + 2, box.Y + 2); // the grey margin: focuses the viewer without selecting text
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        for (int i = 0; i < 12 && await Viewer.Data(page, "currentPage") is not ("3" or "4" or "5"); i++)
        {
            await page.Mouse.WheelAsync(0, 600);
            await page.WaitForTimeoutAsync(150);
        }

        await page.WaitForSelectorAsync(".bs-a11y section[data-bs-page='2'] [data-bs-node]", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(500);
        Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement === document.querySelector('.bs-viewer')"), "the viewer keeps the focus");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.querySelectorAll('.bs-a11y :focus-visible').length"));
    }

    [Fact]
    public async Task Links_show_when_pressed_follow_internal_destinations_and_ask_before_leaving_the_document()
    {
        // Issue #350: a tap on a link gave no feedback. Page 1's top half: a URI link on the left, a link to page 4 on the right.
        BrowserHost.RequireApp();
        host.Serve("links.pdf", LinkedDocument());
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 800 } });
        IPage page = await OpenAsync(context, new Uri(host.AppAddress, "?file=../files/links.pdf").ToString());
        LocatorBoundingBoxResult box = (await page.Locator(".bs-viewer").BoundingBoxAsync())!;
        (float X, float Y) external = (box.X + (box.Width * 0.3f), box.Y + 120);
        (float X, float Y) inside = (box.X + (box.Width * 0.7f), box.Y + 120);

        // Hovered under a mouse once the page's links have loaded.
        for (int i = 0; i < 200 && await Viewer.Data(page, "linkState") != "hover"; i++)
        {
            await page.Mouse.MoveAsync(external.X + (i % 2), external.Y);
            await page.WaitForTimeoutAsync(100);
        }

        string state = await page.EvaluateAsync<string>(
            "() => { const v = document.querySelector('.bs-viewer'); return JSON.stringify({ cursor: v.style.cursor, data: { ...v.dataset }, message: document.querySelector('.message')?.innerText ?? null, sections: document.querySelectorAll('.bs-a11y section').length }); }");
        Assert.True(await Viewer.Data(page, "linkState") == "hover", state);
        await page.Mouse.DownAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.bs-viewer').dataset.linkState === 'pressed'", null, Slow);
        await page.Mouse.UpAsync();
        ILocator sheet = page.Locator("[data-link-sheet]");
        await sheet.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        Assert.Equal("https://example.com/", await sheet.Locator(".link-open").GetAttributeAsync("href"));
        Assert.Single(context.Pages); // never opened by itself
        await sheet.Locator(".link-cancel").ClickAsync();
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 30_000 });

        await page.Mouse.ClickAsync(inside.X, inside.Y);
        await page.WaitForFunctionAsync("() => document.querySelector('.bs-viewer').dataset.currentPage === '4'", null, Slow);
        Assert.Equal(0, await page.Locator("[data-link-sheet]").CountAsync());
    }

    [Fact]
    public async Task A_banner_appearing_in_the_middle_of_a_jump_to_the_last_page_does_not_stop_it()
    {
        // Issue #416: a resize stopped a running page jump, so a banner or toolbar appearing (or a phone's address bar collapsing)
        // left the viewer partway. A 40-page document; a 150 px banner is inserted above the viewer 60 ms after End is pressed.
        BrowserHost.RequireApp();
        host.Serve("forty-pages.pdf", BlankPages(40));
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 800 } });
        IPage page = await OpenAsync(context, new Uri(host.AppAddress, "?file=../files/forty-pages.pdf").ToString());
        Assert.Equal("40", await Viewer.Data(page, "pageCount"));
        Assert.Equal(0, await page.Locator(".message").CountAsync());
        await page.EvaluateAsync("""
            () => {
              const viewer = document.querySelector('.bs-viewer');
              window.bsBanner = new Promise(resolve => {
                document.addEventListener('keydown', () => setTimeout(() => {
                  const before = viewer.clientHeight;
                  const page = viewer.dataset.currentPage;
                  const banner = document.createElement('div');
                  banner.className = 'test-banner';
                  banner.style.height = '150px';
                  banner.style.flex = 'none';
                  document.querySelector('.app').insertBefore(banner, document.querySelector('.workspace'));
                  requestAnimationFrame(() => resolve({ page, before, after: viewer.clientHeight }));
                }, 60), { capture: true, once: true });
              });
            }
            """);

        await page.Locator(".bs-viewer").FocusAsync();
        await page.Keyboard.PressAsync("End");
        JsonElement banner = await page.EvaluateAsync<JsonElement>("() => window.bsBanner");
        Viewer.Log($"banner at page {banner.GetProperty("page")}, viewer {banner.GetProperty("before")} -> {banner.GetProperty("after")} px");
        Assert.True(banner.GetProperty("after").GetDouble() < banner.GetProperty("before").GetDouble(), "the banner shrank the viewer");

        await page.WaitForFunctionAsync("() => document.querySelector('.bs-viewer').dataset.currentPage === '40'", null, Slow);
        await page.WaitForTimeoutAsync(500);
        Assert.Equal("40", await Viewer.Data(page, "currentPage"));
        Assert.Equal(0, await page.Locator(".message").CountAsync());
    }

    private static byte[] BlankPages(int count)
    {
        using PdfDocument document = PdfDocument.Create();
        while (document.Pages.Count < count)
        {
            document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static byte[] LinkedDocument()
    {
        using PdfDocument document = PdfDocument.Create();
        while (document.Pages.Count < 5)
        {
            document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        }

        PdfPage first = document.Pages[0];

        first.AddLinkAnnotation(new PdfRectangle(0, 396, 306, 792), new Uri("https://example.com/"));
        first.AddLinkAnnotation(new PdfRectangle(306, 396, 612, 792), document.Pages[3]);
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task A_mouse_release_away_from_the_press_selects_up_to_it_when_no_move_arrived_between()
    {
        // Issue #558: a fast drag can end before a move reaches the viewer at the release point; the release still ends the selection
        // there. The press and the release are dispatched with no pointermove between them (a real mouse always moves to the release).
        BrowserHost.RequireApp();
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 800 } });
        IPage page = await OpenAsync(context, host.AppAddress.ToString());
        await page.WaitForSelectorAsync(".bs-a11y section[data-bs-page='0'] [data-bs-node]", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 120_000 });

        // A line of text on page 1: the widest accessible leaf node, which the viewer places over its glyphs.
        float[] line = await page.EvaluateAsync<float[]>("""
            () => {
              const viewer = document.querySelector('.bs-viewer').getBoundingClientRect();
              let best = null;
              for (const node of document.querySelectorAll(".bs-a11y section[data-bs-page='0'] [data-bs-node]")) {
                if (node.querySelector('[data-bs-node]') || node.textContent.trim().length < 8) continue;
                const r = node.getBoundingClientRect();
                if (r.top < viewer.top || r.bottom > viewer.bottom || r.left < viewer.left || r.right > viewer.right) continue;
                if (!best || r.width > best.width) best = r;
              }
              return best ? [best.left, best.top + (best.height / 2), best.right] : [];
            }
            """);
        Assert.Equal(3, line.Length);
        (float left, float y, float right) = (line[0], line[1], line[2]);

        // Hover until the viewer shows the text cursor there: the page's text is loaded, so a press selects instead of panning.
        for (int i = 0; i < 200 && await page.EvaluateAsync<string>("() => document.querySelector('.bs-viewer').style.cursor") != "text"; i++)
        {
            await page.Mouse.MoveAsync(left + 2 + (i % 2), y);
            await page.WaitForTimeoutAsync(100);
        }

        string copied = await page.EvaluateAsync<string>("""
            ({ left, y, right }) => {
              const host = document.querySelector('.bs-viewer');
              host.setPointerCapture = () => {}; // the synthetic pointer is not one the browser can capture
              const pointer = (type, x) => host.dispatchEvent(new PointerEvent(type, {
                pointerId: 7, pointerType: 'mouse', isPrimary: true, button: 0, buttons: type === 'pointerdown' ? 1 : 0, detail: 1,
                clientX: x, clientY: y, bubbles: true, cancelable: true }));
              pointer('pointerdown', left + 2);
              pointer('pointerup', right - 2);
              const data = new DataTransfer();
              host.dispatchEvent(new ClipboardEvent('copy', { clipboardData: data, bubbles: true, cancelable: true }));
              return data.getData('text/plain');
            }
            """, new { left = (double)left, y = (double)y, right = (double)right });
        Assert.False(string.IsNullOrWhiteSpace(copied), "the release selected nothing");
        Viewer.Log($"press-release with no move between selected \"{copied}\"");
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context, string address)
    {
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(address);
        await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 0", null, Slow);
        return page;
    }
}
