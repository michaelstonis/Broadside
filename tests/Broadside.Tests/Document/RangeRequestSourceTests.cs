using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Broadside.Tests.Document;

/// <summary>
/// Opening a remote file through HTTP range requests (spec #33, viewer capability 5; issue #207): the tail first, then only the
/// byte ranges the objects in use occupy, through a bounded block cache; a server without range support still serves the whole
/// file; and where synchronous reading cannot block (the browser), a page's objects are prefetched asynchronously first.
/// </summary>
public sealed class RangeRequestSourceTests
{
    private const int PageCount = 50;
    private const int ContentPadding = 40 * 1024;
    private static readonly Uri FileUri = new("http://broadside.test/fifty-pages.pdf");

    [Fact]
    public async Task Opening_and_reading_page_1_of_a_50_page_file_fetches_under_a_fifth_of_its_bytes()
    {
        (byte[] file, long[] contentOffsets) = FiftyPages();
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();

        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, TestContext.Current.CancellationToken);
        Assert.Equal(PageCount, document.Pages.Count);
        PdfPage first = document.Pages[0];
        await first.PrefetchAsync(TestContext.Current.CancellationToken);
        string text = first.ExtractText().Text;

        Assert.Contains("Page 1", text, StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);

        // The head is read first, to see whether the file is linearized (issue #208); it is not, so the tail follows: startxref,
        // the cross-reference table and the trailer are at the end of the file.
        Assert.StartsWith("bytes=0-", server.Requests[0].Range, StringComparison.Ordinal);
        long headEnd = server.Requests[0].Length;
        long tailStart = server.Requests[1].Start;
        Assert.Equal(file.Length, tailStart + server.Requests[1].Length);
        Assert.All(server.Requests, request => Assert.Equal(HttpStatusCode.PartialContent, request.Status));

        // The page tree reads every page object, but no other page's content is fetched: the middle of each content stream is never
        // sent, except for those inside the head or the tail.
        for (int page = 1; page < PageCount; page++)
        {
            long middle = contentOffsets[page] + (ContentPadding / 2);
            if (middle >= headEnd && middle + 1024 <= tailStart)
            {
                Assert.False(server.AnyServed(middle, middle + 1024), string.Create(CultureInfo.InvariantCulture, $"Page {page + 1}'s content was fetched: {server}"));
            }
        }

        Assert.True(
            server.BytesServed * 5 < file.Length,
            string.Create(CultureInfo.InvariantCulture, $"{server.BytesServed} of {file.Length} bytes were fetched: {server}"));
    }

    [Fact]
    public async Task A_server_without_range_support_still_opens_the_file_with_one_request()
    {
        (byte[] file, _) = FiftyPages();
        using var server = new RangeServer(file, supportsRanges: false);
        using HttpClient client = server.CreateClient();

        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, TestContext.Current.CancellationToken);

        Assert.Equal(PageCount, document.Pages.Count);
        Assert.Contains("Page 50", document.Pages[49].ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
        ServedRequest only = Assert.Single(server.Requests);
        Assert.Equal(HttpStatusCode.OK, only.Status);
    }

    [Fact]
    public async Task Without_synchronous_fetching_as_on_the_browser_a_page_read_before_its_prefetch_fails_clearly()
    {
        (byte[] file, _) = FiftyPages();
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();
        PdfOptions options = new PdfOptions().WithSynchronousRangeFetch(false);

        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, options, TestContext.Current.CancellationToken);
        PdfPage page = document.Pages[29];

        PdfRangeNotLoadedException missed = Assert.Throws<PdfRangeNotLoadedException>(() => page.ExtractText());
        Assert.Contains(nameof(PdfPage.PrefetchAsync), missed.Message, StringComparison.Ordinal);
        Assert.InRange(missed.Offset, 0, file.Length - 1);
        Assert.True(missed.Length > 0);

        // Once prefetched, the same page reads synchronously, and the failed read left nothing broken behind.
        await page.PrefetchAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Page 30", page.ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public async Task With_synchronous_fetching_a_page_read_without_a_prefetch_fetches_what_it_misses()
    {
        (byte[] file, _) = FiftyPages();
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();

        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, new PdfOptions().WithSynchronousRangeFetch(true), TestContext.Current.CancellationToken);

        Assert.Contains("Page 42", document.Pages[41].ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Synchronous_fetching_is_on_by_default_except_on_the_browser()
    {
        Assert.Equal(!OperatingSystem.IsBrowser() && !OperatingSystem.IsWasi(), new PdfOptions().SynchronousRangeFetch);
    }

    [Fact]
    public async Task A_cache_smaller_than_the_file_still_reads_every_page()
    {
        (byte[] file, _) = FiftyPages();
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();
        PdfOptions options = new PdfOptions().WithRangeBlockSize(4096).WithRangeCacheLimit(256 * 1024).WithSynchronousRangeFetch(false);

        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, options, TestContext.Current.CancellationToken);
        for (int index = 0; index < PageCount; index++)
        {
            PdfPage page = document.Pages[index];
            await page.PrefetchAsync(TestContext.Current.CancellationToken);
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"Page {index + 1}"), page.ExtractText().Text, StringComparison.Ordinal);
        }

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public async Task A_deviation_found_while_opening_is_logged_once_however_many_round_trips_the_open_takes()
    {
        // startxref points at the end-of-line marker before "xref": a warning found in the tail. The catalog is 20 KB into the file
        // and 200 KB from its end, out of the header's block and the tail, so the attempt that records the warning stops on a miss and the open runs again.
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new SortedDictionary<int, int>();
        offsets[4] = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"4 0 obj\n<< /Length 20000 >>\nstream\n{new string('%', 20_000)}\nendstream\nendobj\n");
        string[] bodies = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 4 0 R >>"];
        for (int index = 0; index < bodies.Length; index++)
        {
            offsets[index + 1] = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{bodies[index]}\nendobj\n");
        }

        offsets[5] = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"5 0 obj\n<< /Length 200000 >>\nstream\n{new string('%', 200_000)}\nendstream\nendobj\n");
        int xref = text.Length;
        text.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int offset in offsets.Values)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref - 1}\n%%EOF\n");
        using var server = new RangeServer(Encoding.Latin1.GetBytes(text.ToString()));
        using HttpClient client = server.CreateClient();
        using var logs = new Hosting.CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, new PdfOptions().WithLoggerFactory(loggerFactory), TestContext.Current.CancellationToken);

        Broadside.Diagnostics.Diagnostic warning = Assert.Single(document.Diagnostics);
        Assert.Equal("StartxrefInvalid", warning.Code);
        Assert.True(server.Requests.Count > 1, server.ToString());
        Assert.Single(logs.Entries, entry => entry.EventId.Id == 1 && entry.Message.Contains(warning.Code, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_server_that_stops_serving_ranges_fails_the_read_instead_of_mixing_answers()
    {
        (byte[] file, _) = FiftyPages();
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();
        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, TestContext.Current.CancellationToken);

        server.StopServingRanges();

        IOException failure = await Assert.ThrowsAsync<IOException>(() => document.Pages[20].PrefetchAsync(TestContext.Current.CancellationToken));
        Assert.Contains("stopped serving ranges", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_older_cross_reference_section_is_read_without_fetching_what_lies_after_it()
    {
        // An update appends a 400 KB stream, so the original table is 400 KB before the end, behind the update's own table.
        (byte[] original, _) = FiftyPages();
        string padding = new('%', 400_000);
        byte[] file = TestPdf.AppendUpdate(
            original,
            $"/Size {4 + (2 * PageCount)} /Root 1 0 R",
            (3 + (2 * PageCount), 0, $"<< /Length {padding.Length} >>\nstream\n{padding}\nendstream"));
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();

        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, TestContext.Current.CancellationToken);

        Assert.Equal(2, document.Revisions.Count);
        Assert.Equal(PageCount, document.Pages.Count);
        Assert.Empty(document.Diagnostics);
        long originalTable = Encoding.Latin1.GetString(original).LastIndexOf("\nxref\n", StringComparison.Ordinal);
        long updateStart = original.Length;
        Assert.False(server.AnyServed(originalTable + (64 * 1024), updateStart + (200 * 1024)), server.ToString());
    }

    [Fact]
    public async Task A_document_opened_from_bytes_prefetches_nothing()
    {
        (byte[] file, _) = FiftyPages();
        using PdfDocument document = PdfDocument.Open(file);

        Task prefetch = document.Pages[0].PrefetchAsync(TestContext.Current.CancellationToken);

        Assert.True(prefetch.IsCompletedSuccessfully);
        await prefetch;
    }

    [Fact]
    public async Task A_remote_file_that_is_not_a_PDF_fails_to_open_with_a_diagnostic()
    {
        using var server = new RangeServer(Encoding.ASCII.GetBytes("<html>not found</html>"));
        using HttpClient client = server.CreateClient();

        await Assert.ThrowsAsync<Broadside.Diagnostics.DiagnosticException>(() => PdfDocument.OpenAsync(client, FileUri, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_server_error_fails_the_open_with_the_HTTP_status()
    {
        using var handler = new StatusHandler(HttpStatusCode.NotFound);
        using var client = new HttpClient(handler, disposeHandler: false);

        await Assert.ThrowsAsync<HttpRequestException>(() => PdfDocument.OpenAsync(client, FileUri, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A 50-page file laid out as most writers lay it out: each page object next to its content stream, a shared Helvetica font, the
    /// page tree and catalog first, the cross-reference table last. Each content stream shows "Page N" and is padded with comments
    /// to 40 KiB, so the file is about 2 MB.
    /// </summary>
    private static (byte[] File, long[] ContentOffsets) FiftyPages()
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(' ', Enumerable.Range(0, PageCount).Select(static page => $"{4 + (2 * page)} 0 R")) + $"] /Count {PageCount} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        for (int page = 0; page < PageCount; page++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + (2 * page)} 0 R >>");
            var content = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"BT /F1 12 Tf 72 720 Td (Page {page + 1}) Tj ET\n"));
            while (content.Length < ContentPadding)
            {
                content.Append('%').Append('x', 78).Append('\n');
            }

            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
        }

        byte[] file = new TestPdf().Build([.. objects]);
        string text = Encoding.Latin1.GetString(file);
        long[] offsets = new long[PageCount];
        for (int page = 0; page < PageCount; page++)
        {
            offsets[page] = text.IndexOf(string.Create(CultureInfo.InvariantCulture, $"\n{5 + (2 * page)} 0 obj\n"), StringComparison.Ordinal);
        }

        return (file, offsets);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent([]) });
    }
}
