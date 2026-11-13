using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>The render worker (#357): content tiles drawn by a second runtime in a Web Worker, on a host without cross-origin isolation.</summary>
public sealed class RenderWorkerTests(BrowserHost host)
{
    private static readonly PageWaitForFunctionOptions Slow = new() { Timeout = 120_000 };

    [Fact]
    public async Task The_demo_draws_content_tiles_in_a_worker_without_cross_origin_isolation()
    {
        BrowserHost.RequireApp();
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 900, Height = 900 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 2", null, Slow);

        // A plain static host: no COOP/COEP headers, so no SharedArrayBuffer and no WebAssembly threads; the worker needs neither.
        Assert.False(await page.EvaluateAsync<bool>("() => globalThis.crossOriginIsolated === true"));
        Assert.Equal("yes", await Viewer.Data(page, "bsWorker"));

        // The page is drawn: dark text on white paper at the top of page 1 (pixels that came from the worker's ImageBitmaps).
        int[] counts = await page.EvaluateAsync<int[]>("""
            () => {
              const canvas = document.querySelector('.bs-viewer canvas');
              const d = canvas.getContext('2d').getImageData(0, 0, canvas.width, Math.floor(canvas.height / 2)).data;
              let dark = 0, white = 0;
              for (let i = 0; i < d.length; i += 16) {
                if (d[i] < 96 && d[i + 1] < 96 && d[i + 2] < 96) dark++;
                if (d[i] > 250 && d[i + 1] > 250 && d[i + 2] > 250) white++;
              }
              return [dark, white];
            }
            """);
        Assert.True(counts[0] > 30, $"dark samples: {counts[0]}");
        Assert.True(counts[1] > counts[0] * 3, $"white samples: {counts[1]}");
    }

    [Fact]
    public async Task Without_the_worker_the_viewer_draws_every_tile_itself()
    {
        BrowserHost.RequireApp();
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 900, Height = 900 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(host.AppAddress, "?worker=0").ToString());
        await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 2", null, Slow);
        Assert.Equal("no", await Viewer.Data(page, "bsWorker"));
    }
}
