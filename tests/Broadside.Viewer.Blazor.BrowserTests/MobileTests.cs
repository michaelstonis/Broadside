using System.Globalization;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>The sample on a phone (Playwright's Pixel 7 profile: touch, 2.625 device pixel ratio): it loads, and two fingers pinch-zoom.</summary>
public sealed class MobileTests(BrowserHost host)
{
    [Fact]
    public async Task The_sample_loads_on_a_phone_and_pinch_zooms()
    {
        BrowserHost.RequireApp();
        BrowserNewContextOptions phone = host.Playwright.Devices["Pixel 7"];
        await using IBrowserContext context = await host.Browser.NewContextAsync(phone);
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync(
            "() => { const v = document.querySelector('.bs-viewer'); return v && Number(v.dataset.tilesPresented) > 0; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 120_000 });
        Assert.True(await page.EvaluateAsync<int>(Viewer.CountDarkPixels) > 0, "the gallery's first document is drawn");
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > window.innerWidth"), "the toolbar fits the phone's width");

        await ScreenshotAsync(page, "phone-loaded.png");
        double before = await ZoomAsync(page);
        LocatorBoundingBoxResult box = (await page.Locator(".bs-viewer").BoundingBoxAsync())!;
        double cx = box.X + (box.Width / 2);
        double cy = box.Y + (box.Height * 0.22); // over the text of the gallery's first page
        ICDPSession cdp = await context.NewCDPSessionAsync(page);
        await TouchAsync(cdp, "touchStart", (cx - 40, cy), (cx + 40, cy));
        for (int step = 1; step <= 10; step++)
        {
            await TouchAsync(cdp, "touchMove", (cx - 40 - (step * 8), cy), (cx + 40 + (step * 8), cy));
            await page.WaitForTimeoutAsync(16);
        }

        await TouchAsync(cdp, "touchEnd");
        await page.WaitForFunctionAsync(
            "before => Number(document.querySelector('.bs-viewer').dataset.zoom) > before * 1.5",
            before,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        double after = await ZoomAsync(page);
        Viewer.Log($"pinch: zoom {before:0.###} -> {after:0.###} of actual size");
        await Viewer.LogFramesAsync(page, "phone");
        await page.WaitForTimeoutAsync(500);
        await ScreenshotAsync(page, "phone-pinched.png");
    }

    /// <summary>Saves a screenshot when BROADSIDE_BROWSER_SCREENSHOTS names a folder (to look at the phone layout).</summary>
    private static async Task ScreenshotAsync(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("BROADSIDE_BROWSER_SCREENSHOTS") is { Length: > 0 } folder)
        {
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(folder, name) });
        }
    }

    private static async Task<double> ZoomAsync(IPage page) =>
        double.Parse((await Viewer.Data(page, "zoom"))!, CultureInfo.InvariantCulture);

    private static async Task TouchAsync(ICDPSession cdp, string type, params (double X, double Y)[] points) =>
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = points.Select(static (point, id) => new Dictionary<string, object> { ["x"] = point.X, ["y"] = point.Y, ["id"] = id }).ToArray(),
        });
}
