using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Files Broadside saves as PDF/A-2a, PDF/A-3a and PDF/A-4e (issue #650) validated by veraPDF's profiles of the same flavour: a tagged
/// document at level A, and a 3D annotation kept as stored at 4e. A failure lists the veraPDF rules (ISO 19005 clause and test
/// number) the file breaks. Flow-layout documents saved at level A are in <c>Broadside.Layout.Tests</c>.
/// </summary>
[Trait("Category", "VeraPdf")]
public sealed class PdfALevelAVeraPdfTests
{
    [Theory]
    [InlineData(PdfAConformance.PdfA2a, "2a")]
    [InlineData(PdfAConformance.PdfA3a, "3a")]
    public void A_tagged_document_saved_at_level_A_passes_verapdf(PdfAConformance conformance, string flavour)
    {
        using PdfDocument document = PdfALevelAConformanceTests.TaggedDocument();
        PdfALevelAConformanceTests.AddFigure(document, "Figure").AlternateDescription = "A blue square";

        byte[] saved = PdfA2u3bConformanceTests.Save(document, conformance);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Kind == SavingDiagnostics.PdfANonConformant || diagnostic.Kind == SavingDiagnostics.PdfANotIdentified);
        Assert.Empty(VeraPdf.FailedRules(saved, flavour));
    }

    [Fact]
    public void An_unmapped_structure_type_verapdf_finds_is_the_one_a_pdf_a_2a_save_reports()
    {
        using PdfDocument document = PdfALevelAConformanceTests.TaggedDocument(type: "Para");

        byte[] asLevelU = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2u);
        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA2a);

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == SavingDiagnostics.PdfANonConformant.Name && diagnostic.Message.Contains("6.7.3.4-1", StringComparison.Ordinal));
        Assert.Empty(VeraPdf.FailedRules(asLevelU, "2u"));
        Assert.Contains(VeraPdf.FailedRules(asLevelU, "2a"), rule => rule.StartsWith("6.7.3.4-1:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_3d_annotation_saved_as_pdf_a_4e_passes_verapdf()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pdfa4e-3d.pdf"));

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4e);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, "4e"));
    }

    [Fact]
    public void An_untagged_document_converted_to_pdf_a_2a_passes_verapdf()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Converted";
        PdfA2u3bConformanceTests.DrawType3Text(document, "g1", toUnicode: "<41> <0041>");
        document.Pages[0].Dictionary[new CosName("Contents")] = document.AddObject(new CosStream(new CosDictionary(), "BT /T3 24 Tf 72 700 Td (AA) Tj ET"u8.ToArray()));
        document.Catalog[new CosName("Lang")] = new CosString("en-US"u8.ToArray());

        PdfAConversion conversion = document.ConvertToPdfA(PdfAConformance.PdfA2a);
        using var stream = new MemoryStream();
        document.Save(stream, conversion.CreateSaveOptions());

        Assert.True(conversion.IsConformant);
        Assert.Empty(VeraPdf.FailedRules(stream.ToArray(), "2a"));
    }
}
