using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Layout.Flow;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Writing;

namespace Broadside.Layout.Tests;

/// <summary>
/// PDF/UA-1 output from flow layout (issue #204): a tagged flow document saved with <see cref="PdfUAConformance.PdfUA1"/> passes
/// veraPDF's PDF/UA-1 profile, and each failure condition of the profile (Matterhorn Protocol 1.1) the layout can produce is either
/// prevented by the save or reported under its veraPDF rule identifier, refused in strict mode and never claimed in lenient mode.
/// </summary>
public sealed class PdfUAFlowTests
{
    private const string NonConformant = "PdfUANonConformant";
    private const string NotIdentified = "PdfUANotIdentified";

    private static readonly ReadOnlyMemory<byte> Sans = Standard14FontResolver.GetProgram(Standard14Font.Helvetica);
    private static readonly ReadOnlyMemory<byte> SansBold = Standard14FontResolver.GetProgram(Standard14Font.HelveticaBold);

    private static FlowTagging Accessible => FlowTagging.Default
        .WithLanguage("en-US")
        .WithTitle("Quarterly report")
        .WithNamespace(FlowStructureNamespace.Pdf17);

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_report_with_headings_lists_a_table_and_a_figure_passes_verapdf_PDF_UA_1()
    {
        using PdfDocument document = NewDocument();
        Report(document, Accessible).Render(document);

        byte[] saved = Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, "ua1"));
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_report_saved_as_PDF_UA_1_and_PDF_A_2u_passes_both_verapdf_profiles()
    {
        using PdfDocument document = NewDocument();
        Report(document, Accessible).Render(document);

        byte[] saved = Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1).WithPdfAConformance(PdfAConformance.PdfA2u));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, "ua1"));
        Assert.Empty(VeraPdf.FailedRules(saved, "2u"));
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_report_saved_as_PDF_A_4_with_PDF_UA_1_is_PDF_A_4_and_not_claimed_as_PDF_UA_1_whose_header_is_1_n()
    {
        using PdfDocument document = NewDocument();
        Report(document, Accessible).Render(document);

        byte[] saved = Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1).WithPdfAConformance(PdfAConformance.PdfA4));

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
        Assert.Contains("PDF/A-4", reported.Message, StringComparison.Ordinal);
        Assert.Contains("veraPDF rule 6.1-1,", reported.Message, StringComparison.Ordinal);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == NotIdentified);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "PdfANonConformant" or "PdfANotIdentified");
        Assert.Empty(VeraPdf.FailedRules(saved, "4"));
    }

    [Fact]
    public void The_save_identifies_PDF_UA_1_and_writes_what_the_profile_requires_of_the_file()
    {
        using PdfDocument document = NewDocument();
        Report(document, Accessible).Render(document);
        document.Catalog.Remove(new CosName("ViewerPreferences"));
        document.MarkInfo.Marked = false;

        byte[] saved = Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is NonConformant or NotIdentified);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(1, reopened.Metadata!.Packet!.PdfUAPart);
        Assert.Contains("Quarterly report", reopened.Metadata.Packet.Properties.Select(property => property.Items.Count > 0 ? property.Items[0].Value : property.Value));
        Assert.True(reopened.ViewerPreferences!.DisplayDocTitle);
        Assert.True(reopened.MarkInfo.Marked);
        Assert.Equal(new PdfVersion(1, 7), reopened.Version);
        foreach (PdfPage page in reopened.Pages.Where(page => page.Annotations.Count > 0))
        {
            Assert.Equal(new CosName("S"), page.Dictionary[new CosName("Tabs")]);
        }

        Assert.Contains(reopened.Pages, page => page.Annotations.Count > 0);
    }

    /// <summary>The failure conditions the flow layout can produce, each with the veraPDF PDF/UA-1 rule the save reports it under.</summary>
    public static TheoryData<string, string> Conditions => new()
    {
        { "no title", "7.1-9" },
        { "no language", "7.2-33" },
        { "malformed language", "7.2-29" },
        { "figure without alternate text", "7.3-1" },
        { "font not embedded", "7.21.4.1-1" },
        { "PDF 2.0 namespace", "6.1-1" },
        { "untagged", "7.1-11" },
        { "skipped heading level", "7.4.2-1" },
        { "note without ID", "7.9-1" },
    };

    [Theory]
    [MemberData(nameof(Conditions))]
    public void A_lenient_save_reports_the_condition_and_does_not_claim_PDF_UA(string condition, string rule)
    {
        using PdfDocument document = NewDocument();
        Build(document, condition);

        byte[] saved = Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant && diagnostic.Message.Contains($"veraPDF rule {rule},", StringComparison.Ordinal));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == NotIdentified);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Null(reopened.Metadata?.Packet?.PdfUAPart);
    }

    [Theory]
    [MemberData(nameof(Conditions))]
    public void A_strict_save_refuses_the_condition_and_leaves_the_document_as_it_was(string condition, string rule)
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict());
        document.Pages.RemoveAt(0);
        Build(document, condition);
        string before = CosSnapshot.Of(document);

        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1)));

        Assert.Equal(NonConformant, refused.Diagnostic.Code);
        Assert.Contains($"veraPDF rule {rule},", refused.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(before, CosSnapshot.Of(document));
    }

    /// <summary>Failure conditions an edit of a flow document can add, which the layout itself never produces.</summary>
    public static TheoryData<string, string> EditConditions => new()
    {
        { "untagged content", "7.1-3" },
        { "suspects", "7.1-4" },
        { "remapped standard type", "7.1-7" },
        { "list item outside a list", "7.2-17" },
        { "header cell without scope", "7.5-1" },
        { "untagged link", "7.18.5-1" },
        { "untagged annotation", "7.18.1-1" },
        { "dynamic XFA", "7.15-1" },
    };

    [Theory]
    [MemberData(nameof(EditConditions))]
    public void A_condition_added_by_editing_a_flow_document_is_reported_and_not_claimed(string condition, string rule)
    {
        using PdfDocument document = NewDocument();
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
            case "suspects":
                document.MarkInfo.Suspects = true;
                break;
            case "remapped standard type":
                document.StructureTree.Dictionary[new CosName("RoleMap")] = new CosDictionary { [new CosName("P")] = new CosName("Span") };
                break;
            case "list item outside a list":
                top.AddElement("LI");
                break;
            case "header cell without scope":
                PdfStructureElement row = top.AddElement("Table").AddElement("TR");
                row.AddElement("TH");
                row.AddElement("TD");
                break;
            case "untagged link":
                page.AddLinkAnnotation(new PdfRectangle(10, 10, 50, 20), new Uri("https://example.com/"));
                break;
            case "untagged annotation":
                page.AddTextAnnotation(new PdfRectangle(10, 10, 30, 30), "A comment");
                break;
            case "dynamic XFA":
                document.Catalog[new CosName("NeedsRendering")] = CosBoolean.True;
                break;
        }

        byte[] saved = Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant && diagnostic.Message.Contains($"veraPDF rule {rule},", StringComparison.Ordinal));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Null(reopened.Metadata?.Packet?.PdfUAPart);
    }

    [Fact]
    public void The_conformant_base_of_the_edit_conditions_conforms()
    {
        using PdfDocument document = NewDocument();
        Build(document, "conformant");

        byte[] saved = Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(1, reopened.Metadata!.Packet!.PdfUAPart);
    }

    [Fact]
    public void A_save_without_PDF_UA_neither_checks_nor_identifies()
    {
        using PdfDocument document = NewDocument();
        Build(document, "font not embedded");

        byte[] saved = Saved(document, new PdfSaveOptions());

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is NonConformant or NotIdentified);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Null(reopened.Metadata?.Packet?.PdfUAPart);
    }

    [Fact]
    public void An_undefined_conformance_is_refused()
    {
        var options = new PdfSaveOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithPdfUAConformance((PdfUAConformance)7));
    }

    private static void Build(PdfDocument document, string condition)
    {
        FlowTagging? tagging = condition switch
        {
            "no title" => FlowTagging.Default.WithLanguage("en-US").WithNamespace(FlowStructureNamespace.Pdf17),
            "no language" => FlowTagging.Default.WithTitle("Report").WithNamespace(FlowStructureNamespace.Pdf17),
            "malformed language" => Accessible.WithLanguage("en_US"),
            "PDF 2.0 namespace" => Accessible.WithNamespace(FlowStructureNamespace.Pdf20),
            "untagged" => null,
            _ => Accessible,
        };
        PdfCanvasFont font = condition == "font not embedded" ? document.GetStandard14Font(Standard14Font.Helvetica) : document.EmbedTrueTypeFont(Sans);
        var body = new ParagraphStyle(font, 10);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = tagging };
        flow.Add(new Paragraph("Results", body) { StructureType = "H1" });
        switch (condition)
        {
            case "figure without alternate text":
                flow.Add(new ImageBlock(Image(document), 40, 40));
                break;
            case "skipped heading level":
                flow.Add(new Paragraph("Details", body) { StructureType = "H3" });
                break;
            case "note without ID":
                flow.Add(new Paragraph("1. A note.", body) { StructureType = "Note" });
                break;
        }

        flow.Add(new Paragraph("Revenue grew.", body));
        flow.Render(document);
    }

    internal static FlowDocument Report(PdfDocument document, FlowTagging tagging)
    {
        PdfCanvasFont font = document.EmbedTrueTypeFont(Sans);
        var body = new ParagraphStyle(font, 10);
        var heading = new ParagraphStyle(document.EmbedTrueTypeFont(SansBold), 16);
        var small = new TextStyle(font, 8);
        var template = new PageTemplate(PageSetup.Letter)
        {
            Header = new HeaderFooter(small) { Left = "Running head" },
            Footer = new HeaderFooter(small) { Center = "Page {page}" },
        };
        var flow = new FlowDocument(template) { Tagging = tagging };
        flow.Add(new Paragraph("Results", heading) { StructureType = "H1" });
        flow.Add(new Paragraph("Revenue grew in every region.", body));
        flow.Add(new Paragraph("By region", heading) { StructureType = "H2" });
        flow.Add(new ListBlock(new ListStyle(new TextStyle(font, 10))).Add(new ListItem("North", body)).Add(new ListItem("South", body)));
        var table = new Table(ColumnWidth.Fixed(100), ColumnWidth.Fixed(100)) { HeaderRows = 1 };
        table.AddRow(new TableCell("Region", body), new TableCell("Sales", body));
        table.AddRow(new TableCell("East", body), new TableCell("12", body));
        table.AddRow(new TableCell("West", body), new TableCell("15", body));
        flow.Add(table);
        flow.Add(new ImageBlock(Image(document), 40, 40) { AlternateText = "Sales chart" });
        flow.Add(new Paragraph("Figure 1. Sales", body) { StructureType = "Caption" });
        flow.Add(new Paragraph([new TextRun("See "), new TextRun("the site") { Link = new Uri("https://example.com/") }, new TextRun(" for more.")], body));
        flow.Add(new Paragraph([new TextRun("Bonjour") { Language = "fr" }], body));
        return flow;
    }

    internal static PdfImage Image(PdfDocument document) =>
        document.CreateImage([0, 64, 128, 255], new PdfImageLayout(2, 2, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);

    internal static PdfDocument NewDocument()
    {
        PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        return document;
    }

    internal static byte[] Saved(PdfDocument document, PdfSaveOptions options)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options);
        return stream.ToArray();
    }
}
