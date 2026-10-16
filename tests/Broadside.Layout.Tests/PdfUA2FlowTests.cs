using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Writing;

namespace Broadside.Layout.Tests;

/// <summary>
/// PDF/UA-2 and Well-Tagged PDF output from flow layout (issue #205): a tagged flow document in the PDF 2.0 structure namespace saved
/// with <see cref="PdfUAConformance.PdfUA2"/> and the WTPDF declarations passes veraPDF's PDF/UA-2 and WTPDF 1.0 profiles, alone and
/// with PDF/A-4; each failure condition is reported under its veraPDF rule identifier, refused in strict mode and not claimed in
/// lenient mode.
/// </summary>
public sealed class PdfUA2FlowTests
{
    private const string NonConformant = "PdfUANonConformant";
    private const string NotIdentified = "PdfUANotIdentified";
    private const string WellTaggedNonConformant = "WellTaggedNonConformant";
    private const string WellTaggedNotDeclared = "WellTaggedNotDeclared";
    private const PdfWellTaggedConformance BothLevels = PdfWellTaggedConformance.Reuse | PdfWellTaggedConformance.Accessibility;

    private static readonly ReadOnlyMemory<byte> Sans = Standard14FontResolver.GetProgram(Standard14Font.Helvetica);

    private static FlowTagging Accessible => FlowTagging.Default.WithLanguage("en-US").WithTitle("Quarterly report");

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_report_with_headings_lists_a_table_and_a_figure_passes_verapdf_PDF_UA_2()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, Accessible).Render(document);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, "ua2"));
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_report_declared_well_tagged_passes_verapdf_PDF_UA_2_and_both_WTPDF_profiles()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, Accessible).Render(document);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2).WithWellTaggedConformance(BothLevels));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, "ua2"));
        Assert.Empty(VeraPdf.FailedRules(saved, "wt1r"));
        Assert.Empty(VeraPdf.FailedRules(saved, "wt1a"));
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_report_saved_as_PDF_A_4_and_PDF_UA_2_passes_both_verapdf_profiles()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, Accessible).Render(document);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2).WithPdfAConformance(PdfAConformance.PdfA4).WithWellTaggedConformance(BothLevels));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, "4"));
        Assert.Empty(VeraPdf.FailedRules(saved, "ua2"));
        Assert.Empty(VeraPdf.FailedRules(saved, "wt1a"));
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_figure_without_alternate_text_is_declared_for_reuse_only_and_passes_verapdf_WTPDF_reuse()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        Build(document, "figure without alternate text");

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithWellTaggedConformance(BothLevels));

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == WellTaggedNonConformant && diagnostic.Message.Contains("accessibility", StringComparison.Ordinal) && diagnostic.Message.Contains("veraPDF rule 8.2.5.28.2-1,", StringComparison.Ordinal));
        Diagnostic notDeclared = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == WellTaggedNotDeclared);
        Assert.Contains("accessibility", notDeclared.Message, StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal([PdfDeclaration.WellTaggedReuse], reopened.Declarations.Select(declaration => declaration.ConformsTo));
        Assert.Null(reopened.Metadata!.Packet!.PdfUAPart);
        Assert.Empty(VeraPdf.FailedRules(saved, "wt1r"));
    }

    [Fact]
    public void The_save_identifies_PDF_UA_2_declares_WTPDF_and_writes_a_PDF_2_0_file_that_reads_back()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, Accessible).Render(document);
        document.Catalog.Remove(new CosName("ViewerPreferences"));
        document.MarkInfo.Marked = false;

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2).WithWellTaggedConformance(BothLevels));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is NonConformant or NotIdentified or WellTaggedNonConformant or WellTaggedNotDeclared);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(2, reopened.Metadata!.Packet!.PdfUAPart);
        Assert.Equal(2024, reopened.Metadata.Packet.PdfUARevision);
        Assert.Equal([PdfDeclaration.WellTaggedReuse, PdfDeclaration.WellTaggedAccessibility], reopened.Declarations.Select(declaration => declaration.ConformsTo));
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        Assert.True(reopened.ViewerPreferences!.DisplayDocTitle);
        Assert.True(reopened.MarkInfo.Marked);
        Assert.Contains(reopened.Pages, page => page.Annotations.Count > 0);
        foreach (PdfPage page in reopened.Pages.Where(page => page.Annotations.Count > 0))
        {
            Assert.Equal(new CosName("S"), page.Dictionary[new CosName("Tabs")]);
        }

        PdfStructureElement list = Assert.Single(Descendants(reopened.StructureTree!.Children[0]), element => element.StandardType?.Name == "L");
        Assert.Equal(new CosName("Disc"), list.GetAttributeValue(new CosName("List"), new CosName("ListNumbering")));
    }

    [Fact]
    public void Saving_again_keeps_one_declaration_per_level()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, Accessible).Render(document);
        byte[] first = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithWellTaggedConformance(PdfWellTaggedConformance.Reuse));
        using PdfDocument reopened = PdfDocument.Open(first);

        byte[] second = PdfUAFlowTests.Saved(reopened, new PdfSaveOptions().WithWellTaggedConformance(BothLevels));

        using PdfDocument again = PdfDocument.Open(second);
        Assert.Equal([PdfDeclaration.WellTaggedReuse, PdfDeclaration.WellTaggedAccessibility], again.Declarations.Select(declaration => declaration.ConformsTo));
        Assert.Null(again.Metadata!.Packet!.PdfUAPart);
    }

    [Fact]
    public void PDF_UA_2_with_PDF_A_2u_writes_PDF_UA_2_and_does_not_claim_PDF_A_2u_whose_header_is_1_7()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        PdfUAFlowTests.Report(document, Accessible).Render(document);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2).WithPdfAConformance(PdfAConformance.PdfA2u));

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "PdfANonConformant" && diagnostic.Message.Contains("veraPDF rule 6.1.2-1,", StringComparison.Ordinal));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "PdfANotIdentified");
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is NonConformant or NotIdentified);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(2, reopened.Metadata!.Packet!.PdfUAPart);
        Assert.Null(reopened.Metadata.Packet.PdfAPart);
    }

    /// <summary>The failure conditions the flow layout can produce, each with the veraPDF PDF/UA-2 rule the save reports it under.</summary>
    public static TheoryData<string, string> Conditions => new()
    {
        { "no title", "8.11.1-1" },
        { "no language", "8.4.4-1" },
        { "malformed language", "8.4.4-2" },
        { "figure without alternate text", "8.2.5.28.2-1" },
        { "font not embedded", "8.4.5.5.1-1" },
        { "PDF 1.7 namespace", "8.2.5.2-2" },
        { "untagged", "8.2.1-1" },
        { "note", "8.2.5.14-1" },
        { "unnumbered heading", "8.2.5.12-1" },
    };

    [Theory]
    [MemberData(nameof(Conditions))]
    public void A_lenient_save_reports_the_condition_and_neither_identifies_nor_declares(string condition, string rule)
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        Build(document, condition);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2).WithWellTaggedConformance(PdfWellTaggedConformance.Accessibility));

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant && diagnostic.Message.Contains($"PDF/UA-2: ", StringComparison.Ordinal) && diagnostic.Message.Contains($"veraPDF rule {rule},", StringComparison.Ordinal));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == WellTaggedNonConformant && diagnostic.Message.Contains($"veraPDF rule {rule},", StringComparison.Ordinal));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == NotIdentified);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == WellTaggedNotDeclared);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Null(reopened.Metadata?.Packet?.PdfUAPart);
        Assert.Empty(reopened.Declarations);
    }

    [Theory]
    [MemberData(nameof(Conditions))]
    public void A_strict_save_refuses_the_condition_and_leaves_the_document_as_it_was(string condition, string rule)
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict());
        document.Pages.RemoveAt(0);
        Build(document, condition);
        string before = CosSnapshot.Of(document);

        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2)));

        Assert.Equal(NonConformant, refused.Diagnostic.Code);
        Assert.Contains($"veraPDF rule {rule},", refused.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(before, CosSnapshot.Of(document));
    }

    /// <summary>Failure conditions an edit of a flow document can add, which the layout itself never produces.</summary>
    public static TheoryData<string, string> EditConditions => new()
    {
        { "untagged content", "8.2.2-1" },
        { "second top-level element", "8.2.5.2-1" },
        { "remapped standard type", "8.2.4-4" },
        { "list item outside a list", "Table 5. Document-LI-1" },
        { "labelled list without numbering", "8.2.5.25-1" },
        { "header cell without scope", "8.2.5.26-5" },
        { "untagged link", "8.2.5.20-1" },
        { "untagged markup annotation", "8.9.2.3-1" },
        { "dynamic XFA", "8.10.1-3" },
    };

    [Theory]
    [MemberData(nameof(EditConditions))]
    public void A_condition_added_by_editing_a_flow_document_is_reported_and_not_claimed(string condition, string rule)
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        Build(document, "conformant");
        PdfPage page = document.Pages[0];
        PdfStructureElement top = document.StructureTree!.Children[0];
        switch (condition)
        {
            case "untagged content":
                using (PdfCanvas canvas = page.CreateCanvas())
                {
                    canvas.Rectangle(10, 10, 20, 20);
                    canvas.Fill();
                }

                break;
            case "second top-level element":
                document.StructureTree.AddElement(new PdfStructureType("Document", PdfStructureNamespace.Pdf20));
                break;
            case "remapped standard type":
                document.StructureTree.Dictionary[new CosName("RoleMap")] = new CosDictionary { [new CosName("P")] = new CosName("Span") };
                break;
            case "list item outside a list":
                top.AddElement(new PdfStructureType("LI", PdfStructureNamespace.Pdf20));
                break;
            case "labelled list without numbering":
                PdfStructureElement item = top.AddElement(new PdfStructureType("L", PdfStructureNamespace.Pdf20)).AddElement(new PdfStructureType("LI", PdfStructureNamespace.Pdf20));
                item.AddElement(new PdfStructureType("Lbl", PdfStructureNamespace.Pdf20));
                item.AddElement(new PdfStructureType("LBody", PdfStructureNamespace.Pdf20));
                break;
            case "header cell without scope":
                PdfStructureElement row = top.AddElement(new PdfStructureType("Table", PdfStructureNamespace.Pdf20)).AddElement(new PdfStructureType("TR", PdfStructureNamespace.Pdf20));
                row.AddElement(new PdfStructureType("TH", PdfStructureNamespace.Pdf20));
                row.AddElement(new PdfStructureType("TD", PdfStructureNamespace.Pdf20));
                break;
            case "untagged link":
                page.AddLinkAnnotation(new PdfRectangle(10, 10, 50, 20), new Uri("https://example.com/"));
                break;
            case "untagged markup annotation":
                page.AddTextAnnotation(new PdfRectangle(10, 10, 30, 30), "A comment");
                break;
            case "dynamic XFA":
                document.Catalog[new CosName("NeedsRendering")] = CosBoolean.True;
                break;
        }

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2));

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant && diagnostic.Message.Contains($"veraPDF rule {rule},", StringComparison.Ordinal));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Null(reopened.Metadata?.Packet?.PdfUAPart);
    }

    [Fact]
    public void The_conformant_base_of_the_conditions_conforms()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        Build(document, "conformant");

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2).WithWellTaggedConformance(BothLevels));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(2, reopened.Metadata!.Packet!.PdfUAPart);
        Assert.Equal(2, reopened.Declarations.Count);
    }

    [Fact]
    public void An_undefined_level_is_refused()
    {
        var options = new PdfSaveOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithWellTaggedConformance((PdfWellTaggedConformance)4));
    }

    private static void Build(PdfDocument document, string condition)
    {
        FlowTagging? tagging = condition switch
        {
            "no title" => FlowTagging.Default.WithLanguage("en-US"),
            "no language" => FlowTagging.Default.WithTitle("Report"),
            "malformed language" => Accessible.WithLanguage("en_US"),
            "PDF 1.7 namespace" => Accessible.WithNamespace(FlowStructureNamespace.Pdf17),
            "untagged" => null,
            _ => Accessible,
        };
        PdfCanvasFont font = condition == "font not embedded" ? document.GetStandard14Font(Standard14Font.Helvetica) : document.EmbedTrueTypeFont(Sans);
        var body = new ParagraphStyle(font, 10);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = tagging };
        flow.Add(new Paragraph("Results", body) { StructureType = condition == "unnumbered heading" ? "H" : "H1" });
        switch (condition)
        {
            case "figure without alternate text":
                flow.Add(new ImageBlock(PdfUAFlowTests.Image(document), 40, 40));
                break;
            case "note":
                flow.Add(new Paragraph("1. A note.", body) { StructureType = "Note" });
                break;
        }

        flow.Add(new Paragraph("Revenue grew.", body));
        flow.Render(document);
    }

    private static IEnumerable<PdfStructureElement> Descendants(PdfStructureItem item) =>
        item is PdfStructureElement element ? element.Children.SelectMany(Descendants).Prepend(element) : [];
}
