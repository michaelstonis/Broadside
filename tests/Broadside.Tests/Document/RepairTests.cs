using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Damage the minimal corpus does not cover, built in the test from a well-formed corpus file: lenient reading repairs it with
/// diagnostics (ADR 0005). ISO 32000-2 §7.3.8.2, §7.5.2, §7.5.4, §7.5.5, §7.5.6, §7.5.7, §7.5.8.
/// </summary>
public sealed partial class RepairTests
{
    [Fact]
    public void A_file_whose_line_endings_were_converted_to_crlf_opens_with_the_intact_pages()
    {
        string text = Latin1("empty-page.pdf").Replace("\n", "\r\n", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(document.Pages).MediaBox);
        Assert.Equal("StartxrefInvalid", document.Diagnostics[0].Code);
        Assert.Contains(document.Diagnostics, static diagnostic => diagnostic.Code == "XrefEntryOffsetInvalid");
        Assert.All(document.Diagnostics, static diagnostic => Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));
    }

    [Fact]
    public void Offsets_counted_from_byte_0_instead_of_the_header_are_repaired()
    {
        // §7.5.2: offsets count from the %PDF- header; this writer counted from the start of the file, before 300 bytes of junk.
        const int junk = 300;
        string text = new string('J', junk) + OffsetPattern().Replace(Latin1("empty-page.pdf"), static match =>
            (long.Parse(match.Value, CultureInfo.InvariantCulture) + junk).ToString(match.Length == 10 ? "D10" : "D", CultureInfo.InvariantCulture));

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(document.Pages).MediaBox);
        Assert.Equal(
            ["StartxrefInvalid", "XrefEntryOffsetInvalid", "XrefEntryOffsetInvalid", "XrefEntryOffsetInvalid"],
            document.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_wrong_prev_offset_reads_the_nearest_older_section()
    {
        string text = Latin1("incremental-update.pdf").Replace("/Prev 203", "/Prev 230", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(new PdfRectangle(0, 0, 595, 842), Assert.Single(document.Pages).MediaBox);
        Assert.Equal(2, document.Revisions.Count);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("TrailerPrevInvalid", diagnostic.Code);
        Assert.Equal(230, diagnostic.Offset);
    }

    [Fact]
    public void Rebuilding_an_updated_file_keeps_the_newest_copy_of_each_object_and_the_newest_trailer()
    {
        string text = Latin1("incremental-update.pdf").Replace("startxref", "startxrex", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(new PdfRectangle(0, 0, 595, 842), Assert.Single(document.Pages).MediaBox);
        Assert.Equal("StartxrefMissing", Assert.Single(document.Diagnostics).Code);
        Assert.False(document.Trailer.ContainsKey(new CosName("Prev")));
    }

    [Fact]
    public void Rebuilding_an_encrypted_file_keeps_its_encryption_dictionary_and_identifier()
    {
        string text = Latin1("encrypted-rc4-40.pdf").Replace("startxref", "startxrex", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Single(document.Pages);
        Assert.IsType<CosDictionary>(document.Resolve(document.Trailer[new CosName("Encrypt")]));
        Assert.IsType<CosArray>(document.Trailer[new CosName("ID")]);
    }

    [Fact]
    public void Objects_in_object_streams_survive_a_damaged_cross_reference_stream()
    {
        string text = Latin1("object-stream.pdf").Replace("/W [1 2 1]", "/W [1 2 ]", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(document.Pages).MediaBox);
        Assert.Equal(["XrefStreamWidthsInvalid"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_compressed_catalog_is_found_when_no_trailer_survives()
    {
        string text = Latin1("object-stream.pdf")
            .Replace("/Type /XRef", "/Type /XReg", StringComparison.Ordinal)
            .Replace("startxref", "startxrex", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(document.Pages).MediaBox);
        Assert.Equal(["StartxrefMissing", "TrailerMissing"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
        Assert.Equal(new CosReference(1, 0), document.Trailer[new CosName("Root")]);
    }

    [Fact]
    public void A_subsection_numbered_from_1_whose_first_entry_is_the_free_list_head_is_renumbered_from_0()
    {
        string text = Latin1("empty-page.pdf").Replace("xref\n0 4\n", "xref\n1 4\n", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(document.Pages).MediaBox);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("XrefSubsectionNumberingInvalid", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void Entries_that_are_not_20_bytes_long_are_read_with_one_diagnostic_per_section()
    {
        string text = Latin1("empty-page.pdf").Replace(" n \n", " n\n", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Single(document.Pages);
        Assert.Equal(["XrefEntryFormatInvalid"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Stream_lengths_that_refer_to_each_other_are_recovered_from_endstream()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [] /Count 0 >>",
            "<< /Length 4 0 R >>\nstream\nthree\nendstream",
            "<< /Length 3 0 R >>\nstream\nfour\nendstream"));

        CosStream three = Assert.IsType<CosStream>(document.Resolve(new CosReference(3, 0)));

        Assert.Equal("three", Encoding.Latin1.GetString(three.EncodedData.Span));
        Assert.Equal(
            ["StreamLengthInvalid 4", "StreamLengthInvalid 3"],
            document.Diagnostics.Select(static diagnostic => $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber}"));
    }

    [Fact]
    public void A_stream_without_endstream_ends_at_endobj()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [] /Count 0 >>",
            "<< /Length 99 >>\nstream\nthree"));

        CosStream three = Assert.IsType<CosStream>(document.Resolve(new CosReference(3, 0)));

        Assert.Equal("three", Encoding.Latin1.GetString(three.EncodedData.Span));
        Assert.Equal(["EndstreamMissing"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_repair_met_twice_is_recorded_once()
    {
        byte[] file = Encoding.Latin1.GetBytes(Latin1("empty-page.pdf").Replace("0000000115 00000 n", "0000000117 00000 n", StringComparison.Ordinal));
        using PdfDocument document = PdfDocument.Open(file);

        Parallel.For(0, 8, _ => document.Resolve(new CosReference(3, 0)));
        _ = document.Pages.Count;

        Assert.Equal("XrefEntryOffsetInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_file_with_nothing_but_junk_cannot_be_read()
    {
        DiagnosticException error = Assert.Throws<DiagnosticException>(() => PdfDocument.Open(Encoding.Latin1.GetBytes(new string('x', 5000))));

        Assert.Equal("CatalogNotFound", error.Diagnostic.Code);
    }

    private static string Latin1(string fileName) => Encoding.Latin1.GetString(Corpus.Bytes(fileName));

    /// <summary>The 10-digit entry offsets of the table and the startxref value.</summary>
    [GeneratedRegex(@"\d{10}(?= 00000 n)|(?<=startxref\n)\d+")]
    private static partial Regex OffsetPattern();
}
