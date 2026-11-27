using System.Globalization;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The <c>PdfToolbar</c> component bound to the sample's real <c>PdfViewer</c> (#632, in the View panel): page and zoom round trips
/// through its buttons, page box and zoom picker, read back from the sample's top bar.
/// </summary>
public sealed class PdfToolbarTests(BrowserHost host)
{
    private static readonly PageWaitForFunctionOptions Wait = new() { Timeout = 120_000 };

    [Fact]
    public async Task The_toolbar_moves_and_zooms_the_viewer()
    {
        BrowserHost.RequireApp();
        IBrowser browser = await host.GetBrowserAsync("chromium");
        await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 900 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync(
            "() => { const v = document.querySelector('.bs-viewer'); return v && Number(v.dataset.pageCount) > 2 && Number(v.dataset.tilesPresented) > 0; }", null, Wait);

        await page.Locator("[data-panel-button=view]").ClickAsync();
        ILocator toolbar = page.Locator("[data-sheet=view] .bs-toolbar");
        await toolbar.WaitForAsync();

        // The next-page button, then a page typed into the page box.
        await toolbar.Locator("[data-command='page.next']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.topbar .page')?.dataset.page === '1'", null, Wait);
        await page.WaitForFunctionAsync("() => document.querySelector(\"[data-command='page.number'] input\")?.value === '2'", null, Wait);
        ILocator box = toolbar.Locator("[data-command='page.number'] input");
        await box.FillAsync("3");
        await box.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => document.querySelector('.topbar .page')?.dataset.page === '2'", null, Wait);

        // Zoom in, then the 200 % preset (the fit modes come first: index 3 + 5).
        string before = await page.Locator(".topbar .percent").InnerTextAsync();
        await toolbar.Locator("[data-command='zoom.in']").ClickAsync();
        await page.WaitForFunctionAsync("before => document.querySelector('.topbar .percent')?.textContent !== before", before, Wait);
        await toolbar.Locator("select[data-command='zoom.mode']").SelectOptionAsync((3 + 5).ToString(CultureInfo.InvariantCulture));
        await page.WaitForFunctionAsync("() => document.querySelector('.topbar .percent')?.textContent === '200%'", null, Wait);
    }
}
