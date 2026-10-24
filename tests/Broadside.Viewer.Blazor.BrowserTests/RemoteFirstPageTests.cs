using System.Globalization;
using System.Text;
using Broadside.Objects;
using Broadside.Tests.Document;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The sample opens a remote linearized file and shows page 1 before the download completes (#221 acceptance 1): the server holds
/// every request past the first page's end (the linearization dictionary's E) until page 1 is on the canvas.
/// </summary>
public sealed class RemoteFirstPageTests(BrowserHost host)
{
    private const int PageCount = 30;
    private const int PagePadding = 40 * 1024;

    [Theory]
    [InlineData("chromium")]
    [InlineData("webkit")]
    [InlineData("firefox")]
    public async Task Page_one_of_a_remote_linearized_file_shows_before_the_download_completes(string browserName)
    {
        BrowserHost.RequireApp();
        IBrowser browser = await host.GetBrowserAsync(browserName);
        (byte[] file, long firstPageEnd) = LargeLinearized();
        string name = $"large-linearized-{browserName}.pdf";
        HostedDocument document = host.Serve(name, file, gateFrom: firstPageEnd);
        await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 800 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(host.AppAddress, $"?file=../files/{name}").ToString());

        await page.WaitForFunctionAsync(
            "() => { const v = document.querySelector('.bs-viewer'); return v && Number(v.dataset.tilesPresented) > 0 && Number(v.dataset.pageCount) > 0; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 120_000 });
        int dark = await page.EvaluateAsync<int>(Viewer.CountDarkPixels);
        long served = Interlocked.Read(ref document.Served);
        Viewer.Log(
            $"{browserName}: file {file.Length} B, first page ends at {firstPageEnd}, served {served} B before page 1 showed; {Volatile.Read(ref document.Held)} request(s) held");

        Assert.Equal(PageCount.ToString(CultureInfo.InvariantCulture), await Viewer.Data(page, "pageCount"));
        Assert.True(dark > 0, "page 1 has ink on the canvas");
        Assert.True(served < file.Length / 2, $"served {served} of {file.Length} bytes");

        // Page 1's text layer (its selectable text and accessible tree) loads from the first-page section too, while the rest of the
        // file is still held, and without an error: reading a page's layout used to walk the page tree, which needs the main
        // cross-reference section at the end of the file (#410). The sample showed that error as a message above the viewer, whose
        // resize then stopped the jump to the last page below, at whatever page it had reached.
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.bs-a11y section') !== null || document.querySelector('.message') !== null",
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });
        Assert.Null(await page.EvaluateAsync<string?>("() => document.querySelector('.message')?.innerText ?? null"));

        // The rest of the file arrives once the gate opens: the last page becomes reachable.
        document.Release();
        await page.Locator(".bs-viewer").FocusAsync();
        await page.Keyboard.PressAsync("End");
        await page.WaitForFunctionAsync(
            $"() => document.querySelector('.bs-viewer').dataset.currentPage === '{PageCount}'",
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });
        for (int i = 0; i < 10; i++)
        {
            await page.Keyboard.PressAsync("PageUp");
        }

        await page.WaitForTimeoutAsync(1500);
        await Viewer.LogFramesAsync(page, $"remote 30-page linearized file ({browserName})");
        Assert.Null(await page.EvaluateAsync<string?>("() => document.querySelector('.message')?.innerText ?? null"));
    }

    /// <summary>
    /// A 30-page file linearized by the writer (#228): each page shows "Page N" over a 10 pt black square and its content stream is
    /// padded with comments to 40 KiB, so the file is about 1.2 MB and page 1 is a small part of it; the last page is landscape, so
    /// its real size replaces page 1's placeholder size once it arrives.
    /// </summary>
    private static (byte[] File, long FirstPageEnd) LargeLinearized()
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(' ', Enumerable.Range(0, PageCount).Select(static page => $"{4 + (2 * page)} 0 R")) + $"] /Count {PageCount} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        for (int page = 0; page < PageCount; page++)
        {
            string box = page == PageCount - 1 ? "[0 0 792 612]" : "[0 0 612 792]";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox {box} /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + (2 * page)} 0 R >>");
            var content = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"72 500 40 40 re f BT /F1 36 Tf 72 560 Td (Page {page + 1}) Tj ET\n"));
            while (content.Length < PagePadding)
            {
                content.Append('%').Append('x', 78).Append('\n');
            }

            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
        }

        using PdfDocument source = PdfDocument.Open(new TestPdf().Build([.. objects]));
        using var stream = new MemoryStream();
        source.Save(stream, new PdfSaveOptions().WithLinearize(true).WithStreamCompression(PdfStreamCompression.None));
        byte[] file = stream.ToArray();
        using PdfDocument local = PdfDocument.Open(file);
        long end = ((CosInteger)local.Linearization!.Dictionary[new CosName("E")]).Value;
        return (file, end);
    }
}
