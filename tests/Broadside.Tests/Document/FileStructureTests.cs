using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>Header, cross-reference table, trailer and indirect objects. ISO 32000-2 §7.3.10, §7.5.1 to §7.5.5.</summary>
public class FileStructureTests
{
    [Fact]
    public void Offsets_count_from_the_header_so_bytes_before_it_do_not_matter()
    {
        byte[] junk = Encoding.Latin1.GetBytes("Junk\r\nï»¿");
        byte[] file = [.. junk, .. Corpus.Bytes("empty-page.pdf")];

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(document.Pages).MediaBox);
        Assert.Equal(new PdfVersion(1, 7), document.Version);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_file_without_a_header_opens_from_byte_0_with_a_diagnostic()
    {
        byte[] file = Corpus.Bytes("empty-page.pdf");
        file[1] = (byte)'X';

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Single(document.Pages);
        Assert.Equal(new PdfVersion(1, 4), document.Version);
        Assert.Equal("HeaderMissing", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void The_newest_section_wins_and_older_sections_are_read_through_prev()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("incremental-update.pdf"));

        Assert.Equal(new PdfRectangle(0, 0, 595, 842), Assert.Single(document.Pages).MediaBox);
        Assert.IsType<CosDictionary>(document.Resolve(new CosReference(1, 0)));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_prev_chain_that_loops_ends_with_a_diagnostic()
    {
        byte[] file = WithTrailerEntries(xref => $"/Prev {xref}");

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Single(document.Pages);
        Assert.Equal("XrefPrevLoop", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Entries_are_read_as_tokens_not_fixed_width_records()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("empty-page.pdf")).Replace(" \n", "\r\n", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Single(document.Pages);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_startxref_offset_on_the_white_space_before_xref_is_repaired_with_a_diagnostic()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("empty-page.pdf")).Replace("startxref\n203", "startxref\n202", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Single(document.Pages);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("StartxrefInvalid", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void A_file_with_no_startxref_is_rebuilt_by_scanning_and_keeps_its_trailer()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("empty-page.pdf")).Replace("startxref", "startxrex", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(document.Pages).MediaBox);
        Assert.Equal("StartxrefMissing", Assert.Single(document.Diagnostics).Code);
        Assert.Equal(new CosReference(1, 0), document.Trailer[new CosName("Root")]);
        Assert.Equal(new CosInteger(4), document.Trailer[new CosName("Size")]);
        Assert.Equal(text.Length, Assert.Single(document.Revisions).Length);
    }

    [Fact]
    public void A_file_that_holds_no_catalog_cannot_be_read_even_leniently()
    {
        DiagnosticException error = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(Encoding.Latin1.GetBytes("%PDF-1.7\n1 0 obj\n(text)\nendobj\n")));

        Assert.Equal("CatalogNotFound", error.Diagnostic.Code);
    }

    [Fact]
    public void A_trailer_without_root_is_repaired_by_finding_the_catalog_by_scanning()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("empty-page.pdf")).Replace("/Root 1 0 R", "/Rood 1 0 R", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Single(document.Pages);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("RootMissing", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(new CosReference(1, 0), document.Trailer[new CosName("Root")]);
        DiagnosticException error = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(Encoding.Latin1.GetBytes(text), new PdfOptions().UseStrict()));
        Assert.Equal("RootMissing", error.Diagnostic.Code);
    }

    [Fact]
    public void A_catalog_without_its_type_is_read_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>"));

        Assert.Single(document.Pages);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("CatalogTypeInvalid", diagnostic.Code);
        Assert.Equal(new CosReference(1, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void References_to_missing_free_or_other_generation_objects_resolve_to_null_without_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(99, 0)));
        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(1, 1)));
        Assert.Same(CosNull.Instance, document.Resolve(null));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Resolving_a_reference_twice_returns_the_same_instance_and_direct_objects_resolve_to_themselves()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        var reference = new CosReference(3, 0);

        CosObject page = document.Resolve(reference);

        Assert.Same(page, document.Resolve(reference));
        Assert.Same(page, document.Pages[0].Dictionary);
        Assert.Same(document.Catalog, document.Resolve(document.Catalog));
        Assert.Same(document.Catalog, document.Resolve(document.Trailer[new CosName("Root")]));
    }

    [Fact]
    public void A_stream_takes_its_extent_from_an_indirect_length()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /Contents 4 0 R >>",
            "<< /Length 5 0 R >>\nstream\nendstream inside\nendstream",
            "16"));

        CosStream contents = Assert.IsType<CosStream>(document.Resolve(new CosReference(4, 0)));

        Assert.Equal("endstream inside", Encoding.Latin1.GetString(contents.EncodedData.Span));
        Assert.False(contents.IsDirty);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_indirect_length_that_is_wrong_falls_back_to_endstream_with_a_diagnostic_on_the_stream()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Length 3 0 R >>\nstream\nabc\nendstream",
            "99"));

        CosStream stream = Assert.IsType<CosStream>(document.Resolve(new CosReference(2, 0)));

        Assert.Equal("abc", Encoding.Latin1.GetString(stream.EncodedData.Span));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("StreamLengthInvalid", diagnostic.Code);
        Assert.Equal(new CosReference(2, 0), diagnostic.ObjectReference);
    }

    public static TheoryData<string, byte[]> BinaryCommentFiles => new()
    {
        // §7.5.2: a file with binary data has, right after the header, a comment line with at least four bytes of 128 or more.
        { "%PDF-1.7\n%âãÏÓ", [0x80, 0xFF] },
        { "%PDF-1.7\r\n%âãÏÓ comment\r", [0x80, 0xFF] },
        { "%PDF-1.7", "text"u8.ToArray() },
    };

    [Theory]
    [MemberData(nameof(BinaryCommentFiles))]
    public void Strict_mode_accepts_a_binary_comment_after_the_header_or_a_file_without_binary_data(string header, byte[] data)
    {
        byte[] file = FileWithStreamData(header, data);

        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());

        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("%PDF-1.7")]
    [InlineData("%PDF-1.7\n%âãÏ")]
    [InlineData("%PDF-1.7\n%abcd")]
    [InlineData("%PDF-1.7\n\n%âãÏÓ")]
    public void Strict_mode_rejects_a_file_with_binary_data_and_no_binary_comment_line(string header)
    {
        byte[] file = FileWithStreamData(header, [0x80, 0xFF]);

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(file, new PdfOptions().UseStrict()));

        Assert.Equal("HeaderBinaryCommentMissing", exception.Diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, exception.Diagnostic.Severity);
    }

    [Fact]
    public void An_entry_whose_offset_does_not_hold_the_object_is_read_from_where_its_header_is_with_a_diagnostic()
    {
        byte[] file = new TestPdf().Build("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [] /Count 0 >>", "(three)");
        string text = Encoding.Latin1.GetString(file);
        int third = text.IndexOf("3 0 obj", StringComparison.Ordinal);
        text = text.Replace($"{third:D10} 00000 n", $"{third + 1:D10} 00000 n", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        CosObject three = document.Resolve(new CosReference(3, 0));
        Assert.Equal(new CosString("three"u8), three);
        Assert.Same(three, document.Resolve(new CosReference(3, 0)));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("XrefEntryOffsetInvalid", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(new CosReference(3, 0), diagnostic.ObjectReference);
        Assert.Equal(third + 1, diagnostic.Offset);
    }

    [Fact]
    public void An_in_use_entry_at_offset_0_is_read_from_where_its_header_is_with_a_diagnostic()
    {
        // Offset 0 is the header, never an object: the entry is wrong, not free. pdf.js finds such objects by scanning; so does the
        // misplaced-object search.
        byte[] file = new TestPdf().Build("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [] /Count 0 >>", "(three)");
        string text = Encoding.Latin1.GetString(file);
        int third = text.IndexOf("3 0 obj", StringComparison.Ordinal);
        text = text.Replace($"{third:D10} 00000 n", "0000000000 00000 n", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(new CosString("three"u8), document.Resolve(new CosReference(3, 0)));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("XrefEntryOffsetInvalid", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(new CosReference(3, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void An_in_use_entry_at_offset_0_in_an_update_reads_the_newest_copy_of_the_object()
    {
        // The update rewrites object 3 but its table gives offset 0: the object is still in use, and its newest header is the copy.
        byte[] original = new TestPdf().Build("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [] /Count 0 >>", "(old)");
        string text = Encoding.Latin1.GetString(original);
        int previous = text.LastIndexOf("startxref", StringComparison.Ordinal);
        string previousOffset = text[(previous + "startxref".Length)..].Trim().Split('\n')[0].Trim();
        int xref = text.Length + "3 0 obj\n(new)\nendobj\n".Length;
        text += "3 0 obj\n(new)\nendobj\n"
            + "xref\n3 1\n0000000000 00000 n \n"
            + $"trailer\n<< /Size 4 /Root 1 0 R /Prev {previousOffset} >>\nstartxref\n{xref}\n%%EOF\n";

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(new CosString("new"u8), document.Resolve(new CosReference(3, 0)));
        Assert.Equal(["XrefEntryOffsetInvalid"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void An_entry_for_an_object_the_file_does_not_hold_reads_as_null_with_an_error()
    {
        byte[] file = new TestPdf().Build("<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [] /Count 0 >>", "(three)");
        string text = Encoding.Latin1.GetString(file).Replace("3 0 obj", "4 0 obj", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(3, 0)));
        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(3, 0)));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("XrefEntryOffsetInvalid", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(new CosReference(3, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void An_object_without_endobj_is_read_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("missing-endobj.pdf"));

        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(document.Pages).MediaBox);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("MissingEndobj", diagnostic.Code);
        Assert.Equal(new CosReference(3, 0), diagnostic.ObjectReference);
    }

    /// <summary>A one-page file whose header lines (without their last end-of-line marker) are <paramref name="header"/> and whose object 4 is a stream of <paramref name="data"/>.</summary>
    private static byte[] FileWithStreamData(string header, byte[] data) =>
        new TestPdf { Header = header, BinaryComment = false }.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            $"<< /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream");

    private static byte[] WithTrailerEntries(Func<int, string> entries)
    {
        byte[] probe = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>");
        int xref = Encoding.Latin1.GetString(probe).IndexOf("xref", StringComparison.Ordinal);
        return new TestPdf { TrailerEntries = entries(xref) }.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>");
    }
}
