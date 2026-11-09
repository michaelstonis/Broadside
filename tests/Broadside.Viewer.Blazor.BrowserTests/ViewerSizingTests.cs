using System.Text.Json;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// Issue #393: the viewer's children are all absolutely positioned, so without host CSS its element was 0 px tall and showed nothing.
/// It now has an intrinsic height that any size the host sets overrides.
/// </summary>
public sealed class ViewerSizingTests(BrowserHost host)
{
    [Fact]
    public async Task A_viewer_without_host_css_shows_page_one_at_a_default_height()
    {
        BrowserHost.RequireApp();
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 900 } });
        IPage page = await context.NewPageAsync();

        // Without the demo's stylesheet the PdfViewer sits in plain block flow, as in an app that has no CSS for it.
        await page.RouteAsync("**/css/app.css", route => route.AbortAsync());
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 120_000 });

        double height = await page.EvaluateAsync<double>("() => document.querySelector('.bs-viewer').getBoundingClientRect().height");
        Assert.Equal(600, height, 0.5); // min(80vh, 600px) at 900 px
        Assert.True(await page.EvaluateAsync<int>(Viewer.CountDarkPixels) > 0, "page 1 has ink");
    }

    [Fact]
    public async Task Sizes_the_host_sets_override_the_default_height()
    {
        BrowserHost.RequireApp();
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 900 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 120_000 });

        // The demo's flex layout (.viewer { flex: 1; min-height: 0 }) fills the window below the top bar, not the default height.
        double[] demo = await page.EvaluateAsync<double[]>(
            """
            () => {
              const viewer = document.querySelector('.bs-viewer');
              const parent = viewer.parentElement.getBoundingClientRect();
              const own = viewer.getBoundingClientRect();
              return [own.height, parent.bottom - own.bottom];
            }
            """);
        Assert.NotEqual(600, demo[0], 0.5);
        Assert.Equal(0, demo[1], 0.5);

        // Each way a host sizes an element, with the viewer's injected stylesheet in the page: an explicit height (also below the
        // default), a class rule, flex: 1 with min-height: 0 in a short column, insets, and a grid row.
        string json = await page.EvaluateAsync<string>(
            """
            () => {
              const style = document.createElement('style');
              style.textContent = '.host-height { height: 120px; }';
              document.head.append(style);
              const measure = (containerCss, viewerCss, className) => {
                const container = document.createElement('div');
                container.style.cssText = containerCss;
                const viewer = document.createElement('div');
                viewer.className = 'bs-viewer' + (className ? ' ' + className : '');
                viewer.style.cssText = viewerCss;
                container.append(viewer);
                document.body.append(container);
                const height = viewer.getBoundingClientRect().height;
                container.remove();
                return height;
              };
              return JSON.stringify({
                bare: measure('', '', ''),
                height: measure('', 'height: 200px', ''),
                small: measure('', 'height: 50px', ''),
                rule: measure('', '', 'host-height'),
                flex: measure('display: flex; flex-direction: column; height: 150px', 'flex: 1; min-height: 0', ''),
                inset: measure('position: relative; height: 170px', 'position: absolute; inset: 0', ''),
                grid: measure('display: grid; grid-template-rows: 180px', 'min-height: 0', ''),
              });
            }
            """);
        Viewer.Log($"sizing: {json}");
        Dictionary<string, double> heights = JsonSerializer.Deserialize<Dictionary<string, double>>(json)!;
        Assert.Equal(600, heights["bare"], 0.5);
        Assert.Equal(200, heights["height"], 0.5);
        Assert.Equal(50, heights["small"], 0.5);
        Assert.Equal(120, heights["rule"], 0.5);
        Assert.Equal(150, heights["flex"], 0.5);
        Assert.Equal(170, heights["inset"], 0.5);
        Assert.Equal(180, heights["grid"], 0.5);
    }
}
