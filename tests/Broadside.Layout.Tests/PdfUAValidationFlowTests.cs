using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests;

/// <summary>
/// <see cref="PdfDocument.ValidatePdfUA"/> on flow documents (issue #652): a report saved as PDF/UA-1 or PDF/UA-2 and read back
/// validates with no violations, and the same report before its save fails exactly the rules the save writes, without being changed.
/// ISO 14289-1 clauses 5 to 7; ISO 14289-2 clauses 5 to 8; WTPDF 1.0 clause 8.
/// </summary>
public sealed class PdfUAValidationFlowTests
{
    private const PdfWellTaggedConformance BothLevels = PdfWellTaggedConformance.Reuse | PdfWellTaggedConformance.Accessibility;

    [Fact]
    public void A_flow_document_saved_as_PDF_UA_1_validates_with_no_violations()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, FlowTagging.Default.WithLanguage("en-US").WithTitle("Quarterly report").WithNamespace(FlowStructureNamespace.Pdf17)).Render(document);
        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfUAValidation validation = reopened.ValidatePdfUA();

        Assert.Equal(PdfUAConformance.PdfUA1, validation.Conformance);
        Assert.True(validation.IsClaimed);
        Assert.Empty(validation.Violations);
        Assert.NotEmpty(validation.CheckpointsNotChecked);
        Assert.NotEmpty(validation.MachineUncheckable);
    }

    [Fact]
    public void A_flow_document_saved_as_PDF_UA_2_and_declared_well_tagged_validates_with_no_violations()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, FlowTagging.Default.WithLanguage("en-US").WithTitle("Quarterly report")).Render(document);
        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2).WithWellTaggedConformance(BothLevels));

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfUAValidation validation = reopened.ValidatePdfUA();
        PdfUAValidation asPart1 = reopened.ValidatePdfUA(PdfUAConformance.PdfUA1);

        Assert.Equal(PdfUAConformance.PdfUA2, validation.Conformance);
        Assert.True(validation.IsClaimed);
        Assert.Empty(validation.Violations);
        Assert.Equal(BothLevels, validation.WellTagged);
        Assert.Equal(BothLevels, validation.WellTaggedConformant);
        Assert.False(asPart1.IsClaimed);
        Assert.Contains(asPart1.Violations, violation => violation is { Rule: "5-2", Checkpoint: "06-002" });
    }

    [Fact]
    public void Before_its_save_a_flow_document_fails_only_what_the_save_writes_and_is_not_changed()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, FlowTagging.Default.WithLanguage("en-US").WithTitle("Quarterly report").WithNamespace(FlowStructureNamespace.Pdf17)).Render(document);
        bool marked = document.MarkInfo.Marked;
        bool hasMetadata = document.Metadata is not null;

        PdfUAValidation validation = document.ValidatePdfUA(PdfUAConformance.PdfUA1);

        Assert.False(validation.IsClaimed);
        Assert.NotEmpty(validation.Violations);
        Assert.All(validation.Violations, violation => Assert.Contains(violation.Rule, (string[])["5-1", "6.2-1", "7.1-8", "7.1-9", "7.1-10", "7.18.3-1"]));
        Assert.Contains(validation.Violations, violation => violation.Rule == "5-1");
        Assert.Equal(marked, document.MarkInfo.Marked);
        Assert.Equal(hasMetadata, document.Metadata is not null);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "PdfUANonConformant" or "PdfUANotIdentified");
    }
}
