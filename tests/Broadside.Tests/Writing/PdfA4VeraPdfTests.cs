using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Files Broadside saves as PDF/A-4 and PDF/A-4f (issue #203) validated by veraPDF's PDF/A-4 and PDF/A-4F profiles: new and read
/// documents, a document information dictionary moved to the packet, an associated XML source in 4f, and an incremental update. A
/// failure lists the veraPDF rules (ISO 19005-4 clause and test number) the file breaks. Text in embedded fonts is in the Standard 14
/// tests' <c>PdfA4Tests</c>.
/// </summary>
[Trait("Category", "VeraPdf")]
public sealed class PdfA4VeraPdfTests
{
    [Fact]
    public void A_canvas_drawn_document_saved_as_pdf_a_4_passes_verapdf()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Broadside PDF/A-4 sample";
        document.Properties.Author = "Broadside";
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        PdfA2u3bConformanceTests.DrawType3Text(document, "A", toUnicode: "<41> <0041>");

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is DiagnosticCodes.PdfANonConformant or DiagnosticCodes.PdfANotIdentified);
        Assert.Empty(VeraPdf.FailedRules(saved, "4"));
    }

    [Fact]
    public void A_document_with_an_xml_source_attachment_saved_as_pdf_a_4f_passes_verapdf()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Invoice 42";
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        PdfA2u3bConformanceTests.AttachXmlSource(document);

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4f);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is DiagnosticCodes.PdfANonConformant or DiagnosticCodes.PdfANotIdentified);
        Assert.Empty(VeraPdf.FailedRules(saved, "4f"));
    }

    [Theory]
    [InlineData("empty-page.pdf")]
    [InlineData("info-dictionary.pdf")]
    [InlineData("metadata-xmp.pdf")]
    public void A_read_pdf_1_x_file_saved_as_pdf_a_4_passes_verapdf(string file)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file));

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is DiagnosticCodes.PdfANonConformant or DiagnosticCodes.PdfANotIdentified);
        Assert.Empty(VeraPdf.FailedRules(saved, "4"));
    }

    [Fact]
    public void A_file_with_piece_information_keeping_only_the_modification_date_passes_verapdf()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("info-dictionary.pdf"));
        document.Catalog[new CosName("PieceInfo")] = new CosDictionary();

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4);

        Assert.Empty(VeraPdf.FailedRules(saved, "4"));
    }

    [Fact]
    public void An_incremental_pdf_a_4f_update_adding_an_attachment_passes_verapdf()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        PdfA2u3bConformanceTests.AttachXmlSource(document);
        using PdfDocument reopened = PdfDocument.Open(PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4f));
        PdfFileSpecification file = reopened.CreateEmbeddedFile("notes.txt", "notes"u8, new PdfEmbeddedFileOptions().WithMediaType("text/plain").WithRelationship(PdfFileRelationship.Supplement));
        reopened.AddEmbeddedFile("notes.txt", file);
        reopened.AddAssociatedFile(reopened.Catalog, file);

        byte[] updated = PdfA2u3bConformanceTests.Save(reopened, PdfAConformance.PdfA4f, incremental: true);

        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(updated, "4f"));
    }

    [Fact]
    public void A_file_saved_as_pdf_a_2b_fails_the_pdf_a_4_header_rule_verapdf_checks()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawRgbSquare(document);

        byte[] asPdfA2 = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2b);

        Assert.Contains(VeraPdf.FailedRules(asPdfA2, "4"), rule => rule.StartsWith("6.1.2-1:", StringComparison.Ordinal));
    }
}
