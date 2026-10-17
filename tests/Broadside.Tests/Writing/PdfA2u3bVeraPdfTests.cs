using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Tests.Writing;

/// <summary>
/// Files Broadside saves as PDF/A-2u, PDF/A-3b and PDF/A-3u (issue #202) validated by veraPDF's profiles of the same flavour: text in a
/// Type 3 font mapped to Unicode, and an XML source file attached as an associated file. A failure lists the veraPDF rules (ISO 19005
/// clause and test number) the file breaks. Text in embedded TrueType fonts is in the Standard 14 tests' <c>PdfA2bTests</c>.
/// </summary>
[Trait("Category", "VeraPdf")]
public sealed class PdfA2u3bVeraPdfTests
{
    [Theory]
    [InlineData(PdfAConformance.PdfA2u, "2u")]
    [InlineData(PdfAConformance.PdfA3b, "3b")]
    [InlineData(PdfAConformance.PdfA3u, "3u")]
    public void Text_mapped_to_unicode_saved_at_each_level_passes_verapdf(PdfAConformance conformance, string flavour)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = $"Broadside PDF/A-{flavour} sample";
        PdfA2u3bConformanceTests.DrawType3Text(document, "g1", toUnicode: "<41> <0041>");

        byte[] saved = PdfA2u3bConformanceTests.Save(document, conformance);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is DiagnosticCodes.PdfANonConformant or DiagnosticCodes.PdfANotIdentified);
        Assert.Empty(VeraPdf.FailedRules(saved, flavour));
    }

    [Fact]
    public void A_glyph_verapdf_finds_unmapped_is_the_one_a_pdf_a_2u_save_reports()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawType3Text(document, "g1", toUnicode: null);

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2u);
        byte[] asLevelB = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2b);

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.PdfANonConformant && diagnostic.Message.Contains("6.2.11.7.2-1", StringComparison.Ordinal));
        Assert.Empty(VeraPdf.FailedRules(asLevelB, "2b"));
        Assert.Contains(VeraPdf.FailedRules(asLevelB, "2u"), rule => rule.StartsWith("6.2.11.7.2-1:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA3b, "3b")]
    [InlineData(PdfAConformance.PdfA3u, "3u")]
    public void A_document_with_an_xml_source_attachment_saved_as_pdf_a_3_passes_verapdf(PdfAConformance conformance, string flavour)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Invoice 42";
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        PdfA2u3bConformanceTests.AttachXmlSource(document);

        byte[] saved = PdfA2u3bConformanceTests.Save(document, conformance);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is DiagnosticCodes.PdfANonConformant or DiagnosticCodes.PdfANotIdentified);
        Assert.Empty(VeraPdf.FailedRules(saved, flavour));
    }

    [Fact]
    public void An_incremental_pdf_a_3b_update_adding_an_attachment_passes_verapdf()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        using PdfDocument reopened = PdfDocument.Open(PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA3b));
        PdfA2u3bConformanceTests.AttachXmlSource(reopened);

        byte[] updated = PdfA2u3bConformanceTests.Save(reopened, PdfAConformance.PdfA3b, incremental: true);

        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(updated, "3b"));
    }
}
