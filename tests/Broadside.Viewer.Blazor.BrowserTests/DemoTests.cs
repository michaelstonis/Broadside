using System.Globalization;
using Broadside.Forms;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// Every feature of the demo (#338) on a phone-sized touch screen in Chromium, WebKit and Firefox: search, thumbnails, the outline,
/// document information with the tag tree, annotations, form filling with the download of the edited file, rotation, signatures,
/// creating a PDF/A + PDF/UA document, a local file and a remote URL. With BROADSIDE_BROWSER_SCREENSHOTS set, Chromium saves the
/// screenshots the sample's README uses.
/// </summary>
public sealed class DemoTests(BrowserHost host)
{
    private static readonly PageWaitForFunctionOptions Slow = new() { Timeout = 120_000 };
    private static readonly PageWaitForFunctionOptions Normal = new() { Timeout = 30_000 };

    [Theory]
    [InlineData("chromium")]
    [InlineData("webkit")]
    [InlineData("firefox")]
    public async Task Every_feature_of_the_demo_works_on_a_phone(string browserName)
    {
        BrowserHost.RequireApp();
        IBrowser browser = await host.GetBrowserAsync(browserName);
        await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            DeviceScaleFactor = 3,
            HasTouch = true,
            AcceptDownloads = true,
        });
        IPage page = await context.NewPageAsync();
        bool screenshots = browserName == "chromium";
        await page.GotoAsync(host.AppAddress.ToString());
        await WaitForDocumentAsync(page, "tracemonkey.pdf", Slow);
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > window.innerWidth"), "nothing scrolls sideways at 390 px");

        // Search selects the first match in the viewer and lists the rest.
        await OpenPanelAsync(page, "search");
        await page.Locator("[data-input=search]").FillAsync("trace");
        await page.Locator("[data-input=search]").PressAsync("Enter");
        await page.WaitForFunctionAsync("() => Number(document.querySelector('[data-search-count]')?.dataset.searchCount) >= 3", null, Normal);
        await ScreenshotAsync(page, screenshots, "search");

        // Thumbnails are rendered pages.
        await OpenPanelAsync(page, "pages");
        await page.WaitForFunctionAsync("() => { const i = document.querySelector('[data-thumbnail]'); return i && i.complete && i.naturalWidth > 0; }", null, Normal);

        // The outline of a gallery document.
        await OpenGalleryAsync(page, "documents/outline-full.pdf", "outline-full.pdf", ink: false); // its pages are blank
        await OpenPanelAsync(page, "outline");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-outline]').length >= 3", null, Normal);
        await ScreenshotAsync(page, screenshots, "outline");

        // Document information, diagnostics and the tag tree.
        await OpenGalleryAsync(page, "documents/tagged-structure.pdf", "tagged-structure.pdf");
        await OpenPanelAsync(page, "info");
        await page.WaitForFunctionAsync("() => /Tagged\\s*yes/.test(document.querySelector('[data-info=document]')?.innerText ?? '')", null, Normal);
        await page.Locator("[data-info-tab=Diagnostics]").ClickAsync();
        await page.Locator("[data-diagnostic-count]").WaitForAsync();
        await page.Locator("[data-info-tab=Tags]").ClickAsync();
        await page.WaitForFunctionAsync("() => Number(document.querySelector('[data-tag-count]')?.dataset.tagCount) > 0", null, Normal);
        await page.Locator(".tags button[aria-label^=Expand]").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tags [data-tag]').length > 1", null, Normal);
        await ScreenshotAsync(page, screenshots, "tags");

        // Annotations: add a note and a stamp through the annotation layer.
        await OpenGalleryAsync(page, "documents/annotations-link.pdf", "annotations-link.pdf", ink: false); // blue link text only
        await OpenPanelAsync(page, "annotate");
        int before = await CountAsync(page, "annotationCount");
        await page.Locator("[data-action=add-note]").ClickAsync();
        await page.Locator("[data-action=add-stamp]").ClickAsync();
        await page.WaitForFunctionAsync($"() => Number(document.querySelector('[data-annotation-count]')?.dataset.annotationCount) === {before + 2}", null, Normal);
        await page.Locator(".topbar .edited").WaitForAsync();
        await ScreenshotAsync(page, screenshots, "annotate");

        // Form filling, then the download carries the value.
        await OpenGalleryAsync(page, "documents/acroform-fields.pdf", "acroform-fields.pdf");
        await OpenPanelAsync(page, "form");
        ILocator name = page.Locator("[data-field=name]");
        await name.FillAsync("Filled in the demo");
        await name.DispatchEventAsync("change");
        await page.Locator(".topbar .edited").WaitForAsync();
        await ScreenshotAsync(page, screenshots, "form");
        IDownload download = await page.RunAndWaitForDownloadAsync(() => page.Locator("[data-action=download]").ClickAsync());
        Assert.Equal("acroform-fields.pdf", download.SuggestedFilename);
        byte[] saved = await ReadAsync(download);
        using (PdfDocument document = PdfDocument.Open(saved))
        {
            PdfTextField field = Assert.IsType<PdfTextField>(document.AcroForm!.TerminalFields.Single(f => f.FullyQualifiedName == "name"));
            Assert.Equal("Filled in the demo", field.Value);
        }

        // A view rotation.
        await OpenPanelAsync(page, "view");
        await page.Locator("[data-action=rotate-right]").ClickAsync();
        await page.Locator("[data-rotation='90']").WaitForAsync();
        await page.WaitForFunctionAsync(Viewer.CountDarkPixels.Replace("return n;", "return n > 0;", StringComparison.Ordinal), null, Normal);

        // Signatures: validated where the runtime has X.509 support, otherwise the signature dictionary is shown unchecked.
        await OpenGalleryAsync(page, "documents/signed.pdf", "signed.pdf");
        await OpenPanelAsync(page, "signatures");
        await page.Locator("[data-signature-status]").WaitForAsync();
        Assert.Contains("Signature1", await page.Locator(".signatures").InnerTextAsync(), StringComparison.Ordinal);
        await ScreenshotAsync(page, screenshots, "signatures");

        // Create a PDF/A-2u + PDF/UA-1 document with the layout package; it opens in the viewer.
        await OpenPanelAsync(page, "create");
        await page.Locator("[data-action=create]").ClickAsync();
        await page.Locator("[data-conformance=ok]").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
        await WaitForDocumentAsync(page, "created.pdf", Normal);
        await ScreenshotAsync(page, screenshots, "create");
        IDownload created = await page.RunAndWaitForDownloadAsync(() => page.Locator("[data-action=download-created]").ClickAsync());
        using (PdfDocument document = PdfDocument.Open(await ReadAsync(created)))
        {
            Assert.Equal(1, document.Metadata!.Packet!.PdfUAPart);
            Assert.Equal(2, document.Metadata.Packet.PdfAPart);
        }

        // A local file, read in the browser.
        await OpenPanelAsync(page, "open");
        string local = Path.Combine(AppContext.BaseDirectory, "Corpus", "layout-two-column-untagged.pdf");
        await page.Locator("[data-input=file]").SetInputFilesAsync(local);
        await WaitForDocumentAsync(page, "layout-two-column-untagged.pdf", Normal);

        // A remote URL, read through range requests.
        host.Serve("demo-remote.pdf", await File.ReadAllBytesAsync(local, TestContext.Current.CancellationToken));
        await OpenPanelAsync(page, "open");
        await page.Locator("[data-input=url]").FillAsync(new Uri(host.BaseAddress, "files/demo-remote.pdf").ToString());
        await page.Locator("[data-input=url]").PressAsync("Enter");
        await WaitForDocumentAsync(page, "demo-remote.pdf", Normal);
        await Viewer.LogFramesAsync(page, $"demo walkthrough ({browserName})");
    }

    private static async Task OpenPanelAsync(IPage page, string panel)
    {
        ILocator button = page.Locator($"[data-panel-button={panel}]");
        if (await page.Locator($"[data-sheet={panel}]").CountAsync() == 0)
        {
            await button.ClickAsync();
        }

        await page.Locator($"[data-sheet={panel}]").WaitForAsync();
    }

    private static async Task OpenGalleryAsync(IPage page, string file, string name, bool ink = true)
    {
        await OpenPanelAsync(page, "open");
        await page.Locator($"[data-gallery='{file}']").ClickAsync();
        await WaitForDocumentAsync(page, name, Normal, ink);
    }

    // The document is open and drawn: its name in the top bar and page ink on the canvas.
    private static async Task WaitForDocumentAsync(IPage page, string name, PageWaitForFunctionOptions options, bool ink = true)
    {
        await page.WaitForFunctionAsync("name => document.querySelector('.topbar .name')?.textContent === name", name, options);
        await page.WaitForFunctionAsync(
            "() => { const v = document.querySelector('.bs-viewer'); return v && Number(v.dataset.pageCount) > 0 && Number(v.dataset.tilesPresented) > 0; }", null, options);
        if (ink)
        {
            await page.WaitForFunctionAsync(Viewer.CountDarkPixels.Replace("return n;", "return n > 0;", StringComparison.Ordinal), null, options);
        }
    }

    private static async Task<int> CountAsync(IPage page, string name)
    {
        await page.Locator($"[data-{Kebab(name)}]").WaitForAsync();
        return int.Parse(await page.EvaluateAsync<string>($"() => document.querySelector('[data-{Kebab(name)}]').dataset.{name}"), CultureInfo.InvariantCulture);
    }

    private static string Kebab(string name) => string.Concat(name.Select(static c => char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : c.ToString()));

    private static async Task<byte[]> ReadAsync(IDownload download)
    {
        await using Stream stream = (await download.CreateReadStreamAsync())!;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    /// <summary>Saves a phone screenshot when BROADSIDE_BROWSER_SCREENSHOTS names a folder (the README's pictures).</summary>
    private static async Task ScreenshotAsync(IPage page, bool enabled, string name)
    {
        if (enabled && Environment.GetEnvironmentVariable("BROADSIDE_BROWSER_SCREENSHOTS") is { Length: > 0 } folder)
        {
            await page.WaitForTimeoutAsync(600);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(folder, $"demo-{name}.png"), Scale = ScreenshotScale.Css });
        }
    }
}
