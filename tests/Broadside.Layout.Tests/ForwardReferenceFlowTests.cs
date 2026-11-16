using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.Standard14;
using Broadside.Layout.Flow;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Writing;
using Broadside.Text;

namespace Broadside.Layout.Tests;

/// <summary>
/// Forward references in flow layout (issue #397, E4): a <see cref="PageReference"/> or a <see cref="TableOfContents"/> shows page
/// numbers known only once the flow is laid out, so the flow is laid out again until they settle, at most
/// <see cref="FlowDocument.MaxLayoutPasses"/> times, with a diagnostic when they do not. A <see cref="Leader"/> fills the rest of its line.
/// </summary>
public sealed class ForwardReferenceFlowTests
{
    [Fact]
    public void A_page_reference_shows_the_page_its_block_lands_on_forward_and_back()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        ParagraphStyle body = AnchorFlowTests.Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph([new TextRun("The results are on page "), new PageReference("results"), new TextRun(".")], body) { Id = "intro" });
        flow.Add(new Paragraph(AnchorFlowTests.Filler, body));
        flow.Add(new Paragraph(AnchorFlowTests.Filler, body));
        flow.Add(new Paragraph([new TextRun("Results, after the introduction on page "), new PageReference("intro"), new TextRun(".")], body) { Id = "results" });

        FlowLayoutResult result = flow.Render(document);

        Assert.Empty(result.Diagnostics);
        int resultsPage = result.Anchors["results"].PageNumber;
        Assert.True(resultsPage >= 3);
        Assert.Contains($"The results are on page {resultsPage}.", result.Pages[0].ExtractText().PlainText, StringComparison.Ordinal);
        Assert.Contains("after the introduction on page 1.", result.Pages[^1].ExtractText().PlainText, StringComparison.Ordinal);
        Assert.Equal(resultsPage, result.Pages.Count);
    }

    [Fact]
    public void A_page_reference_to_an_id_nothing_has_shows_a_question_mark_and_is_reported()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph([new TextRun("See page "), new PageReference("missing")], AnchorFlowTests.Body(document)));

        FlowLayoutResult result = flow.Render(document);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AnchorNotFound" && diagnostic.Message.Contains("'missing'", StringComparison.Ordinal));
        Assert.Contains("See page ?", result.Pages[0].ExtractText().PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_of_contents_lists_the_headings_with_their_pages_and_links_each_entry_to_its_heading()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        ParagraphStyle body = AnchorFlowTests.Body(document);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default.WithLanguage("en") };
        flow.Add(new Paragraph("Guide", body) { StructureType = "H1" });
        flow.Add(new TableOfContents(body) { Depth = 2 });
        flow.Add(new Paragraph("Installing", body) { StructureType = "H2", Id = "install" });
        flow.Add(new Paragraph(AnchorFlowTests.Filler, body));
        flow.Add(new Paragraph("On Windows", body) { StructureType = "H3" });
        flow.Add(new Paragraph("Reading files", body) { StructureType = "H2" });

        FlowLayoutResult result = flow.Render(document);

        Assert.Empty(result.Diagnostics);
        int reading = PageOf(result, "Reading files");
        Assert.True(reading >= 2);
        string text = result.Pages[0].ExtractText().PlainText;
        Assert.Contains("Guide", text, StringComparison.Ordinal);
        Assert.Matches(@"Installing\s*\.*\s*1", text);
        Assert.Matches($@"Reading files\s*\.*\s*{reading}", text);
        Assert.DoesNotContain("On Windows", text[..text.IndexOf("Installing", StringComparison.Ordinal)], StringComparison.Ordinal);

        byte[] saved = AnchorFlowTests.Saved(document);
        AnchorFlowTests.AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfLinkAnnotation[] links = [.. reopened.Pages[0].Annotations.OfType<PdfLinkAnnotation>()];
        Assert.Equal(3, links.Length);
        // Tagged in the PDF 2.0 namespace, each entry goes to its heading through a go-to action with a structure destination (SD) to the
        // heading's element and the page destination (D) kept: a name for a heading with an id, else an explicit destination.
        PdfGoToAction install = Assert.IsType<PdfGoToAction>(links[1].Action);
        Assert.Equal("install", Assert.IsType<PdfNamedDestination>(install.Destination).Text);
        PdfGoToAction readingAction = Assert.IsType<PdfGoToAction>(links[2].Action);
        Assert.Equal(reading - 1, Assert.IsType<PdfExplicitDestination>(readingAction.Destination).PageIndex);
        Assert.All(links, link =>
        {
            Assert.Null(link.Destination);
            PdfExplicitDestination structure = Assert.IsType<PdfGoToAction>(link.Action).StructureDestination!;
            Assert.Matches("^H[1-6]$", ((CosName)structure.StructureElement![new CosName("S")]).Value);
        });
        Assert.Equal(reading - 1, readingAction.StructureDestination!.PageIndex);
        PdfStructureElement toc = reopened.StructureTree!.Elements.Single(element => element.StructureType?.Value == "TOC");
        Assert.Equal(["TOCI", "TOCI", "TOCI"], toc.Children.OfType<PdfStructureElement>().Select(element => element.StructureType!.Value));
        Assert.All(toc.Children.OfType<PdfStructureElement>(), entry =>
        {
            PdfStructureElement link = Assert.Single(entry.Children.OfType<PdfStructureElement>());
            Assert.Equal("Link", link.StructureType!.Value);
            Assert.Single(link.Children.OfType<PdfObjectReference>());
            Assert.Contains(entry.Children, child => child is PdfMarkedContentReference);
        });
        FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[0]);
        Assert.Equal(0, recorder.Untagged);
        Assert.Equal(0, recorder.Mixed);
    }

    [Fact]
    public void A_table_of_contents_that_pushes_its_headings_onto_later_pages_shows_the_pages_they_land_on()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        ParagraphStyle body = AnchorFlowTests.Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new TableOfContents(body));
        for (int chapter = 1; chapter <= 80; chapter++)
        {
            flow.Add(new Paragraph($"Chapter {chapter}", body) { StructureType = "H1", Id = $"c{chapter}" });
            flow.Add(new Paragraph(string.Join(' ', Enumerable.Repeat("Some words of the chapter.", 60)), body));
        }

        FlowLayoutResult result = flow.Render(document);

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Anchors["c1"].PageNumber >= 2, "the table of contents takes more than the first page");
        string contents = string.Concat(result.Pages.Take(result.Anchors["c1"].PageNumber).Select(page => page.ExtractText().PlainText));
        foreach (int chapter in new[] { 1, 9, 10, 42, 80 })
        {
            Assert.Matches($@"Chapter {chapter}\s*\.*\s*{result.Anchors[$"c{chapter}"].PageNumber}\b", contents);
        }
    }

    [Fact]
    public void When_the_page_numbers_have_not_settled_after_the_last_pass_the_last_pass_stands_with_a_diagnostic()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        ParagraphStyle body = AnchorFlowTests.Body(document);
        var flow = new FlowDocument(PageSetup.Letter) { MaxLayoutPasses = 1 };
        flow.Add(new Paragraph([new TextRun("See page "), new PageReference("end")], body));
        flow.Add(new Paragraph(AnchorFlowTests.Filler, body));
        flow.Add(new Paragraph("The end.", body) { Id = "end" });

        FlowLayoutResult result = flow.Render(document);

        Diagnostic reported = Assert.Single(result.Diagnostics);
        Assert.Equal("ForwardReferencesNotConverged", reported.Code);
        Assert.Equal(DiagnosticSeverity.Warning, reported.Severity);
        Assert.Contains($"See page {result.Anchors["end"].PageNumber}", result.Pages[0].ExtractText().PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_leader_fills_its_line_and_pushes_what_follows_to_the_right_edge()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        ParagraphStyle body = AnchorFlowTests.Body(document).WithAlignment(TextAlignment.Center);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph([new TextRun("Total"), new Leader(".", body.TextStyle), new TextRun("42")], body));

        FlowLayoutResult result = flow.Render(document);

        TextPage text = result.Pages[0].ExtractText();
        TextWord total = text.Words.Single(word => text.GetWordText(word).StartsWith("Total", StringComparison.Ordinal));
        TextWord number = text.Words.Single(word => text.GetWordText(word).EndsWith("42", StringComparison.Ordinal));
        double right = PageSetup.Letter.Width - PageSetup.Letter.Margins.Right;
        Assert.Equal(PageSetup.Letter.Margins.Left, total.Quad.Bounds.Left, 1);
        Assert.InRange(number.Quad.Bounds.Right, right - 1, right + 0.01);
        Assert.Contains("....", text.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_guide_with_a_table_of_contents_links_and_an_outline_passes_verapdf_PDF_UA_1()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        PdfCanvasFont font = document.EmbedTrueTypeFont(Standard14FontResolver.GetProgram(Standard14Font.Helvetica));
        var body = new ParagraphStyle(font, 10);
        var flow = new FlowDocument(PageSetup.Letter)
        {
            Tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle("Guide").WithNamespace(FlowStructureNamespace.Pdf17),
            OutlineDepth = 3,
        };
        flow.Add(new Paragraph("Guide", body) { StructureType = "H1" });
        flow.Add(new TableOfContents(body));
        flow.Add(new Paragraph("Installing", body) { StructureType = "H2", Id = "install" });
        flow.Add(new Paragraph(AnchorFlowTests.Filler, body));
        flow.Add(new Paragraph("Reading files", body) { StructureType = "H2" });
        flow.Add(new Paragraph([new TextRun("Install first (see "), new TextRun("Installing") { Destination = "install" }, new TextRun(" on page "), new PageReference("install"), new TextRun(").")], body));
        flow.Render(document);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, "ua1"));
    }

    /// <summary>The number of the first page after the first that shows <paramref name="text"/>.</summary>
    private static int PageOf(FlowLayoutResult result, string text)
    {
        for (int index = 1; index < result.Pages.Count; index++)
        {
            if (result.Pages[index].ExtractText().PlainText.Contains(text, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return 0;
    }
}
