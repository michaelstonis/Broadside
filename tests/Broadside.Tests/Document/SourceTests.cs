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

    /// <summary>The window a windowed source first reads a structure in, before #45 grew it: 4 MiB.</summary>
    private const int PdfSectionBound = 4 << 20;

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
    public void A_cross_reference_table_larger_than_4_MiB_is_read_whole_through_a_seekable_stream()
    {
        // 300 000 entries of 20 bytes: a 6 MB section, whose last entry names the object resolved here.
        const int last = 300_000;
        var text = new System.Text.StringBuilder("%PDF-1.7\n");
        var offsets = new int[last + 1];
        string[] bodies = [.. XrefStreamPdf.OnePage];
        for (int index = 0; index < bodies.Length; index++)
        {
            offsets[index + 1] = text.Length;
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{bodies[index]}\nendobj\n");
        }

        offsets[last] = text.Length;
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{last} 0 obj\n(the last object)\nendobj\n");
        int xref = text.Length;
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"xref\n0 {last + 1}\n0000000000 65535 f \n");
        for (int number = 1; number <= last; number++)
        {
            text.Append(offsets[number] > 0 ? $"{offsets[number]:D10} 00000 n \n" : "0000000000 00001 f \n");
        }

        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size {last + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        byte[] file = System.Text.Encoding.Latin1.GetBytes(text.ToString());
        Assert.True(file.Length - xref > PdfSectionBound, $"The section is {file.Length - xref} bytes.");

        AssertLastObjectReads(file, last);
    }

    [Fact]
    public void A_cross_reference_stream_larger_than_4_MiB_is_read_whole_through_a_seekable_stream()
    {
        // 800 000 unfiltered rows of 7 bytes: a 5.6 MB stream, whose last row places the object resolved here.
        const int last = 800_000;
        int[] widths = [1, 4, 2];
        var pdf = new XrefStreamPdf().AddOnePage().Add(last, "(the last object)");
        var rows = new (long Type, long Field2, long Field3)[last + 2];
        rows[0] = (0, 0, 65535);
        for (int number = 1; number <= last; number++)
        {
            rows[number] = number is <= 3 or last ? (1, pdf.Offset(number), 0) : (0, 0, 1);
        }

        rows[last + 1] = (1, pdf.Position, 0);
        byte[] file = pdf.Finish(last + 1, $"/Size {last + 2} /Root 1 0 R /W [1 4 2]", widths, rows);

        AssertLastObjectReads(file, last);
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

    /// <summary>
    /// Opens <paramref name="file"/> through a seekable stream that is not a <see cref="FileStream"/> (read in windows) and from
    /// bytes, and requires both to resolve object <paramref name="last"/> with no diagnostic.
    /// </summary>
    private static void AssertLastObjectReads(byte[] file, int last)
    {
        using var stream = new ProbeStream(file);
        using PdfDocument windowed = PdfDocument.Open(stream);
        using PdfDocument whole = PdfDocument.Open(file);

        foreach (PdfDocument document in (PdfDocument[])[windowed, whole])
        {
            Assert.Equal("the last object", Assert.IsType<CosString>(document.Resolve(new CosReference(last, 0))).DecodeText());
            Assert.Single(document.Pages);
            Assert.Empty(document.Diagnostics);
        }
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
