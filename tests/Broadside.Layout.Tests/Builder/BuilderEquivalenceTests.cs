using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests.Builder;

/// <summary>
/// The document builder is a front over the flow engine, never a second layout engine (issue #397, test plan "Equivalence tests"): for
/// each element, what the builder writes is byte-identical to a <see cref="FlowDocument"/> built by hand with the same resolved styles.
/// </summary>
public sealed class BuilderEquivalenceTests
{
    [Fact]
    public void A_heading_and_a_paragraph_write_the_same_bytes_as_the_hand_built_flow()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Hello")
            .WithLanguage("en-US")
            .Content(c =>
            {
                c.Heading(1, "Hello, Broadside");
                c.Paragraph("This page is tagged, embeds its font and opens in any reader.");
            }));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont bold = Embed(twin, Standard14Font.HelveticaBold);
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle("Hello"), OutlineDepth = 3 };
        flow.Add(new Paragraph("Hello, Broadside", new ParagraphStyle(bold, 22).WithSpaceBefore(12).WithSpaceAfter(6)) { StructureType = "H1" });
        flow.Add(new Paragraph("This page is tagged, embeds its font and opens in any reader.", new ParagraphStyle(regular, 11).WithSpaceAfter(6)));
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }

    [Fact]
    public void Spans_with_their_own_styles_write_the_same_bytes_as_hand_built_runs()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Spans")
            .WithLanguage("en")
            .Content(c => c.Paragraph(t =>
            {
                t.Text("Pay by bank transfer to ");
                t.Text("GB33 BUKB 2020 1555 5555 55").Bold();
                t.Text(", or read the ");
                t.Link("terms", new Uri("https://example.org/terms"));
                t.Text(".");
                t.Text(" Merci.").Language("fr");
            }).Justify()));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        PdfCanvasFont bold = Embed(twin, Standard14Font.HelveticaBold);
        var accent = PdfColor.FromRgb(0x0B / 255f, 0x5F / 255f, 0xFF / 255f);
        var style = new ParagraphStyle(regular, 11).WithSpaceAfter(6).WithAlignment(TextAlignment.Justify);
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en").WithTitle("Spans"), OutlineDepth = 3 };
        flow.Add(new Paragraph(
            [
                new TextRun("Pay by bank transfer to "),
                new TextRun("GB33 BUKB 2020 1555 5555 55", new TextStyle(bold, 11)),
                new TextRun(", or read the "),
                new TextRun("terms", new TextStyle(regular, 11).WithColor(accent).WithUnderline(true)) { Link = new Uri("https://example.org/terms") },
                new TextRun("."),
                new TextRun(" Merci.") { Language = "fr" },
            ],
            style));
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }

    [Fact]
    public void A_table_with_a_header_row_a_row_header_spans_striping_and_a_caption_writes_the_same_bytes_as_the_hand_built_table()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Results")
            .WithLanguage("en-GB")
            .Content(c => c.Table(t =>
            {
                t.Columns(ColumnWidth.Proportional(3), ColumnWidth.Proportional(1), Length.Millimeters(28));
                t.Header(h =>
                {
                    h.Cell("Region");
                    h.Cell("Q3").AlignRight();
                    h.Cell("Q4").AlignRight();
                });
                t.Row(r =>
                {
                    r.HeaderCell("North");
                    r.Cell("120").AlignRight();
                    r.Cell("135").AlignRight();
                });
                t.Row("South", "98", "104");
                t.Row(r => r.Cell("Figures are provisional.").ColumnSpan(3).Italic());
            })
            .Caption("Table 1. Units shipped by region")
            .Striped()
            .Border(0.5, PdfColor.FromGray(0.5f))));

        using PdfDocument twin = NewTwin();
        // The builder embeds each face when it first resolves a style that uses it, in document order: the caption, the header, the body.
        PdfCanvasFont italic = Embed(twin, Standard14Font.HelveticaOblique);
        PdfCanvasFont bold = Embed(twin, Standard14Font.HelveticaBold);
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        var caption = new ParagraphStyle(italic, 9).WithSpaceBefore(3).WithSpaceAfter(9);
        var cell = new ParagraphStyle(regular, 11);
        var right = cell.WithAlignment(TextAlignment.Right);
        var header = new ParagraphStyle(bold, 11);
        var headerRight = header.WithAlignment(TextAlignment.Right);
        var subtle = PdfColor.FromRgb(0xF2 / 255f, 0xF4 / 255f, 0xF6 / 255f);
        var table = new Table(ColumnWidth.Proportional(3), ColumnWidth.Proportional(1), ColumnWidth.Fixed(28 * 72 / 25.4))
        {
            HeaderRows = 1,
            Style = Theme.Default.Table.WithBorder(new TableBorder(0.5, PdfColor.FromGray(0.5f))),
            SpaceAfter = 6,
        };
        table.AddRow(new TableCell("Region", header), new TableCell("Q3", headerRight), new TableCell("Q4", headerRight));
        table.AddRow(new TableCell("North", header) { Scope = TableHeaderScope.Row }, new TableCell("120", right), new TableCell("135", right));
        table.Add(new TableRow(new TableCell("South", cell), new TableCell("98", cell), new TableCell("104", cell)) { Background = subtle });
        table.AddRow(new TableCell(new Paragraph("Figures are provisional.", new ParagraphStyle(italic, 11))) { ColumnSpan = 3 });
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en-GB").WithTitle("Results"), OutlineDepth = 3 };
        flow.Add(new BlockGroup(new Paragraph("Table 1. Units shipped by region", caption) { StructureType = "Caption" }) { KeepWithNext = true });
        flow.Add(table);
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }

    [Fact]
    public void A_narrow_right_aligned_totals_table_writes_the_same_bytes_as_the_hand_built_table()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Totals")
            .WithLanguage("en-GB")
            .Content(c => c.Table(t =>
            {
                t.Columns(ColumnWidth.Proportional(1), Length.Millimeters(30));
                t.Row(r =>
                {
                    r.HeaderCell("Subtotal");
                    r.Cell("1,000.00").AlignRight();
                });
            })
            .Width(Length.Millimeters(80)).AlignRight().KeepTogether().Border(TableBorder.None)));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont bold = Embed(twin, Standard14Font.HelveticaBold);
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        var table = new Table(ColumnWidth.Proportional(1), ColumnWidth.Fixed(30 * 72 / 25.4))
        {
            Style = Theme.Default.Table.WithBorder(TableBorder.None),
            SpaceAfter = 6,
            Width = 80 * 72 / 25.4,
            Alignment = TextAlignment.Right,
        };
        table.AddRow(
            new TableCell("Subtotal", new ParagraphStyle(bold, 11)) { Scope = TableHeaderScope.Row },
            new TableCell("1,000.00", new ParagraphStyle(regular, 11).WithAlignment(TextAlignment.Right)));
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en-GB").WithTitle("Totals"), OutlineDepth = 3 };
        flow.Add(new BlockGroup(table) { KeepTogether = true });
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }

    [Fact]
    public void Spacers_rules_page_breaks_groups_and_keep_options_write_the_same_bytes_as_the_hand_built_blocks()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Blocks")
            .WithLanguage("en")
            .Content(c =>
            {
                c.Heading(2, "Kept with the next paragraph").KeepWithNext();
                c.Paragraph("First.");
                c.Spacer(10);
                c.Rule();
                c.PageBreak();
                c.Group(g => g.Paragraph("Aside text.")).Role("Aside").KeepTogether();
                c.Paragraph("Kept together.").KeepTogether();
            }));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont bold = Embed(twin, Standard14Font.HelveticaBold);
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        var body = new ParagraphStyle(regular, 11).WithSpaceAfter(6);
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en").WithTitle("Blocks"), OutlineDepth = 3 };
        flow.Add(new BlockGroup(new Paragraph("Kept with the next paragraph", new ParagraphStyle(bold, 11 * 1.6).WithSpaceBefore(12).WithSpaceAfter(6)) { StructureType = "H2" }) { KeepWithNext = true });
        flow.Add(new Paragraph("First.", body));
        flow.Add(new Spacer(10));
        flow.Add(new Rule { Color = PdfColor.FromHex("#C8CDD2") });
        flow.Add(new PageBreak());
        flow.Add(new BlockGroup(new Paragraph("Aside text.", body)) { KeepTogether = true, IsTagged = true, StructureType = "Aside" });
        flow.Add(new BlockGroup(new Paragraph("Kept together.", body)) { KeepTogether = true });
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
        Assert.Equal(2, built.Pages.Count);
    }

    [Fact]
    public void A_nested_list_writes_the_same_bytes_as_the_hand_built_list()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Lists")
            .WithLanguage("en")
            .Content(c => c.List(l =>
            {
                l.Item("Add the Broadside package.");
                l.Item(i =>
                {
                    i.Paragraph("Add the optional packages you need:");
                    i.List(sub =>
                    {
                        sub.Item("Broadside.Rendering, for PNG output");
                        sub.Item("Broadside.Fonts.Standard14, for non-embedded fonts");
                    }).Bullet("-");
                });
            }).Numbered()));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        var body = new ParagraphStyle(regular, 11).WithSpaceAfter(6);
        var marker = new TextStyle(regular, 11);
        var inner = new ListBlock(new ListStyle(marker).WithBullet("-"));
        inner.Add(new ListItem("Broadside.Rendering, for PNG output", body));
        inner.Add(new ListItem("Broadside.Fonts.Standard14, for non-embedded fonts", body));
        var outer = new ListBlock(new ListStyle(marker).WithNumbering(ListNumbering.Decimal));
        outer.Add(new ListItem("Add the Broadside package.", body));
        outer.Add(new ListItem(new Paragraph("Add the optional packages you need:", body), inner));
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en").WithTitle("Lists"), OutlineDepth = 3 };
        flow.Add(outer);
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }

    [Fact]
    public void A_section_with_a_header_and_page_number_footer_writes_the_same_bytes_as_the_hand_built_template()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Report")
            .WithLanguage("en")
            .Section(s => s
                .Size(PageSize.Letter)
                .Margins(Length.Inches(1))
                .FirstPageHeader(h => { })
                .Header(h => h.Left("Field report").Right("North team"))
                .Footer(f => f.Center($"Page {PageField.Number} of {PageField.Count}"))
                .Content(c =>
                {
                    for (int index = 0; index < 120; index++)
                    {
                        c.Paragraph($"Note {index}: the north site reported steady flow.");
                    }
                })));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        var muted = PdfColor.FromRgb(0x5B / 255f, 0x67 / 255f, 0x70 / 255f);
        var region = new TextStyle(regular, 8.5).WithColor(muted);
        var template = new PageTemplate(new PageSetup(612, 792, new PageMargins(72)))
        {
            FirstPageHeader = HeaderFooter.None,
            Header = new HeaderFooter(region) { Left = "Field report", Right = "North team" },
            Footer = new HeaderFooter(region) { Center = "Page {page} of {pages}" },
        };
        var body = new ParagraphStyle(regular, 11).WithSpaceAfter(6);
        var flow = new FlowDocument(template) { Tagging = FlowTagging.Default.WithLanguage("en").WithTitle("Report"), OutlineDepth = 3 };
        for (int index = 0; index < 120; index++)
        {
            flow.Add(new Paragraph($"Note {index}: the north site reported steady flow.", body));
        }

        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }
}
