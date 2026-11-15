using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Layout.Flow;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Writing;

namespace Broadside.Layout.Tests;

/// <summary>
/// Anchors in flow layout (issue #397, E2): a block's <see cref="FlowBlock.Id"/> becomes a named destination at its first line and is
/// reported in <see cref="FlowLayoutResult.Anchors"/>; a <see cref="TextRun.Destination"/> links to it (ISO 32000-2 §12.5.6.5, §12.3.2.4),
/// tagged <c>Link</c>; and <see cref="FlowDocument.OutlineDepth"/> builds the outline from the headings (§12.3.3).
/// </summary>
public sealed class AnchorFlowTests
{
    [Fact]
    public void A_block_id_becomes_a_named_destination_at_its_first_line_and_is_reported_with_its_page()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph("Introduction", body));
        flow.Add(new Paragraph(Filler, body));
        flow.Add(new Paragraph("Results", body) { Id = "results" });

        FlowLayoutResult result = flow.Render(document);

        FlowAnchor anchor = result.Anchors["results"];
        Assert.Equal("results", anchor.Id);
        Assert.True(result.Pages.Count >= 2);
        Assert.Equal(result.Pages.Count, anchor.PageNumber);
        Assert.Same(result.Pages[^1].Dictionary, anchor.Page.Dictionary);
        Assert.Equal(PageSetup.Letter.Margins.Left, anchor.Left, 6);
        Assert.InRange(anchor.Top, PageSetup.Letter.Margins.Bottom, PageSetup.Letter.Height - PageSetup.Letter.Margins.Top);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        PdfExplicitDestination destination = reopened.GetNamedDestination("results")!;
        Assert.Equal(PdfDestinationView.Xyz, destination.View);
        Assert.Equal(reopened.Pages.Count - 1, destination.PageIndex);
        Assert.Equal(Math.Round(anchor.Top, 2), ((CosNumber)destination.Array[3]).ToDouble(), 6);
        Assert.True(reopened.Version >= new PdfVersion(1, 2));
    }

    [Fact]
    public void Blocks_inside_list_items_and_table_cells_are_anchors_too()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new ListBlock(new ListStyle(body.TextStyle)).Add(new ListItem([new Paragraph("An item", body) { Id = "item" }])));
        var table = new Table(ColumnWidth.Fixed(100));
        table.AddRow(new TableCell([new Paragraph("A cell", body) { Id = "cell" }]));
        flow.Add(table);

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(1, result.Anchors["item"].PageNumber);
        Assert.Equal(1, result.Anchors["cell"].PageNumber);
        Assert.True(result.Anchors["item"].Left > PageSetup.Letter.Margins.Left, "a list item's content is indented");
        Assert.True(result.Anchors["cell"].Top < result.Anchors["item"].Top);
        Assert.NotNull(document.GetNamedDestination("cell"));
    }

    [Fact]
    public void A_run_with_a_destination_links_forward_and_back_to_the_block_through_its_name()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default.WithLanguage("en") };
        flow.Add(new Paragraph([new TextRun("See "), new TextRun("the results") { Destination = "results" }, new TextRun(".")], body) { Id = "top" });
        flow.Add(new Paragraph(Filler, body));
        flow.Add(new Paragraph([new TextRun("Results. "), new TextRun("Back to the top") { Destination = "top" }], body) { Id = "results" });

        FlowLayoutResult result = flow.Render(document);

        Assert.Empty(result.Diagnostics);
        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfLinkAnnotation forward = Assert.Single(reopened.Pages[0].Annotations.OfType<PdfLinkAnnotation>());
        Assert.Equal("results", Assert.IsType<PdfNamedDestination>(forward.Destination).Text);
        Assert.Equal(reopened.Pages.Count - 1, Assert.IsType<PdfNamedDestination>(forward.Destination).Resolve()!.PageIndex);
        PdfLinkAnnotation back = Assert.Single(reopened.Pages[^1].Annotations.OfType<PdfLinkAnnotation>());
        Assert.Equal(0, Assert.IsType<PdfNamedDestination>(back.Destination).Resolve()!.PageIndex);

        PdfStructureElement[] links = [.. reopened.StructureTree!.Elements.Where(element => element.StructureType?.Value == "Link")];
        Assert.Equal(2, links.Length);
        Assert.All(links, link => Assert.Single(link.Children.OfType<PdfObjectReference>()));
        Assert.Equal("the results", Text(forward.Dictionary[new CosName("Contents")]));
    }

    [Fact]
    public void A_destination_the_document_already_names_is_linked_by_that_name()
    {
        using PdfDocument document = NewDocument();
        PdfPage first = document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        document.SetNamedDestination("cover", document.CreateDestination(first, PdfDestinationView.Fit));
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph([new TextRun("Back to the cover") { Destination = "cover" }], Body(document)));

        FlowLayoutResult result = flow.Render(document);

        Assert.Empty(result.Diagnostics);
        PdfLinkAnnotation link = Assert.Single(result.Pages[0].Annotations.OfType<PdfLinkAnnotation>());
        Assert.Equal(0, Assert.IsType<PdfNamedDestination>(link.Destination).Resolve()!.PageIndex);
    }

    [Fact]
    public void A_link_to_an_id_nothing_has_is_reported_and_drawn_as_plain_text()
    {
        using PdfDocument document = NewDocument();
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new Paragraph([new TextRun("See "), new TextRun("nowhere") { Destination = "missing" }], Body(document)));

        FlowLayoutResult result = flow.Render(document);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AnchorNotFound" && diagnostic.Message.Contains("'missing'", StringComparison.Ordinal));
        Assert.Empty(result.Pages[0].Annotations);
        Assert.DoesNotContain(document.StructureTree!.Elements, element => element.StructureType?.Value == "Link");
        Assert.Contains("See nowhere", result.Pages[0].ExtractText().PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_block_with_the_same_id_is_reported_and_the_first_keeps_the_name()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph("First", body) { Id = "same" });
        flow.Add(new Paragraph("Second", body) { Id = "same" });

        FlowLayoutResult result = flow.Render(document);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AnchorDuplicate");
        FlowAnchor anchor = Assert.Single(result.Anchors).Value;
        Assert.Equal(PageSetup.Letter.Height - PageSetup.Letter.Margins.Top, anchor.Top, 6);
    }

    [Fact]
    public void A_run_with_both_a_link_and_a_destination_is_refused_before_anything_is_added()
    {
        using PdfDocument document = NewDocument();
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph([new TextRun("Both") { Link = new Uri("https://example.com/"), Destination = "x" }], Body(document)));

        Assert.Throws<ArgumentException>(() => flow.Render(document));
        Assert.Empty(document.Pages);
    }

    [Fact]
    public void Headings_build_the_outline_down_to_the_depth_asked_for_nested_by_level()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter) { OutlineDepth = 2 };
        flow.Add(new Paragraph("Guide", body) { StructureType = "H1" });
        flow.Add(new Paragraph("Installing", body) { StructureType = "H2", Id = "install" });
        flow.Add(new Paragraph("On Windows", body) { StructureType = "H3" });
        flow.Add(new Paragraph(Filler, body));
        flow.Add(new Paragraph("Reading  files", body) { StructureType = "H2" });

        FlowLayoutResult result = flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        PdfOutlineItem guide = Assert.Single(reopened.Outline!.Items);
        Assert.Equal("Guide", guide.Title);
        Assert.Equal(["Installing", "Reading files"], guide.Children.Select(item => item.Title));
        Assert.Empty(guide.Children[0].Children);
        Assert.Equal("install", Assert.IsType<PdfNamedDestination>(guide.Children[0].Destination).Text);
        PdfExplicitDestination reading = Assert.IsType<PdfExplicitDestination>(guide.Children[1].Destination);
        Assert.Equal(result.Pages.Count - 1, reading.PageIndex);
        Assert.Equal(0, Assert.IsType<PdfExplicitDestination>(guide.Destination).PageIndex);
    }

    [Fact]
    public void Without_an_outline_depth_no_outline_is_added()
    {
        using PdfDocument document = NewDocument();
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph("Guide", Body(document)) { StructureType = "H1" });

        FlowLayoutResult result = flow.Render(document);

        Assert.Null(document.Outline);
        Assert.Empty(result.Anchors);
        Assert.Null(document.Names?.Dests);
    }

    [Fact]
    public void An_outline_depth_outside_0_to_6_and_an_empty_id_are_refused()
    {
        var flow = new FlowDocument(PageSetup.Letter);

        Assert.Throws<ArgumentOutOfRangeException>(() => flow.OutlineDepth = 7);
        Assert.Throws<ArgumentOutOfRangeException>(() => flow.MaxLayoutPasses = 0);
        Assert.Throws<ArgumentException>(() => new TextRun("x") { Destination = string.Empty });
    }

    internal static string Filler { get; } = string.Join(' ', Enumerable.Repeat("This sentence fills the page so the next block lands on a later page.", 120));

    internal static ParagraphStyle Body(PdfDocument document) => new(document.GetStandard14Font(Standard14Font.Helvetica), 10);

    internal static string? Text(CosObject value) => value is CosString text
        ? text.Bytes is [0xFE, 0xFF, ..] ? System.Text.Encoding.BigEndianUnicode.GetString(text.Bytes[2..]) : System.Text.Encoding.ASCII.GetString(text.Bytes)
        : null;

    internal static void AssertQpdfPasses(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    internal static PdfDocument NewDocument()
    {
        PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        return document;
    }

    internal static byte[] Saved(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
