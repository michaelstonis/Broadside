using System.Globalization;
using System.Text;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The representative sample documents (#363, samples/documents) in the published demo, in Chromium: each one in the gallery opens
/// and its first page presents with ink, then every page of every sample renders with the software rasterizer within a time budget.
/// The per-page times go to the measurement log as the browser column of docs/benchmarks/sample-documents-baseline.md.
/// </summary>
public sealed class SampleDocumentTests(BrowserHost host)
{
    /// <summary>The longest one page may take to render (AOT-compiled; the slowest page measured about 1 s on a CI runner).</summary>
    private static readonly TimeSpan PageBudget = TimeSpan.FromSeconds(20);

    /// <summary>The longest a gallery document may take to open and present its first page.</summary>
    private static readonly PageWaitForFunctionOptions OpenBudget = new() { Timeout = 90_000 };

    /// <summary>
    /// Every sample PDF the demo serves (samples/documents/pdf) that opens without a password, in the order of the baseline table. The
    /// two that need one (a user password, a certificate) are served but not listed: the demo has no password prompt.
    /// </summary>
    private static readonly string[] Samples =
    [
        "application-form-xfa.pdf", "application-form.pdf", "brochure.pdf", "catalogue.pdf", "color-guide.pdf", "contract-signed.pdf", "dictionary-aes256-gcm.pdf", "dictionary.pdf", "invoice-colourful.pdf",
        "invoice-conservative-filled.pdf", "invoice-conservative.pdf", "leaflet-multilingual.pdf", "newsletter.pdf", "notice-trilingual.pdf", "novel-hardback.pdf", "novel-large-print.pdf",
        "novel-pocket.pdf", "paper-ghostscript.pdf", "paper-rc4.pdf", "paper-reviewed.pdf", "paper.pdf", "rashomon-vertical.pdf", "report-1996.pdf", "scan-memo-g4.pdf", "scan-memo-jbig2.pdf",
        "scan-moon-memo-jpx.pdf", "showcase.pdf", "textbook-aes128.pdf",
        "textbook-linearized.pdf", "textbook.pdf",
    ];

    [Fact]
    public async Task Every_sample_document_opens_in_the_gallery_and_every_page_renders_within_budget()
    {
        BrowserHost.RequireApp();
        foreach (string sample in Samples)
        {
            Assert.True(File.Exists(Path.Combine(BrowserHost.AppDirectory!, "documents", sample)), $"the demo serves documents/{sample}");
        }

        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 900 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await WaitForDocumentAsync(page, "textbook.pdf");

        // Each gallery entry opens and presents its first page.
        await OpenGalleryPanelAsync(page);
        string[] gallery = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('[data-gallery]')].map(b => b.dataset.gallery)");
        foreach (string sample in Samples.Where(s => gallery.Contains("documents/" + s)))
        {
            await OpenGalleryPanelAsync(page);
            await page.Locator($"[data-gallery='documents/{sample}']").ClickAsync();
            await WaitForDocumentAsync(page, sample);
        }

        // Every page of every sample renders, each within the budget.
        var table = new StringBuilder();
        table.AppendLine("| Document | Pages | Total (ms) | Mean (ms) | Median (ms) | p95 (ms) | Slowest page (ms) |");
        table.AppendLine("|---|---:|---:|---:|---:|---:|---|");
        foreach (string sample in Samples)
        {
            string address = new Uri(host.AppAddress, "documents/" + sample).ToString();
            string result = await page.EvaluateAsync<string>(
                """
                async ([address, dpi]) => {
                  const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
                  return await exports.Broadside.Viewer.Blazor.Sample.RenderStats.RenderDocumentPagesAsync(address, dpi);
                }
                """,
                new object[] { address, 96 });
            double[] times = [.. result.Split(',').Select(static t => double.Parse(t, CultureInfo.InvariantCulture))];
            int slowest = Array.IndexOf(times, times.Max());
            Assert.True(times[slowest] < PageBudget.TotalMilliseconds, $"{sample} page {slowest + 1} took {times[slowest]:0} ms, over the {PageBudget.TotalSeconds:0} s budget");
            double[] sorted = [.. times.Order()];
            table.AppendLine(CultureInfo.InvariantCulture,
                $"| `{sample}` | {times.Length} | {times.Sum():0} | {times.Average():0.0} | {Percentile(sorted, 0.5):0.0} | {Percentile(sorted, 0.95):0.0} | {times[slowest]:0.0} (page {slowest + 1}) |");
        }

        Viewer.Log("Sample documents, every page at 96 DPI with the software rasterizer (second pass):" + Environment.NewLine + table);
    }

    private static async Task OpenGalleryPanelAsync(IPage page)
    {
        if (await page.Locator("[data-sheet=open]").CountAsync() == 0)
        {
            await page.Locator("[data-panel-button=open]").ClickAsync();
        }

        await page.Locator("[data-sheet=open]").WaitForAsync();
    }

    private static double Percentile(double[] sorted, double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];

    // The document is open and drawn: its name in the top bar, a presented tile and page ink on the canvas.
    private static async Task WaitForDocumentAsync(IPage page, string name)
    {
        await page.WaitForFunctionAsync("name => document.querySelector('.topbar .name')?.textContent === name", name, OpenBudget);
        await page.WaitForFunctionAsync(
            "() => { const v = document.querySelector('.bs-viewer'); return v && Number(v.dataset.pageCount) > 0 && Number(v.dataset.tilesPresented) > 0; }", null, OpenBudget);
        await page.WaitForFunctionAsync(HasInk, null, OpenBudget);
    }

    // Page ink: a pixel darker than 128 in every channel. Viewer.CountDarkPixels counts below 100, which the faded typewriting of
    // scan-moon-memo-jpx.pdf never reaches at 1x and 107 % (its darkest pixel is about 100/96/88 on a sepia page): that check only
    // passed while the canvas still held the previous document, and timed out on slower runners.
    private const string HasInk = """
        () => {
          const c = document.querySelector('.bs-viewer canvas');
          const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
          for (let i = 0; i < d.length; i += 4) if (d[i] < 128 && d[i + 1] < 128 && d[i + 2] < 128) return true;
          return false;
        }
        """;
}
