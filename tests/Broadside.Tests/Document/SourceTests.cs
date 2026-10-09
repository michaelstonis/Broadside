using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Where a document's bytes come from: memory, a memory-mapped file, a seekable stream read in place, or a non-seekable stream
/// buffered when it is opened. ISO 32000-2 §7.5.1 (a file is read at random through its cross-reference table) and §7.5.4.
/// </summary>
public class SourceTests
{
    private const int ContentLength = 64 * 1024;

    [Fact]
    public void A_seekable_stream_is_read_in_place_only_where_objects_are_needed()
    {
        byte[] file = LargePages(pageCount: 16);
        using var stream = new ProbeStream(file);

        using PdfDocument document = PdfDocument.Open(stream);
        Assert.Equal(16, document.Pages.Count);

        // The page tree is read; the sixteen 64 KB content streams are not.
        Assert.True(stream.BytesRead < file.Length / 4, $"{stream.BytesRead} of {file.Length} bytes were read to open the file and walk its pages.");

        var contents = (CosStream)document.Resolve(document.Pages[15].Dictionary[new CosName("Contents")]);
        Assert.Equal(Content(15), document.DecodeStream(contents).ToArray());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_object_larger_than_the_first_read_of_a_stream_is_read_whole()
    {
        byte[] content = [.. Enumerable.Repeat((byte)'%', 300_000), (byte)'\n'];
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{System.Text.Encoding.Latin1.GetString(content)}\nendstream");
        using var stream = new ProbeStream(file);

        using PdfDocument document = PdfDocument.Open(stream);

        var contents = (CosStream)document.Resolve(new CosReference(4, 0));
        Assert.Equal(content, contents.EncodedData.ToArray());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_stream_is_read_from_its_position_when_opened()
    {
        byte[] file = [.. "not part of the file"u8, .. Corpus.Bytes("page-tree-inherited.pdf")];
        using var stream = new ProbeStream(file);
        stream.Position = "not part of the file"u8.Length;

        using PdfDocument document = PdfDocument.Open(stream);
        using PdfDocument expected = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));

        Assert.Equal(DocumentProjection.Of(expected), DocumentProjection.Of(document));
    }

    [Fact]
    public void The_callers_stream_is_left_open()
    {
        using var stream = new ProbeStream(Corpus.Bytes("empty-page.pdf"));

        PdfDocument.Open(stream).Dispose();

        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public void A_non_seekable_stream_is_read_whole_when_opened()
    {
        byte[] file = Corpus.Bytes("object-stream.pdf");
        using var stream = new ProbeStream(file, seekable: false);

        using PdfDocument document = PdfDocument.Open(stream);
        using PdfDocument expected = PdfDocument.Open(file);

        Assert.Equal(file.Length, stream.BytesRead);
        Assert.Equal(DocumentProjection.Of(expected), DocumentProjection.Of(document));
    }

    [Fact]
    public void A_non_seekable_stream_longer_than_the_buffer_limit_is_read_through_a_temporary_file()
    {
        byte[] file = LargePages(pageCount: 4);
        using var stream = new ProbeStream(file, seekable: false);

        using PdfDocument document = PdfDocument.Open(stream, new PdfOptions().WithStreamBufferLimit(4096));

        Assert.Equal(4, document.Pages.Count);
        var contents = (CosStream)document.Resolve(document.Pages[3].Dictionary[new CosName("Contents")]);
        Assert.Equal(Content(3), document.DecodeStream(contents).ToArray());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public async Task OpenAsync_reads_a_non_seekable_stream_asynchronously()
    {
        byte[] file = Corpus.Bytes("xref-stream.pdf");
        using var stream = new ProbeStream(file, seekable: false, synchronous: false);

        using PdfDocument document = await PdfDocument.OpenAsync(stream, TestContext.Current.CancellationToken);
        using PdfDocument expected = PdfDocument.Open(file);

        Assert.Equal(DocumentProjection.Of(expected), DocumentProjection.Of(document));
    }

    [Fact]
    public async Task OpenAsync_spills_a_long_non_seekable_stream_to_a_temporary_file_asynchronously()
    {
        byte[] file = LargePages(pageCount: 4);
        using var stream = new ProbeStream(file, seekable: false, synchronous: false);
        var engine = new PdfEngine(new PdfOptions().WithStreamBufferLimit(4096));

        using PdfDocument document = await engine.OpenAsync(stream, TestContext.Current.CancellationToken);

        var contents = (CosStream)document.Resolve(document.Pages[2].Dictionary[new CosName("Contents")]);
        Assert.Equal(Content(2), document.DecodeStream(contents).ToArray());
    }

    [Fact]
    public async Task OpenAsync_honors_cancellation()
    {
        using var stream = new ProbeStream(Corpus.Bytes("empty-page.pdf"), seekable: false, synchronous: false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfDocument.OpenAsync(stream, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Stream_data_of_a_disposed_document_opened_from_a_path_cannot_be_read()
    {
        PdfDocument document = PdfDocument.Open(Corpus.Path("flate-stream.pdf"));
        var contents = (CosStream)document.Resolve(document.Pages[0].Dictionary[new CosName("Contents")]);
        Assert.False(contents.EncodedData.IsEmpty);

        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => contents.EncodedData);
    }

    [Fact]
    public void Stream_data_of_a_document_opened_from_bytes_refers_to_those_bytes()
    {
        byte[] file = Corpus.Bytes("flate-stream.pdf");
        using PdfDocument document = PdfDocument.Open(file);

        var contents = (CosStream)document.Resolve(document.Pages[0].Dictionary[new CosName("Contents")]);

        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(contents.EncodedData, out ArraySegment<byte> segment));
        Assert.Same(file, segment.Array);
    }

    /// <summary>A file of <paramref name="pageCount"/> pages, each with a content stream of <see cref="ContentLength"/> bytes.</summary>
    internal static byte[] LargePages(int pageCount)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pageCount).Select(page => $"{3 + (2 * page)} 0 R"))}] /Count {pageCount} >>",
        };
        for (int page = 0; page < pageCount; page++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents {4 + (2 * page)} 0 R >>");
            byte[] content = Content(page);
            objects.Add($"<< /Length {content.Length} >>\nstream\n{System.Text.Encoding.Latin1.GetString(content)}\nendstream");
        }

        return new TestPdf().Build([.. objects]);
    }

    /// <summary>A content stream that paints nothing: a save/restore pair, then a comment padding it to <see cref="ContentLength"/>.</summary>
    private static byte[] Content(int page)
    {
        byte[] prefix = System.Text.Encoding.ASCII.GetBytes($"q Q % page {page} ");
        return [.. prefix, .. Enumerable.Repeat((byte)'x', ContentLength - prefix.Length - 1), (byte)'\n'];
    }
}
