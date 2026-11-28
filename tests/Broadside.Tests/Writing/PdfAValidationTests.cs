using System.Text;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Validating a document against PDF/A without converting it (issue #651): the level the file claims or the one asked for, the save's
/// rules on the file as it is, the file's own header, trailer, output intents and XMP identification, and every repair the parser
/// made as a violation. veraPDF's own verdicts on its test suite are compared in <see cref="PdfAValidationVeraPdfAgreementTests"/>.
/// </summary>
public sealed class PdfAValidationTests
{
    private static readonly CosName OutputIntents = new("OutputIntents");

    [Theory]
    [InlineData(PdfAConformance.PdfA2b)]
    [InlineData(PdfAConformance.PdfA2u)]
    [InlineData(PdfAConformance.PdfA3b)]
    [InlineData(PdfAConformance.PdfA3u)]
    [InlineData(PdfAConformance.PdfA4)]
    public void A_file_broadside_saved_as_pdf_a_validates_at_the_level_it_claims(PdfAConformance conformance)
    {
        using PdfDocument document = PdfDocument.Open(Written(conformance));

        PdfAValidation validation = document.ValidatePdfA();

        Assert.Equal(conformance, validation.Conformance);
        Assert.True(validation.IsClaimed);
        Assert.Empty(validation.Violations);
        Assert.True(validation.IsConformant);
    }

    [Fact]
    public void A_pdf_a_4f_file_with_an_associated_attachment_validates()
    {
        using PdfDocument created = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawRgbSquare(created);
        PdfA2u3bConformanceTests.AttachXmlSource(created);
        using PdfDocument document = PdfDocument.Open(PdfA2u3bConformanceTests.Save(created, PdfAConformance.PdfA4f));

        PdfAValidation validation = document.ValidatePdfA();

        Assert.Equal(PdfAConformance.PdfA4f, validation.Conformance);
        Assert.Empty(validation.Violations);
    }

    [Fact]
    public void An_incremental_update_of_a_pdf_a_file_is_validated_as_its_last_revision()
    {
        using PdfDocument first = PdfDocument.Open(Written(PdfAConformance.PdfA2b));
        first.Properties.Title = "Updated";
        using PdfDocument document = PdfDocument.Open(PdfA2u3bConformanceTests.Save(first, PdfAConformance.PdfA2b, incremental: true));

        PdfAValidation validation = document.ValidatePdfA();

        Assert.Equal(2, document.Revisions.Count);
        Assert.Empty(validation.Violations);
    }

    [Fact]
    public void Validation_changes_nothing_in_the_document()
    {
        using PdfDocument document = PdfDocument.Open(Written(PdfAConformance.PdfA2b));
        document.Catalog.Remove(OutputIntents);
        string before = CosSnapshot.Of(document);

        _ = document.ValidatePdfA();

        Assert.Equal(before, CosSnapshot.Of(document));
    }

    [Fact]
    public void A_file_whose_cross_reference_table_is_rebuilt_is_never_conformant()
    {
        byte[] file = Written(PdfAConformance.PdfA2b);
        string text = Encoding.Latin1.GetString(file);
        int keyword = text.LastIndexOf("startxref", StringComparison.Ordinal);
        int digits = keyword + "startxref\n".Length;
        int end = text.IndexOf('\n', digits);
        // An offset of 0 names the header, not a cross-reference section: the parser rebuilds the table by scanning the file.
        byte[] damaged = Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, digits), new string('0', end - digits), text.AsSpan(end)));
        using PdfDocument document = PdfDocument.Open(damaged);

        PdfAValidation validation = document.ValidatePdfA();

        Assert.False(validation.IsConformant);
        Assert.Contains(validation.Violations, violation => violation.Rule.StartsWith("ISO 32000-2 7.5", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_with_a_rebuilt_cross_reference_table_and_no_trailer_id_fails_6_1_3()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("broken-xref-offsets.pdf"));

        PdfAValidation validation = document.ValidatePdfA(PdfAConformance.PdfA2b);

        Assert.False(validation.IsConformant);
        Assert.Contains(validation.Violations, violation => violation.Rule == "6.1.3-1");
        Assert.Contains(validation.Violations, violation => violation.Rule.StartsWith("ISO 32000-2 7.", StringComparison.Ordinal));
    }

    [Fact]
    public void Data_after_the_end_of_file_marker_and_a_header_without_its_binary_comment_fail_6_1_3_3_and_6_1_2_2()
    {
        byte[] file = Written(PdfAConformance.PdfA2b);
        string text = Encoding.Latin1.GetString(file);
        int comment = text.IndexOf('\n', StringComparison.Ordinal) + 1;
        // The comment's four bytes above 127 become ASCII, the same length so no offset moves.
        string damaged = string.Concat(text.AsSpan(0, comment + 1), "abcd", text.AsSpan(comment + 5)) + "garbage\n";
        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(damaged));

        PdfAValidation validation = document.ValidatePdfA();

        Assert.Equal(["6.1.2-2", "6.1.3-3"], validation.Violations.Select(violation => violation.Rule).Where(rule => rule.StartsWith("6.1.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_pdf_a_2_header_above_1_7_fails_6_1_2_1()
    {
        byte[] file = Written(PdfAConformance.PdfA2b);
        file[7] = (byte)'8';
        using PdfDocument document = PdfDocument.Open(file);

        PdfAValidation validation = document.ValidatePdfA();

        Assert.Contains(validation.Violations, violation => violation.Rule == "6.1.2-1");
    }

    [Fact]
    public void A_missing_output_intent_fails_the_device_colour_rule()
    {
        using PdfDocument document = PdfDocument.Open(Written(PdfAConformance.PdfA2b));
        document.Catalog.Remove(OutputIntents);

        PdfAValidation validation = document.ValidatePdfA();

        Assert.True(validation.IsClaimed);
        Assert.Contains(validation.Violations, violation => violation.Rule == "6.2.4.3-2");
    }

    [Fact]
    public void Device_gray_without_an_output_intent_fails_6_2_4_3_4()
    {
        using PdfDocument created = PdfDocument.Create();
        using (Broadside.Graphics.PdfCanvas canvas = created.Pages[0].CreateCanvas())
        {
            canvas.SetFillColor(Broadside.Graphics.PdfColor.FromGray(0.5f));
            canvas.Rectangle(72, 72, 100, 100);
            canvas.Fill();
        }

        using PdfDocument document = PdfDocument.Open(PdfA2u3bConformanceTests.Save(created, PdfAConformance.PdfA2b));
        Assert.Empty(document.ValidatePdfA().Violations);
        document.Catalog.Remove(OutputIntents);

        Assert.Contains(document.ValidatePdfA().Violations, violation => violation.Rule == "6.2.4.3-4");
    }

    [Fact]
    public void A_file_claiming_pdf_a_2b_with_an_lzw_stream_fails_6_1_7_2_1()
    {
        using PdfDocument document = PdfDocument.Open(Written(PdfAConformance.PdfA2b));
        var form = new CosStream(
            new CosDictionary
            {
                [KnownNames.Type] = new CosName("XObject"),
                [new CosName("Subtype")] = new CosName("Form"),
                [new CosName("BBox")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(10), new CosInteger(10)]),
                [new CosName("Filter")] = new CosName("LZWDecode"),
            },
            new byte[] { 0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01 });
        CosReference reference = document.AddObject(form);
        document.Pages[0].Resources![new CosName("XObject")] = new CosDictionary { [new CosName("X1")] = reference };

        PdfAValidation validation = document.ValidatePdfA();

        Assert.Contains(validation.Violations, violation => violation.Rule == "6.1.7.2-1");
        Assert.False(validation.IsConformant);
    }

    [Fact]
    public void An_encrypted_file_reports_the_encryption_rule_and_is_still_checked()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-aes-128.pdf"));

        PdfAValidation validation = document.ValidatePdfA(PdfAConformance.PdfA2b);

        Assert.False(validation.IsClaimed);
        Assert.Contains(validation.Violations, violation => violation.Rule == "6.1.3-2");
        Assert.Contains(validation.Violations, violation => violation.Rule == "6.6.2.1-1");
    }

    [Fact]
    public void A_level_asked_for_that_the_file_does_not_claim_reports_the_identification()
    {
        using PdfDocument document = PdfDocument.Open(Written(PdfAConformance.PdfA2b));

        PdfAValidation u = document.ValidatePdfA(PdfAConformance.PdfA2u);
        PdfAValidation three = document.ValidatePdfA(PdfAConformance.PdfA3b);
        PdfAValidation four = document.ValidatePdfA(PdfAConformance.PdfA4);

        Assert.False(u.IsClaimed);
        Assert.Equal(["6.6.4-3"], u.Violations.Select(violation => violation.Rule));
        Assert.Equal(["6.6.4-2"], three.Violations.Select(violation => violation.Rule));
        Assert.Contains(four.Violations, violation => violation.Rule == "6.1.2-1");
        Assert.Contains(four.Violations, violation => violation.Rule == "6.7.3-2");
        Assert.Contains(four.Violations, violation => violation.Rule == "6.7.3-5");
    }

    [Fact]
    public void A_pdf_a_2b_file_validated_as_pdf_a_2b_accepts_a_stricter_identification()
    {
        using PdfDocument document = PdfDocument.Open(Written(PdfAConformance.PdfA2u));

        PdfAValidation validation = document.ValidatePdfA(PdfAConformance.PdfA2b);

        Assert.False(validation.IsClaimed);
        Assert.Empty(validation.Violations);
    }

    [Fact]
    public void An_unclaimed_file_without_a_level_throws()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => document.ValidatePdfA());

        Assert.Contains("pass a level", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(document.ValidatePdfA(PdfAConformance.PdfA2b).Violations, violation => violation.Rule == "6.6.2.1-1");
    }

    [Fact]
    public void A_file_claiming_a_level_broadside_cannot_check_throws_naming_it()
    {
        using PdfDocument document = PdfDocument.Open(Written(PdfAConformance.PdfA2b));
        byte[] packet = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(document.Metadata!.Data.Span).Replace("<pdfaid:part>2</pdfaid:part>", "<pdfaid:part>1</pdfaid:part>", StringComparison.Ordinal));
        document.Catalog[new CosName("Metadata")] = document.AddObject(new CosStream(
            new CosDictionary { [KnownNames.Type] = new CosName("Metadata"), [new CosName("Subtype")] = new CosName("XML") },
            packet));

        NotSupportedException thrown = Assert.Throws<NotSupportedException>(() => document.ValidatePdfA());

        Assert.Contains("PDF/A-1b", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void None_is_not_a_level_to_validate_against()
    {
        using PdfDocument document = PdfDocument.Open(Written(PdfAConformance.PdfA2b));

        Assert.Throws<ArgumentOutOfRangeException>(() => document.ValidatePdfA(PdfAConformance.None));
    }

    [Fact]
    public void Rules_not_checked_list_the_rules_issue_533_tracks()
    {
        using PdfDocument document = PdfDocument.Open(Written(PdfAConformance.PdfA2b));

        IReadOnlyList<string> part2 = document.ValidatePdfA().RulesNotChecked;
        IReadOnlyList<string> part4 = document.ValidatePdfA(PdfAConformance.PdfA4).RulesNotChecked;

        Assert.Superset(new HashSet<string>(["6.2.5-5", "6.2.5-6", "6.2.8-4", "6.2.8-5", "6.2.8.3", "6.2.11.2", "6.4.3", "6.8-5"]), part2.ToHashSet());
        Assert.Superset(new HashSet<string>(["6.1.6", "6.1.9", "6.2.5-5", "6.2.5-6", "6.2.7.1-4", "6.2.7.1-5", "6.2.7.3", "6.2.8", "6.4", "6.10"]), part4.ToHashSet());
    }

    [Fact]
    public void A_violation_names_the_object_it_was_found_on()
    {
        using PdfDocument document = PdfDocument.Open(Written(PdfAConformance.PdfA2b));
        var annotation = new CosDictionary
        {
            [KnownNames.Type] = new CosName("Annot"),
            [new CosName("Subtype")] = new CosName("Sound"),
            [new CosName("Rect")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(10), new CosInteger(10)]),
            [new CosName("F")] = new CosInteger(4),
        };
        CosReference reference = document.AddObject(annotation);
        document.Pages[0].Dictionary[new CosName("Annots")] = new CosArray([reference]);

        PdfARuleViolation violation = Assert.Single(document.ValidatePdfA().Violations, violation => violation.Rule == "6.3.1-1");

        Assert.Equal(reference, violation.Reference);
    }

    [Fact]
    public void Validation_runs_concurrently_on_threads_reading_the_same_document()
    {
        using PdfDocument document = PdfDocument.Open(Written(PdfAConformance.PdfA3u));

        PdfAValidation[] results = new PdfAValidation[8];
        Parallel.For(0, results.Length, index => results[index] = document.ValidatePdfA());

        Assert.All(results, result => Assert.Empty(result.Violations));
    }

    internal static byte[] Written(PdfAConformance conformance)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Archive";
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        return PdfA2u3bConformanceTests.Save(document, conformance);
    }
}
