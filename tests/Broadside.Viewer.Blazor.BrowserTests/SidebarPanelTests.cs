using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The <c>PdfSidebar</c> component bound to the sample's real <c>PdfViewer</c> (#636, in the View panel): a thumbnail click moves the
/// viewer, read back from the sample's top bar, and the tabs switch panels.
/// </summary>
public sealed class SidebarPanelTests(BrowserHost host)
{
    private static readonly PageWaitForFunctionOptions Wait = new() { Timeout = 120_000 };

    [Fact]
    public async Task A_thumbnail_moves_the_viewer_and_the_tabs_switch_panels()
    {
        BrowserHost.RequireApp();
        IBrowser browser = await host.GetBrowserAsync("chromium");
        await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 900 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync(
            "() => { const v = document.querySelector('.bs-viewer'); return v && Number(v.dataset.pageCount) > 2 && Number(v.dataset.tilesPresented) > 0; }", null, Wait);

        await page.Locator("[data-panel-button=view]").ClickAsync();
        ILocator sidebar = page.Locator("[data-sheet=view] .bs-sidebar");
        await sidebar.WaitForAsync();

        await sidebar.Locator("[data-thumbnail='2']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.topbar .page')?.dataset.page === '2'", null, Wait);
        await page.WaitForFunctionAsync("() => document.querySelector(\"[data-sheet=view] [data-thumbnail='2']\")?.getAttribute('aria-current') === 'page'", null, Wait);

        await sidebar.Locator("[data-tab=layers]").ClickAsync();
        await sidebar.Locator("[data-panel=layers]").WaitForAsync();
        await sidebar.Locator("[data-tab=annotations]").ClickAsync();
        await sidebar.Locator("[data-panel=annotations]").WaitForAsync();
    }
}
