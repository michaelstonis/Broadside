using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Writing;
using static Broadside.Layout.Tests.FlowBuildingBlockTests;

namespace Broadside.Layout.Tests;

/// <summary>
/// <see cref="PdfCanvas"/> inside flow layout (issue #397, E1 and E6) and layout diagnostics on the document (C2): a drawing block
/// paints through a callback in box coordinates, tagged as a <c>Figure</c> with <c>Alt</c> and <c>BBox</c> (ISO 32000-2 §14.8.4.8.5,
/// §14.8.5.4.3) or as an artifact; a template's decoration paints every page as a pagination artifact (§14.8.2.2.2); a callback that
/// leaves the canvas unbalanced (§8.4.2, §14.6.1) is balanced and reported on <see cref="PdfDocument.Diagnostics"/>, and thrown in
/// strict mode.
/// </summary>
public sealed class DrawingFlowTests
{
    private static readonly PdfColor Red = PdfColor.FromHex("#CC0000");

    [Fact]
    public void A_drawing_paints_in_box_coordinates_and_is_a_Figure_with_alternate_text_and_its_bounding_box()
    {
        using PdfDocument document = NewDocument();
        PdfRectangle? received = null;
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new DrawingBlock(100, 50, (canvas, box) =>
        {
            received = box;
            canvas.SetFillColor(Red);
            canvas.Rectangle(0, 0, box.Width, box.Height);
            canvas.Fill();
        })
        { AlternateText = "A red box" });

        FlowLayoutResult result = flow.Render(document);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(new PdfRectangle(0, 0, 100, 50), received);
        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[0]);
        (PdfRectangle bounds, PdfColor color) = Assert.Single(recorder.Fills);
        Assert.Equal(new PdfRectangle(0, 0, 100, 50), Rounded(bounds));
        Assert.Equal(Red, color);
        Assert.Equal(0, recorder.Untagged);
        Assert.Equal("Document(Figure)", Shape(reopened.StructureTree!.Children));
        PdfStructureElement figure = reopened.StructureTree!.Elements.Single(element => element.StructureType?.Value == "Figure");
        Assert.Equal("A red box", figure.AlternateDescription);
        Assert.Equal([72, 670, 172, 720], BoundingBox(figure));
    }

    [Fact]
    public void A_drawing_wider_than_the_content_area_is_scaled_down_and_one_that_does_not_fit_starts_a_page()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        for (int line = 0; line < 50; line++)
        {
            flow.Add(new Paragraph("Line", new ParagraphStyle(font, 10)));
        }

        flow.Add(new DrawingBlock(936, 100, static (canvas, box) =>
        {
            canvas.Rectangle(0, 0, box.Width, box.Height);
            canvas.Fill();
        })
        { AlternateText = "A wide box" });

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Single(FlowContentRecorder.Record(result.Pages[1]).Fills);
        PdfStructureElement figure = document.StructureTree!.Elements.Single(element => element.StructureType?.Value == "Figure");
        Assert.Equal([72, 670, 540, 720], BoundingBox(figure));
    }

    [Fact]
    public void An_image_first_in_the_flow_keeps_its_size()
    {
        using PdfDocument document = NewDocument();
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new ImageBlock(PdfUAFlowTests.Image(document), 100, 50));

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(new Matrix(100, 0, 0, 50, 72, 670), Assert.Single(FlowContentRecorder.Record(result.Pages[0]).Images));
    }

    [Fact]
    public void A_decorative_drawing_is_a_layout_artifact_and_adds_no_element()
    {
        using PdfDocument document = NewDocument();
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new DrawingBlock(40, 10, static (canvas, box) =>
        {
            canvas.Rectangle(0, 0, box.Width, box.Height);
            canvas.Fill();
        })
        { IsDecorative = true });
        flow.Add(new Paragraph("Text", new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10)));

        FlowLayoutResult result = flow.Render(document);

        Assert.Empty(result.Diagnostics);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[0]);
        Assert.Equal(["Layout/"], recorder.Artifacts);
        Assert.Equal(0, recorder.Untagged);
        Assert.Equal("Document(P)", Shape(reopened.StructureTree!.Children));
    }

    [Fact]
    public void A_tagged_drawing_without_alternate_text_is_reported_on_the_document()
    {
        using PdfDocument document = NewDocument();
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new DrawingBlock(40, 10, static (canvas, box) => { }));

        FlowLayoutResult result = flow.Render(document);

        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(StructureDiagnostics.FigureWithoutAlternateText, diagnostic.Kind);
        Assert.Equal([diagnostic], result.Diagnostics);
    }

    [Fact]
    public void A_drawing_that_leaves_the_canvas_unbalanced_is_balanced_and_reported()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new DrawingBlock(40, 10, (canvas, box) =>
        {
            canvas.SaveState();
            canvas.SaveState();
            canvas.BeginMarkedContent("Span");
            canvas.BeginText();
            canvas.BeginMarkedContent("Span");
        })
        { AlternateText = "Unbalanced" });
        flow.Add(new DrawingBlock(40, 10, static (canvas, box) => canvas.Rectangle(0, 0, box.Width, box.Height)) { AlternateText = "Unpainted" });
        flow.Add(new Paragraph("After", new ParagraphStyle(font, 10)));

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(document.Diagnostics, diagnostic => Assert.Equal(LayoutDiagnostics.DrawingStateUnbalanced, diagnostic.Kind));
        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[0]);
        Assert.Equal("After", recorder.BodyText);
        Assert.Equal(0, recorder.Untagged);
        Assert.Equal("Document(Figure,Figure,P)", Shape(reopened.StructureTree!.Children));
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
    }

    [Fact]
    public void In_strict_mode_the_first_layout_diagnostic_is_thrown_once_every_page_is_laid_out()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict());
        document.Pages.RemoveAt(0);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new DrawingBlock(40, 10, static (canvas, box) => canvas.SaveState()));
        flow.Add(new PageBreak());
        flow.Add(new Paragraph("Second page", new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10)));

        DiagnosticException thrown = Assert.Throws<DiagnosticException>(() => flow.Render(document));

        Assert.Equal(LayoutDiagnostics.DrawingStateUnbalanced, thrown.Diagnostic.Kind);
        Assert.Equal(2, document.Pages.Count);
        Assert.Equal([thrown.Diagnostic], document.Diagnostics);
    }

    [Fact]
    public void A_decoration_draws_every_page_with_its_number_count_and_section_as_a_pagination_artifact()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = new(document.GetStandard14Font(Standard14Font.Helvetica), 10);
        var seen = new List<PageInfo>();
        void Border(PdfCanvas canvas, PageInfo page)
        {
            seen.Add(page);
            canvas.Rectangle(page.Margins.Left, page.Margins.Bottom, page.ContentWidth, page.ContentHeight);
            canvas.Stroke();
        }

        var template = new PageTemplate(PageSetup.Letter) { Decoration = Border, Footer = new HeaderFooter(new TextStyle(body.Font, 8)) { Center = "Page {page}" } };
        var flow = new FlowDocument(template) { Tagging = FlowTagging.Default };
        flow.Add(new Paragraph("One", body)).Add(new PageBreak()).Add(new Paragraph("Two", body));
        flow.StartSection(template);
        flow.Add(new Paragraph("Three", body));

        flow.Render(document);

        Assert.Equal(
            [new PageInfo(1, 3, 0, true, PageSetup.Letter), new PageInfo(2, 3, 0, false, PageSetup.Letter), new PageInfo(3, 3, 1, true, PageSetup.Letter)],
            seen);
        Assert.Empty(document.Diagnostics);
        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[0]);
        (PdfRectangle bounds, _) = Assert.Single(recorder.Strokes);
        Assert.Equal(new PdfRectangle(72, 72, 540, 720), Rounded(bounds));
        Assert.Contains("Pagination/", recorder.Artifacts);
        Assert.Equal(0, recorder.Untagged);
        Assert.Equal("One", recorder.BodyText);
    }

    [Fact]
    public void A_decoration_that_leaves_the_canvas_unbalanced_is_balanced_and_reported()
    {
        using PdfDocument document = NewDocument();
        var template = new PageTemplate(PageSetup.Letter) { Decoration = static (canvas, page) => canvas.BeginText() };
        var flow = new FlowDocument(template);
        flow.Add(new Paragraph("One", new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10)));

        flow.Render(document);

        Assert.Equal(LayoutDiagnostics.DrawingStateUnbalanced, Assert.Single(document.Diagnostics).Kind);
        AssertQpdfPasses(Saved(document));
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void Drawings_rules_groups_columns_and_decorations_pass_verapdf_PDF_UA_1()
    {
        using PdfDocument document = PdfUAFlowTests.NewDocument();
        FlowTagging tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle("Building blocks").WithNamespace(FlowStructureNamespace.Pdf17);
        FlowDocument report = PdfUAFlowTests.Report(document, tagging);
        PdfCanvasFont font = ((Paragraph)report.Blocks[1]).Style.Font;
        var body = new ParagraphStyle(font, 10);
        var template = new PageTemplate(PageSetup.Letter)
        {
            Decoration = static (canvas, page) =>
            {
                canvas.Rectangle(page.Margins.Left / 2, page.Margins.Bottom / 2, page.Width - page.Margins.Left, page.Height - page.Margins.Bottom);
                canvas.Stroke();
            },
        };
        var flow = new FlowDocument(template) { Tagging = tagging };
        foreach (FlowBlock block in report.Blocks)
        {
            flow.Add(block);
        }

        flow.Add(new Rule());
        flow.Add(new DrawingBlock(120, 40, (canvas, box) =>
        {
            canvas.SetFillColor(Red);
            canvas.Rectangle(0, 0, box.Width / 2, box.Height);
            canvas.Fill();
        })
        { AlternateText = "Two bars: the first half the width of the box." });
        flow.Add(new DrawingBlock(60, 6, static (canvas, box) =>
        {
            canvas.Rectangle(0, 0, box.Width, box.Height);
            canvas.Fill();
        })
        { IsDecorative = true, Alignment = TextAlignment.Center });
        flow.Add(new ColumnsBlock(
            new FlowColumn(new Paragraph("Seller", body), new Paragraph("1 Main Street", body)),
            new FlowColumn(new Paragraph("Invoice 42", body) { StructureType = "H2" })));
        flow.Add(new BlockGroup(new Paragraph("Kept", body), new Paragraph("Together", body)) { KeepTogether = true, IsTagged = true });
        flow.Add(new PageBreak());
        flow.Add(new Paragraph("Second page", body) { KeepWithNext = true });
        flow.Add(new Spacer(20));
        flow.Add(new Paragraph("The end.", body));
        flow.Render(document);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        AssertQpdfPasses(saved);
        Assert.Empty(VeraPdf.FailedRules(saved, "ua1"));
    }

    private static double[] BoundingBox(PdfStructureElement figure) =>
        [.. ((CosArray)figure.GetAttributeValue(new CosName("Layout"), new CosName("BBox"))!).Select(value => Math.Round(((CosNumber)value).ToDouble(), 4))];

    private static PdfRectangle Rounded(PdfRectangle box) =>
        new(Math.Round(box.Left, 3), Math.Round(box.Bottom, 3), Math.Round(box.Right, 3), Math.Round(box.Top, 3));
}
