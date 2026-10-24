using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Layout;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// A linearized file opened through HTTP range requests (ISO 32000-2 Annex F and Annex G, G.2 to G.4; issue #208): the first
/// request covers the first-page section, page 1 renders from it and the hint stream alone, and the page offset and shared object
/// hint tables locate any other page's objects, so jumping to a page fetches its hinted range and its shared objects only. Hints
/// that do not match the file fall back to the page tree with a diagnostic.
/// </summary>
public sealed class FirstPageFirstTests
{
    private static readonly Uri FileUri = new("http://broadside.test/linearized.pdf");

    [Theory]
    [InlineData("linearized.pdf")]
    [InlineData("linearized-xref-stream.pdf")]
    [InlineData("linearized-flate-hints.pdf")]
    public async Task A_linearized_corpus_file_opens_and_renders_page_1_from_the_first_request(string fileName)
    {
        byte[] file = await File.ReadAllBytesAsync(Corpus.Path(fileName), TestContext.Current.CancellationToken);
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();

        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, TestContext.Current.CancellationToken);
        PdfPage first = document.Pages[0];
        await first.PrefetchAsync(TestContext.Current.CancellationToken);
        var backend = new RecordingBackend();
        first.CreateDisplayList().Replay(backend);

        Assert.True(document.IsLinearized);
        Assert.Equal(2, document.Pages.Count);
        Assert.NotEmpty(backend.Operations);
        Assert.Contains("Page 1", first.ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
        ServedRequest only = Assert.Single(server.Requests);
        Assert.Equal(0, only.Start);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table, PagePadding)]
    [InlineData(PdfCrossReferenceLayout.Stream, PagePadding)]
    [InlineData(PdfCrossReferenceLayout.Table, 3 * PagePadding)]
    [InlineData(PdfCrossReferenceLayout.Stream, 3 * PagePadding)]
    public async Task Opening_a_large_linearized_file_and_rendering_page_1_fetches_only_the_first_page_section_and_the_hint_stream(PdfCrossReferenceLayout layout, int firstPagePadding)
    {
        // A first page larger than the head (120 KiB of content) takes one more request, for the rest of the first-page section.
        (byte[] file, Layout parts) = LargeLinearized(layout, firstPagePadding);
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();

        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, TestContext.Current.CancellationToken);
        Assert.Equal(PageCount, document.Pages.Count);
        PdfPage first = document.Pages[0];
        await first.PrefetchAsync(TestContext.Current.CancellationToken);
        var backend = new RecordingBackend();
        first.CreateDisplayList().Replay(backend);

        Assert.True(document.IsLinearized);
        Assert.NotEmpty(backend.Operations);
        Assert.Contains("Page 1", first.ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
        // Annex G, G.2: the first request reads the head of the file, and beyond it only the rest of the first-page section (parts
        // 1 to 6, up to E) and the primary hint stream are fetched; the main cross-reference section at the end of the file and the
        // page tree are not needed.
        Assert.Equal(0, server.Requests[0].Start);
        Assert.InRange(server.Requests.Count, 1, firstPagePadding > PagePadding ? 2 : 1);
        Assert.All(server.Requests.Skip(1), request => Assert.True(
            request.Start + request.Length <= RoundUp(parts.FirstPageEnd) || parts.Hint.Contains(request),
            $"{request} lies outside the first-page section [0, {parts.FirstPageEnd}) and the hint stream: {server}"));
        Assert.False(server.AnyServed(parts.MainCrossReference, file.Length), $"The main cross-reference section was fetched: {server}");
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.Stream)]
    public async Task Jumping_to_the_last_page_fetches_only_its_hinted_range_and_its_shared_objects(PdfCrossReferenceLayout layout)
    {
        (byte[] file, Layout parts) = LargeLinearized(layout);
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();
        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, new PdfOptions().WithSynchronousRangeFetch(false), TestContext.Current.CancellationToken);
        int opened = server.Requests.Count;

        PdfPage last = await document.Pages.PrefetchAsync(PageCount - 1, TestContext.Current.CancellationToken);

        Assert.Equal(parts.PageObjects[^1], last.Reference!.ObjectNumber);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"Page {PageCount}"), last.ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);

        // Annex G, G.4: the page's own range (Table F.4) and the ranges of the shared groups it references (Table F.6); neither the
        // main cross-reference section nor the page tree, nor any other page.
        PdfPageHint hint = parts.Hints.Pages[^1];
        ByteRange[] allowed =
        [
            new(hint.Offset, hint.Length),
            .. hint.SharedObjects.Select(index => new ByteRange(parts.Hints.SharedObjects[index].Offset, parts.Hints.SharedObjects[index].Length)),
        ];
        Assert.True(server.Requests.Count > opened, server.ToString());
        Assert.All(server.Requests.Skip(opened), request => Assert.True(
            allowed.Any(range => range.Contains(request)),
            $"{request} lies outside the last page's hinted range and its shared objects: {server}"));
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.Stream)]
    public async Task A_hinted_page_finds_its_index_and_extracts_its_layout_without_the_page_tree(PdfCrossReferenceLayout layout)
    {
        // Issue #410: the layout (the browser viewer's text selection and accessibility) asks the page collection for the page's
        // index. Walking the page tree for it needs the main cross-reference section at the end of the file, which a browser
        // cannot fetch synchronously, so the viewer reported a PdfRangeNotLoadedException for every page of a remote linearized file.
        (byte[] file, Layout parts) = LargeLinearized(layout);
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();
        using PdfDocument document = await PdfDocument.OpenAsync(client, FileUri, new PdfOptions().WithSynchronousRangeFetch(false), TestContext.Current.CancellationToken);

        foreach (int index in (int[])[0, 1, PageCount - 1])
        {
            PdfPage page = await document.Pages.PrefetchAsync(index, TestContext.Current.CancellationToken);
            await page.PrefetchAsync(TestContext.Current.CancellationToken);
            int fetched = server.Requests.Count;

            Assert.Equal(index, document.Pages.IndexOf(page));
            LayoutPage laidOut = Assert.Single(page.ExtractLayout().Pages);
            Assert.Equal(index, laidOut.PageIndex);
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"Page {index + 1}"), laidOut.Text.Text, StringComparison.Ordinal);
            Assert.Equal(fetched, server.Requests.Count); // what the page's prefetch fetched is all its layout needs
        }

        Assert.Empty(document.Diagnostics);
        Assert.False(server.AnyServed(RoundUp(parts.MainCrossReference), file.Length), $"The main cross-reference section was fetched: {server}");
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.Stream)]
    public async Task A_remote_linearized_file_reads_like_the_same_file_opened_locally(PdfCrossReferenceLayout layout)
    {
        (byte[] file, _) = LargeLinearized(layout);
        using PdfDocument local = PdfDocument.Open(file);
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();

        using PdfDocument remote = await PdfDocument.OpenAsync(client, FileUri, new PdfOptions().WithSynchronousRangeFetch(true), TestContext.Current.CancellationToken);

        AssertSameDocument(local, remote);
        Assert.Empty(remote.Diagnostics);
    }

    [Fact]
    public async Task Hints_that_do_not_locate_the_pages_open_with_a_diagnostic_and_the_pages_of_the_page_tree()
    {
        // The page offset hint table's "location of the first page's page object" (Table F.3 item 2, bytes 4 to 7 of the
        // unfiltered hint stream data) moved by 7 bytes: the table still parses, but every page it places is 7 bytes off.
        (byte[] file, Layout parts) = LargeLinearized(PdfCrossReferenceLayout.Table);
        int data = Encoding.Latin1.GetString(file).IndexOf("stream\n", (int)parts.Hint.Start, StringComparison.Ordinal) + "stream\n".Length;
        int location = (file[data + 4] << 24) | (file[data + 5] << 16) | (file[data + 6] << 8) | file[data + 7];
        location += 7;
        file[data + 4] = (byte)(location >> 24);
        file[data + 5] = (byte)(location >> 16);
        file[data + 6] = (byte)(location >> 8);
        file[data + 7] = (byte)location;
        using PdfDocument local = PdfDocument.Open(file);
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();

        using PdfDocument remote = await PdfDocument.OpenAsync(client, FileUri, new PdfOptions().WithSynchronousRangeFetch(true), TestContext.Current.CancellationToken);

        AssertSameDocument(local, remote);
        Diagnostic hints = Assert.Single(remote.Diagnostics);
        Assert.Equal("LinearizationHintsInvalid", hints.Code);
        Assert.Equal(DiagnosticSeverity.Warning, hints.Severity);
    }

    [Fact]
    public async Task A_hint_stream_that_cannot_be_read_opens_with_a_diagnostic_and_the_pages_of_the_page_tree()
    {
        // The primary hint stream's S entry (the shared object hint table's position) points past its data.
        (byte[] file, Layout parts) = LargeLinearized(PdfCrossReferenceLayout.Table);
        string text = Encoding.Latin1.GetString(file);
        int s = text.IndexOf("/S ", (int)parts.Hint.Start, StringComparison.Ordinal) + "/S ".Length;
        int digits = 0;
        while (char.IsAsciiDigit(text[s + digits]))
        {
            digits++;
        }

        Encoding.Latin1.GetBytes(new string('9', digits)).CopyTo(file, s);
        using PdfDocument local = PdfDocument.Open(file);
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();

        using PdfDocument remote = await PdfDocument.OpenAsync(client, FileUri, new PdfOptions().WithSynchronousRangeFetch(true), TestContext.Current.CancellationToken);

        AssertSameDocument(local, remote);
        Assert.Contains(remote.Diagnostics, diagnostic => diagnostic.Code == "LinearizationHintsInvalid");
        Assert.All(remote.Diagnostics, diagnostic => Assert.Equal("LinearizationHintsInvalid", diagnostic.Code));
    }

    [Fact]
    public async Task An_update_appended_to_a_linearized_file_makes_it_read_from_its_end_like_any_file()
    {
        // G.7: L no longer matches the file length, so the file is not linearized and its hints are not used.
        (byte[] linearized, _) = LargeLinearized(PdfCrossReferenceLayout.Table);
        int size;
        using (PdfDocument original = PdfDocument.Open(linearized))
        {
            size = (int)((CosInteger)original.Trailer[new CosName("Size")]).Value;
        }

        byte[] file = TestPdf.AppendUpdate(linearized, $"/Size {size + 1} /Root {RootOf(linearized)}", (size, 0, "<< /Updated true >>"));
        using PdfDocument local = PdfDocument.Open(file);
        using var server = new RangeServer(file);
        using HttpClient client = server.CreateClient();

        using PdfDocument remote = await PdfDocument.OpenAsync(client, FileUri, new PdfOptions().WithSynchronousRangeFetch(true), TestContext.Current.CancellationToken);

        Assert.False(remote.IsLinearized);
        Assert.Equal(2, remote.Revisions.Count);
        Assert.Equal(file.Length, server.Requests[1].Start + server.Requests[1].Length);
        AssertSameDocument(local, remote);
        Assert.Equal(local.Diagnostics.Select(static diagnostic => diagnostic.Code), remote.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    private static string RootOf(byte[] file)
    {
        using PdfDocument document = PdfDocument.Open(file);
        var root = (CosReference)document.Trailer[new CosName("Root")];
        return string.Create(CultureInfo.InvariantCulture, $"{root.ObjectNumber} {root.Generation} R");
    }

    /// <summary>Every page reads the same (object, boxes, text), and so do the file's structure and trailer.</summary>
    private static void AssertSameDocument(PdfDocument expected, PdfDocument actual)
    {
        Assert.Equal(expected.IsLinearized, actual.IsLinearized);
        Assert.Equal(expected.Pages.Select(static page => page.Reference), actual.Pages.Select(static page => page.Reference));
        Assert.Equal(expected.Pages.Count, actual.Pages.Count);
        for (int index = 0; index < expected.Pages.Count; index++)
        {
            PdfPage before = expected.Pages[index];
            PdfPage after = actual.Pages[index];
            Assert.Equal(before.Reference, after.Reference);
            Assert.Equal(before.MediaBox, after.MediaBox);
            Assert.Equal(before.CropBox, after.CropBox);
            Assert.Equal(before.Rotation, after.Rotation);
            Assert.Equal(before.ExtractText().Text, after.ExtractText().Text);
        }

        Assert.Equal(expected.Revisions.Count, actual.Revisions.Count);
        Assert.Equal(expected.Revisions[^1].Length, actual.Revisions[^1].Length);
        Assert.Equal(expected.Trailer.Keys.Select(static key => key.Value).Order(StringComparer.Ordinal), actual.Trailer.Keys.Select(static key => key.Value).Order(StringComparer.Ordinal));
        Assert.Equal(expected.Linearization!.FirstPageObjects, actual.Linearization!.FirstPageObjects);
        Assert.Equal(0, actual.Pages.IndexOf(actual.Pages[0]));
        Assert.Equal(expected.Pages.Count - 1, actual.Pages.IndexOf(actual.Pages[^1]));
    }

    private const int PageCount = 30;
    private const int PagePadding = 40 * 1024;
    private const int Block = PdfOptions.DefaultRangeBlockSize;

    private static long RoundDown(long offset) => offset / Block * Block;

    private static long RoundUp(long offset) => (offset + Block - 1) / Block * Block;

    /// <summary>
    /// A 30-page file, linearized by the writer (issue #228): a font shared by every page, so it is a shared object of the first
    /// page; each content stream shows "Page N" and is padded with comments to 40 KiB, so the file is about 1.2 MB.
    /// </summary>
    private static (byte[] File, Layout Parts) LargeLinearized(PdfCrossReferenceLayout layout, int firstPagePadding = PagePadding)
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
            var content = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"0 0 10 10 re f BT /F1 12 Tf 72 720 Td (Page {page + 1}) Tj ET\n"));
            while (content.Length < (page == 0 ? firstPagePadding : PagePadding))
            {
                content.Append('%').Append('x', 78).Append('\n');
            }

            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
        }

        using PdfDocument source = PdfDocument.Open(new TestPdf().Build([.. objects]));
        using var stream = new MemoryStream();
        source.Save(stream, new PdfSaveOptions().WithLinearize(true).WithCrossReferenceLayout(layout).WithStreamCompression(PdfStreamCompression.None));
        byte[] file = stream.ToArray();
        using PdfDocument local = PdfDocument.Open(file);
        CosDictionary parameters = local.Linearization!.Dictionary;
        var h = (CosArray)parameters[new CosName("H")];
        return (file, new Layout(
            Integer(parameters, "E"),
            new ByteRange(((CosInteger)h[0]).Value, ((CosInteger)h[1]).Value),
            Integer(parameters, "T"),
            local.Linearization.Hints!,
            [.. local.Pages.Select(static page => page.Reference!.ObjectNumber)]));
    }

    private static long Integer(CosDictionary dictionary, string key) => ((CosInteger)dictionary[new CosName(key)]).Value;

    /// <summary>Where the parts of a linearized file are: the end of the first page (E), the hint stream and the main section (T).</summary>
    private sealed record Layout(long FirstPageEnd, ByteRange Hint, long MainCrossReference, PdfLinearizationHints Hints, IReadOnlyList<int> PageObjects);

    /// <summary>A byte range, rounded out to the range source's blocks when tested against a request.</summary>
    private readonly record struct ByteRange(long Start, long Length)
    {
        public bool Contains(ServedRequest request) =>
            request.Start >= RoundDown(Start) && request.Start + request.Length <= RoundUp(Start + Length);
    }
}
