using Broadside.Diagnostics;
using Broadside.Layout.Flow;
using Broadside.Tests.Writing;

namespace Broadside.Layout.Tests;

/// <summary>
/// PDF/A level A from flow layout (issue #650): a tagged flow document is already what PDF/A-2a and PDF/A-3a ask for (ISO 19005-2 and
/// -3 clause 6.7), so it saves as level A with no diagnostic and passes veraPDF's profiles; the document builder asks for tagging and a
/// language at level A, and allows attachments at PDF/A-3a and PDF/A-4e.
/// </summary>
public sealed class PdfALevelAFlowTests
{
    private static FlowTagging Tagged => FlowTagging.Default
        .WithLanguage("en-US")
        .WithTitle("Quarterly report")
        .WithNamespace(FlowStructureNamespace.Pdf17);

    [Theory]
    [InlineData(PdfAConformance.PdfA2a)]
    [InlineData(PdfAConformance.PdfA3a)]
    public void A_tagged_flow_document_saves_as_level_A_with_no_diagnostic(PdfAConformance conformance)
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, Tagged).Render(document);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfAConformance(conformance));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal("A", reopened.Metadata?.Packet?.PdfAConformance);
    }

    [Theory]
    [Trait("Category", "VeraPdf")]
    [InlineData(PdfAConformance.PdfA2a, "2a")]
    [InlineData(PdfAConformance.PdfA3a, "3a")]
    public void A_tagged_flow_document_saved_at_level_A_passes_verapdf(PdfAConformance conformance, string flavour)
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, Tagged).Render(document);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfAConformance(conformance));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, flavour));
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_flow_document_saved_as_PDF_A_2a_and_PDF_UA_1_passes_both_verapdf_profiles()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, Tagged).Render(document);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA2a).WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, "2a"));
        Assert.Empty(VeraPdf.FailedRules(saved, "ua1"));
    }

    [Fact]
    public void The_builder_asks_for_tagging_and_a_language_at_level_A()
    {
        string untagged = Assert.Throws<InvalidOperationException>(() => PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en")
            .WithTagging(false).WithPdfAConformance(PdfAConformance.PdfA2a).Content(c => c.Paragraph("x")))).Message;
        string unlabeled = Assert.Throws<InvalidOperationException>(() => PdfDocument.Create(doc => doc.WithTitle("t")
            .WithPdfAConformance(PdfAConformance.PdfA3a).Content(c => c.Paragraph("x")))).Message;

        Assert.Contains("PDF/A-2a needs tagged output", untagged, StringComparison.Ordinal);
        Assert.Contains("PDF/A-3a needs the document's language", unlabeled, StringComparison.Ordinal);
        using PdfDocument built = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").WithPdfAConformance(PdfAConformance.PdfA2a).Content(c => c.Paragraph("x")));
        PdfUAFlowTests.Saved(built, built.DefaultSaveOptions!);
        Assert.DoesNotContain(built.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA3a)]
    [InlineData(PdfAConformance.PdfA4e)]
    public void The_builder_allows_attachments_at_PDF_A_3a_and_PDF_A_4e(PdfAConformance level)
    {
        using PdfDocument document = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").WithPdfAConformance(level)
            .AttachFile("data.csv", "a,b\n1,2\n", PdfFileRelationship.Data).Content(c => c.Paragraph("x")));

        Assert.Single(document.EmbeddedFiles);
    }
}
