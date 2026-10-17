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
        int[] pixel = await page.EvaluateAsync<int[]>("""
            () => new Promise(resolve => {
              const host = document.querySelector('.bs-viewer');
              const canvas = host.querySelector('canvas');
              const before = canvas.width;
              // Created after the viewer's own observer, so it runs after the viewer resized the canvas, before the browser paints.
              const observer = new ResizeObserver(() => {
                if (canvas.width === before) return;
                observer.disconnect();
                const d = canvas.getContext('2d').getImageData(1, 1, 1, 1).data;
                resolve([d[0], d[1], d[2]]);
              });
              observer.observe(host);
              host.style.width = (host.getBoundingClientRect().width - 37) + 'px';
            })
            """);
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

        Assert.Equal("hover", await Viewer.Data(page, "linkState"));
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

    private static byte[] LinkedDocument()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage first = document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        for (int i = 1; i < 5; i++)
        {
            document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        }

        first.AddLinkAnnotation(new PdfRectangle(0, 396, 306, 792), new Uri("https://example.com/"));
        first.AddLinkAnnotation(new PdfRectangle(306, 396, 612, 792), document.Pages[3]);
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context, string address)
    {
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(address);
        await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 0", null, Slow);
        return page;
    }
}
