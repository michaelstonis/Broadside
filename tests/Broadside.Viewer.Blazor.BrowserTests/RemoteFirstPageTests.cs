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

    [Fact]
    public async Task Page_one_of_a_remote_linearized_file_shows_before_the_download_completes()
    {
        BrowserHost.RequireApp();
        (byte[] file, long firstPageEnd) = LargeLinearized();
        HostedDocument document = host.Serve("large-linearized.pdf", file, gateFrom: firstPageEnd);
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = 800 } });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(host.AppAddress, "?file=../files/large-linearized.pdf").ToString());

        await page.WaitForFunctionAsync(
            "() => { const v = document.querySelector('.bs-viewer'); return v && Number(v.dataset.tilesPresented) > 0 && Number(v.dataset.pageCount) > 0; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 120_000 });
        int dark = await page.EvaluateAsync<int>(Viewer.CountDarkPixels);
        long served = Interlocked.Read(ref document.Served);
        Viewer.Log(
            $"file {file.Length} B, first page ends at {firstPageEnd}, served {served} B before page 1 showed; {Volatile.Read(ref document.Held)} request(s) held");

        Assert.Equal(PageCount.ToString(CultureInfo.InvariantCulture), await Viewer.Data(page, "pageCount"));
        Assert.True(dark > 0, "page 1 has ink on the canvas");
        Assert.True(served < file.Length / 2, $"served {served} of {file.Length} bytes");

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
        await Viewer.LogFramesAsync(page, "remote 30-page linearized file");
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
