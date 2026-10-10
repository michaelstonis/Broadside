using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Cross-reference streams read exactly like classic tables: their dictionary is the trailer, their binary entries locate objects.
/// ISO 32000-2 §7.5.8.1 to §7.5.8.3, Tables 17 and 18.
/// </summary>
public class CrossReferenceStreamTests
{
    private static readonly int[] W121 = [1, 2, 1];

    public static TheoryData<string> CompressedStructureFiles => new(["xref-stream.pdf", "object-stream.pdf", "png-predictor.pdf", "hybrid-xref.pdf"]);

    [Theory]
    [MemberData(nameof(CompressedStructureFiles))]
    public void A_file_with_compressed_structures_exposes_the_same_pages_and_objects_as_the_classic_file(string fileName)
    {
        // Every file holds the catalog (1), page tree (2) and page (3) of empty-page.pdf, the classic baseline (tests/Corpus/README.md).
        using PdfDocument classic = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));

        Assert.Equal(DocumentProjection.Of(classic).Pages, DocumentProjection.Of(document).Pages);
        for (int number = 1; number <= 3; number++)
        {
            var reference = new CosReference(number, 0);
            Assert.True(CosObject.DeepEquals(classic.Resolve(reference), document.Resolve(reference)), $"object {number}");
        }

        Assert.Equal(new CosReference(1, 0), document.Trailer[new CosName("Root")]);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(CompressedStructureFiles))]
    public void Strict_mode_opens_a_file_with_compressed_structures(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName), new PdfOptions().UseStrict());

        Assert.Single(document.Pages);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_trailer_of_a_cross_reference_stream_is_its_dictionary_without_the_entries_that_describe_the_stream()
    {
        // §7.5.8.1: the stream's dictionary replaces the trailer; Type, W, Index, Length and the filter entries describe the stream itself.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("png-predictor.pdf"));

        Assert.Equal(["Size", "Root"], document.Trailer.Keys.Select(key => key.Value));
        Assert.Equal(new CosInteger(5), document.Trailer[new CosName("Size")]);
        Assert.Equal(["Size", "Root"], Assert.Single(document.Revisions).Trailer.Keys.Select(key => key.Value));
    }

    [Fact]
    public void A_missing_type_field_makes_every_entry_type_1()
    {
        // Table 17, W: "If the first element is zero, the type field shall not be present, and shall default to type 1."
        var pdf = new XrefStreamPdf().AddOnePage();
        byte[] file = pdf.Finish(4, "/Size 4 /Index [1 3] /Root 1 0 R /W [0 2 1]", [0, 2, 1], (0, pdf.Offset(1), 0), (0, pdf.Offset(2), 0), (0, pdf.Offset(3), 0));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Single(document.Pages);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_missing_generation_field_defaults_to_generation_0()
    {
        // Table 18: the generation number of a type 1 entry defaults to 0 when its field is absent (W[2] = 0).
        var pdf = new XrefStreamPdf().AddOnePage();
        byte[] file = pdf.Finish(4, "/Size 4 /Root 1 0 R /W [1 4 0]", [1, 4, 0], (0, 0, 0), (1, pdf.Offset(1), 0), (1, pdf.Offset(2), 0), (1, pdf.Offset(3), 0));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Single(document.Pages);
        Assert.IsType<CosDictionary>(document.Resolve(new CosReference(3, 0)));
        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(3, 1)));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Index_with_two_subsections_numbers_the_entries_of_each_from_its_first_object_number()
    {
        var pdf = new XrefStreamPdf().AddOnePage().Add(10, "(ten)").Add(11, "(eleven)");
        byte[] file = pdf.Finish(
            12,
            "/Size 12 /Index [1 3 10 2] /Root 1 0 R /W [1 2 1]",
            W121,
            (1, pdf.Offset(1), 0),
            (1, pdf.Offset(2), 0),
            (1, pdf.Offset(3), 0),
            (1, pdf.Offset(10), 0),
            (1, pdf.Offset(11), 0));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("ten", Text(document, 10));
        Assert.Equal("eleven", Text(document, 11));
        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(4, 0)));
        Assert.Single(document.Pages);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Wide_fields_are_read_big_endian()
    {
        var pdf = new XrefStreamPdf().AddOnePage().Add(4, "(four)");
        byte[] file = pdf.Finish(
            5,
            "/Size 5 /Root 1 0 R /W [2 8 3]",
            [2, 8, 3],
            (0, 0, 65535),
            (1, pdf.Offset(1), 0),
            (1, pdf.Offset(2), 0),
            (1, pdf.Offset(3), 0),
            (1, pdf.Offset(4), 0));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("four", Text(document, 4));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_entry_of_an_unknown_type_is_a_reference_to_the_null_object()
    {
        // Table 18: "Any other value shall be interpreted as a reference to the null object", here hiding an object a classic reader would find.
        var pdf = new XrefStreamPdf().AddOnePage().Add(4, "(hidden)");
        byte[] file = pdf.Finish(5, "/Size 5 /Root 1 0 R /W [1 2 1]", W121, (0, 0, 255), (1, pdf.Offset(1), 0), (1, pdf.Offset(2), 0), (1, pdf.Offset(3), 0), (7, pdf.Offset(4), 0));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(4, 0)));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Data_shorter_than_the_entries_ends_the_section_where_the_data_ends()
    {
        var pdf = new XrefStreamPdf().AddOnePage().Add(4, "(four)");
        byte[] rows = XrefStreamPdf.Rows(W121, (0, 0, 255), (1, pdf.Offset(1), 0), (1, pdf.Offset(2), 0), (1, pdf.Offset(3), 0), (1, pdf.Offset(4), 0));
        byte[] file = pdf.Finish(5, "/Size 5 /Root 1 0 R /W [1 2 1]", rows[..^2]);

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Single(document.Pages);
        Assert.Same(CosNull.Instance, document.Resolve(new CosReference(4, 0)));
        Assert.Equal("XrefStreamDataTruncated", Assert.Single(document.Diagnostics).Code);
    }

    // Found by libFuzzer (issue #48): Flate expands a cross-reference stream about 1000 times and every entry costs tens of bytes
    // in the cross-reference dictionaries, so an 11 KB file made the reader allocate 2.3 GB. A document reads at most one stream
    // entry per byte of the file (and at least 2^20), whatever the data holds.
    [Fact]
    public void A_cross_reference_stream_holds_no_more_entries_than_the_file_has_bytes()
    {
        const int freeEntries = 8_000_000;
        var pdf = new XrefStreamPdf().AddOnePage();
        byte[] head = XrefStreamPdf.Rows([1, 4, 0], (1, pdf.Offset(1), 0), (1, pdf.Offset(2), 0), (1, pdf.Offset(3), 0));
        byte[] data = new byte[head.Length + (freeEntries * 5)];
        head.CopyTo(data, 0);
        byte[] file = pdf.Finish(4, $"/Size {freeEntries + 4} /Index [1 3 4 {freeEntries}] /Root 1 0 R /W [1 4 0] /Filter /FlateDecode", FilterEncoders.Zlib(data));
        Assert.InRange(file.Length, 0, 100_000);

        long allocated = Allocations.Measure(
            () =>
            {
                using PdfDocument opened = PdfDocument.Open(file);
                _ = opened.Pages.Count;
            },
            warmUpCalls: 0);
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Single(document.Pages);
        Assert.Equal(["CrossReferenceEntryLimitExceeded"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.InRange(allocated, 0, 384L << 20); // mostly the 40 MB of decoded data and its growing buffers; 8 million entries took gigabytes
    }

    [Fact]
    public void A_stream_without_type_XRef_is_read_with_a_diagnostic()
    {
        var pdf = new XrefStreamPdf().AddOnePage();
        string text = Encoding.Latin1.GetString(pdf.Finish(4, "/Size 4 /Root 1 0 R /W [1 2 1]", W121, (0, 0, 255), (1, pdf.Offset(1), 0), (1, pdf.Offset(2), 0), (1, pdf.Offset(3), 0)));
        byte[] file = Encoding.Latin1.GetBytes(text.Replace("/Type /XRef", "/Type /XRf", StringComparison.Ordinal));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Single(document.Pages);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("XrefStreamTypeInvalid", diagnostic.Code);
        Assert.Equal(new CosReference(4, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void A_stream_whose_W_cannot_be_read_is_not_a_cross_reference_section_so_the_file_is_rebuilt_by_scanning()
    {
        var pdf = new XrefStreamPdf().AddOnePage();
        byte[] file = pdf.Finish(4, "/Size 4 /Root 1 0 R /W [1 2]", W121, (0, 0, 255), (1, pdf.Offset(1), 0), (1, pdf.Offset(2), 0), (1, pdf.Offset(3), 0));

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Single(document.Pages);
        Assert.Equal(["XrefStreamWidthsInvalid"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
        Assert.Equal(new CosReference(1, 0), document.Trailer[new CosName("Root")]);
        Assert.Throws<DiagnosticException>(() => PdfDocument.Open(file, new PdfOptions().UseStrict()));
    }

    [Fact]
    public void An_update_written_as_a_cross_reference_stream_overrides_the_original_section()
    {
        // §7.5.6 and §7.5.8.1: an update's stream has Prev pointing at the previous section, a classic table here.
        byte[] original = TestPdf.OnePage("/MediaBox [0 0 612 792]");
        string text = Encoding.Latin1.GetString(original);
        int previous = int.Parse(text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 9)..].Trim().Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
        var update = new StringBuilder(text);
        int pageOffset = update.Length;
        update.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 595 842] >>\nendobj\n");
        int streamOffset = update.Length;
        byte[] rows = XrefStreamPdf.Rows(W121, (1, pageOffset, 0), (1, streamOffset, 0));
        update.Append(System.Globalization.CultureInfo.InvariantCulture, $"4 0 obj\n<< /Type /XRef /Size 5 /Index [3 2] /W [1 2 1] /Root 1 0 R /Prev {previous} /Length {rows.Length} >>\nstream\n");
        update.Append(Encoding.Latin1.GetString(rows)).Append(System.Globalization.CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{streamOffset}\n%%EOF\n");
        byte[] file = Encoding.Latin1.GetBytes(update.ToString());

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(595, document.Pages[0].MediaBox.Width);
        Assert.Equal([original.LongLength, file.LongLength], document.Revisions.Select(revision => revision.Length));
        Assert.Empty(document.Diagnostics);
    }

    private static string Text(PdfDocument document, int number) =>
        Assert.IsType<CosString>(document.Resolve(new CosReference(number, 0))).DecodeText();
}
