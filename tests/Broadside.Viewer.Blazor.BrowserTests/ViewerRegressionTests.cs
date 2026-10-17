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

    private static async Task<IPage> OpenAsync(IBrowserContext context, string address)
    {
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(address);
        await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 0", null, Slow);
        return page;
    }
}
