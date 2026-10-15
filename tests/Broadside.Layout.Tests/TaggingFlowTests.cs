using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Layout.Flow;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Writing;
using Broadside.Text;

namespace Broadside.Layout.Tests;

/// <summary>
/// Tagging by construction (ISO 32000-2 §14.8): a flow rendered with <see cref="FlowDocument.Tagging"/> saved and re-read through the
/// structure-tree reader. The tree has the construction order and the expected types, every MCID of the content appears in the tree in
/// the same order (reading order = drawing order), every painted mark is either tagged or an artifact and never both, tables carry their
/// attributes, figures their alternate text, links their annotation, and the catalog its language, title and MarkInfo.
/// </summary>
public sealed class TaggingFlowTests
{
    [Fact]
    public void A_generated_report_re_reads_in_construction_order_with_the_expected_element_types()
    {
        using PdfDocument document = NewDocument();
        (FlowDocument flow, _) = Report(document, FlowTagging.Default.WithLanguage("en-US").WithTitle("Quarterly report"));

        FlowLayoutResult result = flow.Render(document);

        Assert.Empty(result.Diagnostics);
        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfStructureTreeRoot root = reopened.StructureTree!;
        Assert.Equal(
            "Document(H1,P,L(LI(Lbl,LBody(P)),LI(Lbl,LBody(P))),Table(THead(TR(TH(P),TH(P))),TBody(TR(TD(P),TD(P)))),Figure(Caption),P(Link),P(Span))",
            Shape(root.Children));
        Assert.Equal("en-US", reopened.Language);
        Assert.Equal("Quarterly report", reopened.Properties.Title);
        Assert.True(reopened.ViewerPreferences!.DisplayDocTitle);
        Assert.True(reopened.MarkInfo.Marked);
        Assert.Contains(root.Namespaces, ns => ns.Name == PdfStructureNamespace.Pdf20Name);

        PdfStructureElement figure = root.Elements.Single(element => element.StructureType?.Value == "Figure");
        Assert.Equal("Sales chart", figure.AlternateDescription);
        PdfStructureElement link = root.Elements.Single(element => element.StructureType?.Value == "Link");
        PdfObjectReference annotation = Assert.Single(link.Children.OfType<PdfObjectReference>());
        Assert.Equal("the site", ((CosDictionary)annotation.ReferencedObject).TryGetValue(new CosName("Contents"), out CosObject? contents) ? Text(contents) : null);
        Assert.Equal("fr", root.Elements.Single(element => element.StructureType?.Value == "Span").Language);

        PdfPage page = reopened.Pages[0];
        FlowContentRecorder recorder = FlowContentRecorder.Record(page);
        Assert.Equal(0, recorder.Untagged);
        Assert.Equal(0, recorder.Mixed);
        Assert.Equal(recorder.Mcids, TreeOrder(root, page));
        string text = page.ExtractText().PlainText;
        int position = -1;
        foreach (string expected in new[] { "Results", "Revenue grew.", "North", "South", "Region", "Sales", "East", "12", "Figure 1. Sales", "See the site for more.", "Bonjour" })
        {
            int found = text.IndexOf(expected, position + 1, StringComparison.Ordinal);
            Assert.True(found > position, $"'{expected}' is out of order in: {text}");
            position = found;
        }

        Assert.DoesNotContain("Running head", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_cells_carry_RowSpan_ColSpan_Scope_and_Headers()
    {
        using PdfDocument document = NewDocument();
        var text = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10);
        var table = new Table(ColumnWidth.Fixed(80), ColumnWidth.Fixed(80), ColumnWidth.Fixed(80)) { HeaderRows = 1 };
        table.AddRow(new TableCell("Region", text), new TableCell("Q1 and Q2", text) { ColumnSpan = 2 });
        table.AddRow(new TableCell("North", text) { RowSpan = 2, Scope = TableHeaderScope.Row }, new TableCell("1", text), new TableCell("2", text));
        table.AddRow(new TableCell("3", text), new TableCell("4", text));
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(table);

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        PdfStructureTreeRoot root = reopened.StructureTree!;
        Assert.Equal("Document(Table(THead(TR(TH(P),TH(P))),TBody(TR(TH(P),TD(P),TD(P)),TR(TD(P),TD(P)))))", Shape(root.Children));
        List<PdfStructureElement> cells = [.. root.Elements.Where(element => element.StructureType?.Value is "TH" or "TD")];
        PdfTableAttributes Attributes(int index) => Assert.Single(cells[index].Attributes.OfType<PdfTableAttributes>());
        Assert.Equal("Column", Attributes(0).Scope);
        Assert.Equal(2, Attributes(1).ColumnSpan);
        Assert.Equal("Column", Attributes(1).Scope);
        Assert.Equal("Row", Attributes(2).Scope);
        Assert.Equal(2, Attributes(2).RowSpan);
        Assert.Null(Attributes(2).ColumnSpan);
        IReadOnlyList<CosString> headers = Attributes(3).Headers!;
        Assert.Equal([cells[1].Id!, cells[2].Id!], headers);
        Assert.Equal([cells[1].Id!, cells[2].Id!], Attributes(6).Headers!);
    }

    [Fact]
    public void Headers_footers_backgrounds_rules_repeated_headers_and_underlines_are_artifacts_and_everything_else_is_tagged()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var text = new ParagraphStyle(font, 10);
        var footer = new TextStyle(font, 8);
        var template = new PageTemplate(PageSetup.Letter)
        {
            Header = new HeaderFooter(footer) { Left = "Running head" },
            Footer = new HeaderFooter(footer) { Center = "Page {page} of {pages}" },
            BackgroundColor = PdfColor.FromRgb(0.95f, 0.95f, 0.9f),
        };
        var flow = new FlowDocument(template) { Tagging = FlowTagging.Default };
        var table = new Table(ColumnWidth.Fixed(100), ColumnWidth.Proportional()) { HeaderRows = 1 };
        table.AddRow(new TableCell("Key", text), new TableCell("Value", text));
        for (int row = 0; row < 70; row++)
        {
            table.AddRow(new TableCell($"K{row}", text), new TableCell($"Value {row}", text));
        }

        flow.Add(new Paragraph([new TextRun("Underlined", new TextStyle(font, 10).WithUnderline(true)), new TextRun(" plain")], text));
        flow.Add(new Paragraph([new TextRun(string.Join(' ', Enumerable.Repeat("Long paragraph text", 40)), new TextStyle(font, 10).WithStrikeThrough(true))], text));
        flow.Add(table);

        FlowLayoutResult result = flow.Render(document);

        Assert.True(result.Pages.Count >= 2);
        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfStructureTreeRoot root = reopened.StructureTree!;
        for (int index = 0; index < reopened.Pages.Count; index++)
        {
            PdfPage page = reopened.Pages[index];
            FlowContentRecorder recorder = FlowContentRecorder.Record(page);
            Assert.Equal(0, recorder.Untagged);
            Assert.Equal(0, recorder.Mixed);
            Assert.Contains("Pagination/Header", recorder.Artifacts);
            Assert.Contains("Pagination/Footer", recorder.Artifacts);
            Assert.Contains("Background/", recorder.Artifacts);
            Assert.Equal(recorder.Mcids, TreeOrder(root, page));
            Assert.DoesNotContain("Running head", page.ExtractText().PlainText, StringComparison.Ordinal);
            if (index > 0)
            {
                Assert.Contains("Pagination/", recorder.Artifacts);
                Assert.DoesNotContain("Key", recorder.BodyText.Replace("Running head", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            }
        }

        // The table continued on the next page is one Table with one THead and one TBody.
        Assert.Equal(1, root.Elements.Count(element => element.StructureType?.Value == "Table"));
        Assert.Equal(1, root.Elements.Count(element => element.StructureType?.Value == "THead"));
        Assert.Equal(1, root.Elements.Count(element => element.StructureType?.Value == "TBody"));
        Assert.Equal(71, root.Elements.Count(element => element.StructureType?.Value == "TR"));
    }

    [Fact]
    public void A_paragraph_split_across_pages_is_one_element_with_content_on_both_pages()
    {
        using PdfDocument document = NewDocument();
        var text = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new Paragraph(string.Join(' ', Enumerable.Repeat("word", 2500)), text));

        FlowLayoutResult result = flow.Render(document);

        Assert.True(result.Pages.Count >= 2);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        PdfStructureElement paragraph = Assert.Single(reopened.StructureTree!.Elements, element => element.StructureType?.Value == "P");
        List<PdfMarkedContentReference> content = [.. paragraph.Children.OfType<PdfMarkedContentReference>()];
        Assert.Equal(result.Pages.Count, content.Count);
        Assert.Equal(result.Pages.Count, content.Select(item => item.Page!.Dictionary).Distinct().Count());
    }

    [Fact]
    public void A_figure_without_alternate_text_records_a_diagnostic()
    {
        using PdfDocument document = NewDocument();
        var text = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new ImageBlock(Image(document), 20, 20));
        flow.Add(new Paragraph([new TextRun("Icon "), new InlineImage(Image(document), 8, 8) { AlternateText = "warning" }], text));

        FlowLayoutResult result = flow.Render(document);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("FigureWithoutAlternateText", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        PdfStructureElement figure = document.StructureTree!.Elements.First(element => element.StructureType?.Value == "Figure");
        Assert.Equal(figure.Reference, diagnostic.ObjectReference);
        Assert.Null(figure.AlternateDescription);

        using PdfDocument untagged = NewDocument();
        var plain = new FlowDocument(PageSetup.Letter);
        plain.Add(new ImageBlock(Image(untagged), 20, 20));
        Assert.Empty(plain.Render(untagged).Diagnostics);
        Assert.Null(untagged.StructureTree);
    }

    [Fact]
    public void Custom_types_are_role_mapped_and_the_PDF_17_namespace_writes_no_NS()
    {
        using PdfDocument document = NewDocument();
        var text = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10);
        var flow = new FlowDocument(PageSetup.Letter)
        {
            Tagging = FlowTagging.Default.WithNamespace(FlowStructureNamespace.Pdf17).WithRoleMapping("Heading", "H1"),
        };
        flow.Add(new Paragraph("Title", text) { StructureType = "Heading" });
        flow.Add(new Paragraph("Note", text) { StructureType = "Callout" });
        flow.Add(new Paragraph("Quote", text) { StructureType = "BlockQuote" });

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        PdfStructureTreeRoot root = reopened.StructureTree!;
        Assert.Equal("Document(Heading,Callout,BlockQuote)", Shape(root.Children));
        Assert.All(root.Elements, element => Assert.False(element.Dictionary.ContainsKey(new CosName("NS"))));
        Assert.Equal(["H1", "P", "BlockQuote"], root.Elements.Skip(1).Select(element => element.StandardType!.Name));
        Assert.Throws<ArgumentException>(() => FlowTagging.Default.WithRoleMapping("P", "Div"));
        Assert.Throws<ArgumentException>(() => FlowTagging.Default.WithRoleMapping("Custom", "Aside"));
    }

    [Fact]
    public void Sections_become_Sect_elements_and_a_caption_before_a_table_moves_into_it()
    {
        using PdfDocument document = NewDocument();
        var text = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10);
        var table = new Table(ColumnWidth.Fixed(100)) { HeaderRows = 0 };
        table.AddRow(new TableCell("Cell", text));
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new Paragraph("Intro", text));
        flow.StartSection(new PageTemplate(PageSetup.A4));
        flow.Add(new Paragraph("Table 1. Totals", text) { StructureType = "Caption" });
        flow.Add(table);

        flow.Render(document);

        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal("Document(P,Sect(Table(Caption,TBody(TR(TD(P))))))", Shape(reopened.StructureTree!.Children));
        Assert.Equal(FlowContentRecorder.Record(reopened.Pages[1]).Mcids, TreeOrder(reopened.StructureTree!, reopened.Pages[1]));
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_tagged_report_passes_the_structure_checks_of_PDF_UA_1()
    {
        using PdfDocument document = NewDocument();
        (FlowDocument flow, _) = Report(document, FlowTagging.Default.WithLanguage("en-US").WithTitle("Quarterly report").WithNamespace(FlowStructureNamespace.Pdf17));
        flow.Render(document);

        // Smoke check: PDF/UA identification, fonts and metadata are #204's; report what fails without asserting it.
        IReadOnlyList<string> failed = VeraPdf.FailedRules(Saved(document), "ua1");
        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, failed));
        Assert.DoesNotContain(failed, rule => rule.StartsWith("7.1-1:", StringComparison.Ordinal) || rule.StartsWith("7.1-2:", StringComparison.Ordinal) || rule.StartsWith("7.1-3:", StringComparison.Ordinal));
    }

    private static (FlowDocument Flow, PdfCanvasFont Font) Report(PdfDocument document, FlowTagging tagging)
    {
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var body = new ParagraphStyle(font, 10);
        var heading = new ParagraphStyle(document.GetStandard14Font(Standard14Font.HelveticaBold), 16);
        var small = new TextStyle(font, 8);
        var template = new PageTemplate(PageSetup.Letter)
        {
            Header = new HeaderFooter(small) { Left = "Running head" },
            Footer = new HeaderFooter(small) { Center = "Page {page}" },
        };
        var flow = new FlowDocument(template) { Tagging = tagging };
        flow.Add(new Paragraph("Results", heading) { StructureType = "H1" });
        flow.Add(new Paragraph("Revenue grew.", body));
        flow.Add(new ListBlock(new ListStyle(new TextStyle(font, 10))).Add(new ListItem("North", body)).Add(new ListItem("South", body)));
        var table = new Table(ColumnWidth.Fixed(100), ColumnWidth.Fixed(100)) { HeaderRows = 1 };
        table.AddRow(new TableCell("Region", body), new TableCell("Sales", body));
        table.AddRow(new TableCell("East", body), new TableCell("12", body));
        flow.Add(table);
        flow.Add(new ImageBlock(Image(document), 40, 40) { AlternateText = "Sales chart" });
        flow.Add(new Paragraph("Figure 1. Sales", body) { StructureType = "Caption" });
        flow.Add(new Paragraph([new TextRun("See "), new TextRun("the site") { Link = new Uri("https://example.com/") }, new TextRun(" for more.")], body));
        flow.Add(new Paragraph([new TextRun("Bonjour") { Language = "fr" }], body));
        return (flow, font);
    }

    /// <summary>The element types of the tree as <c>Type(child,child)</c>, content items left out.</summary>
    private static string Shape(IEnumerable<PdfStructureItem> items) => string.Join(',', items.OfType<PdfStructureElement>().Select(element =>
    {
        string children = Shape(element.Children);
        return children.Length == 0 ? element.StructureType!.Value : $"{element.StructureType!.Value}({children})";
    }));

    /// <summary>The MCIDs of <paramref name="page"/> in the order a depth-first walk of the tree meets them.</summary>
    private static List<int> TreeOrder(PdfStructureTreeRoot root, PdfPage page)
    {
        var mcids = new List<int>();
        Walk(root.Children);
        return mcids;

        void Walk(IEnumerable<PdfStructureItem> items)
        {
            foreach (PdfStructureItem item in items)
            {
                switch (item)
                {
                    case PdfMarkedContentReference content when ReferenceEquals(content.Page?.Dictionary, page.Dictionary):
                        mcids.Add(content.Mcid);
                        break;
                    case PdfStructureElement element:
                        Walk(element.Children);
                        break;
                }
            }
        }
    }

    private static string? Text(CosObject value) => value is CosString text
        ? text.Bytes is [0xFE, 0xFF, ..] ? System.Text.Encoding.BigEndianUnicode.GetString(text.Bytes[2..]) : System.Text.Encoding.ASCII.GetString(text.Bytes)
        : null;

    private static PdfImage Image(PdfDocument document) =>
        document.CreateImage([0, 64, 128, 255], new PdfImageLayout(2, 2, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);

    private static void AssertQpdfPasses(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    private static PdfDocument NewDocument()
    {
        PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        return document;
    }

    private static byte[] Saved(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
