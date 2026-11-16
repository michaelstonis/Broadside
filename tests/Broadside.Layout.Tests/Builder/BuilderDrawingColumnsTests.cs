using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Layout.Flow.Units;
using Broadside.Structure;
using Broadside.TestSupport;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests.Builder;

/// <summary>
/// The document builder's drawings, columns and page decorations (issue #397, B5): each writes the same bytes as the engine block it
/// fronts, drawings are figures with alternate text or artifacts, columns are one <c>Div</c> each, and decorations, backgrounds and
/// watermarks are artifacts on every page of their section.
/// </summary>
public sealed class BuilderDrawingColumnsTests
{
    private static readonly Action<PdfCanvas, PdfRectangle> Bars = static (canvas, box) =>
    {
        canvas.SetFillColor(PdfColor.FromRgb(0.04f, 0.37f, 1f));
        canvas.Rectangle(0, 0, box.Width / 3, box.Height / 2);
        canvas.Rectangle(box.Width / 2, 0, box.Width / 3, box.Height);
        canvas.Fill();
    };

    [Fact]
    public void A_drawing_with_a_caption_writes_the_same_bytes_as_the_hand_built_drawing()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Chart")
            .WithLanguage("en")
            .Content(c => c.Drawing(120.Mm, 50.Mm, "Bar chart of two regions.", Bars).AlignCenter().Caption("Figure 1. Units shipped")));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont italic = Embed(twin, Standard14Font.HelveticaOblique);
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en").WithTitle("Chart") };
        flow.Add(new BlockGroup(
            new DrawingBlock(Length.Millimeters(120).ToPoints(), Length.Millimeters(50).ToPoints(), Bars)
            {
                AlternateText = "Bar chart of two regions.",
                Alignment = TextAlignment.Center,
                SpaceAfter = 6,
            },
            new Paragraph("Figure 1. Units shipped", new ParagraphStyle(italic, 9).WithSpaceBefore(3).WithSpaceAfter(9)) { StructureType = "Caption" })
        { KeepTogether = true });
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }

    [Fact]
    public void Columns_write_the_same_bytes_as_the_hand_built_columns()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Columns")
            .WithLanguage("en")
            .Content(c => c.Columns(cols =>
            {
                cols.Column(col => col.Paragraph("Acme Instruments"));
                cols.Column(60.Mm, col => col.Paragraph("Invoice 2026-0042").AlignRight());
            }).Gap(18)));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        var style = new ParagraphStyle(regular, 11).WithSpaceAfter(6);
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en").WithTitle("Columns") };
        flow.Add(new ColumnsBlock(
            new FlowColumn(new Paragraph("Acme Instruments", style)),
            new FlowColumn(Length.Millimeters(60), new Paragraph("Invoice 2026-0042", style.WithAlignment(TextAlignment.Right))))
        { Gap = 18, SpaceAfter = 6 });
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }

    [Fact]
    public void Decorate_background_and_watermark_write_the_same_bytes_as_the_hand_built_template()
    {
        Action<PdfCanvas, PageInfo> border = static (canvas, page) =>
        {
            canvas.SetStrokeColor(PdfColor.FromGray(0.8f));
            canvas.Rectangle(page.Margins.Left, page.Margins.Bottom, page.ContentWidth, page.ContentHeight);
            canvas.Stroke();
        };
        PdfColor paper = PdfColor.FromRgb(1f, 0.98f, 0.94f);
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Draft")
            .WithLanguage("en")
            .Section(s => s
                .Decorate(border)
                .Background(paper)
                .Watermark("DRAFT")
                .Content(c => c.Paragraph("Body"))));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont bold = Embed(twin, Standard14Font.HelveticaBold);
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)
        {
            Decoration = border,
            BackgroundColor = paper,
            Watermark = new Watermark("DRAFT", new TextStyle(bold, 60).WithColor(Theme.Default.Muted)),
        })
        { Tagging = FlowTagging.Default.WithLanguage("en").WithTitle("Draft") };
        flow.Add(new Paragraph("Body", new ParagraphStyle(regular, 11).WithSpaceAfter(6)));
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }

    [Fact]
    public void A_drawing_is_a_figure_with_its_alternate_text_and_caption_and_its_callback_runs_once_with_its_box()
    {
        int calls = 0;
        PdfRectangle seen = default;
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc.WithTitle("t").WithLanguage("en").Content(c =>
        {
            c.Paragraph("Before.");
            c.Drawing(40.Mm, 20.Mm, "A red square.", (canvas, box) =>
            {
                calls++;
                seen = box;
                canvas.Rectangle(0, 0, box.Width, box.Height);
                canvas.Fill();
            }).Caption("Figure 1. A square");
            c.DecorativeDrawing(100, 4, Bars);
        }));

        Assert.Equal(1, calls);
        Assert.Equal(Length.Millimeters(40).ToPoints(), seen.Width, 6);
        Assert.Equal(Length.Millimeters(20).ToPoints(), seen.Height, 6);
        using PdfDocument reopened = PdfDocument.Open(Save(pdf));
        Assert.Equal("Document(P,Figure(Caption))", Shape(reopened.StructureTree!.Children));
        PdfStructureElement figure = reopened.StructureTree.Children[0].Children.OfType<PdfStructureElement>().ElementAt(1);
        Assert.Equal("A red square.", figure.AlternateDescription);
        Assert.Empty(pdf.Diagnostics);
    }

    [Fact]
    public void A_drawing_filling_the_width_gets_the_scaled_box()
    {
        PdfRectangle seen = default;
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc.WithTitle("t").WithLanguage("en").Content(c =>
            c.Drawing(100, 50, "A wide band.", (canvas, box) => seen = box).FillWidth()));

        double width = PageSetup.A4.ContentWidth;
        Assert.Equal(width, seen.Width, 6);
        Assert.Equal(width / 2, seen.Height, 6);
    }

    [Fact]
    public void A_drawing_that_leaves_the_canvas_unbalanced_is_restored_with_a_diagnostic()
    {
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc.WithTitle("t").WithLanguage("en").Content(c =>
            c.Drawing(50, 50, "An unfinished drawing.", (canvas, box) => canvas.SaveState())));

        Assert.Contains(pdf.Diagnostics, diagnostic => diagnostic.Code == LayoutDiagnostics.DrawingStateUnbalanced.Name);
        FlowBuildingBlockTests.AssertQpdfPasses(Save(pdf));
    }

    [Fact]
    public void Drawings_refuse_bad_arguments_at_the_call()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(doc => doc.Content(c => c.Drawing(0, 10, "x", Bars))));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(doc => doc.Content(c => c.DecorativeDrawing(10, -1, Bars))));
        Assert.Throws<ArgumentException>(() => PdfDocument.Create(doc => doc.Content(c => c.Drawing(10, 10, " ", Bars))));
        Assert.Throws<ArgumentNullException>(() => PdfDocument.Create(doc => doc.Content(c => c.Drawing(10, 10, "x", null!))));
    }

    [Fact]
    public void Each_column_is_one_div_read_in_column_order_and_inherits_the_columns_style()
    {
        PdfColor red = PdfColor.FromRgb(0.8f, 0.1f, 0.1f);
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc.WithTitle("t").WithLanguage("en").Content(c =>
        {
            c.Columns(cols =>
            {
                cols.Column(col =>
                {
                    col.Heading(1, "Acme");
                    col.Paragraph("1 Reservoir Road");
                });
                cols.Column(col => col.Paragraph("Invoice").AlignRight());
            }).Style(new Style { Color = red });
            c.Paragraph("Below the columns.");
        }));

        using PdfDocument reopened = PdfDocument.Open(Save(pdf));
        Assert.Equal("Document(Div(H1,P),Div(P),P)", Shape(reopened.StructureTree!.Children));
        Assert.Empty(pdf.Diagnostics);
    }

    [Fact]
    public void Columns_need_a_column()
    {
        Assert.Throws<InvalidOperationException>(() => PdfDocument.Create(doc => doc.Content(c => c.Columns(cols => { }))));
    }

    [Fact]
    public void Decorations_run_on_every_page_of_their_section_with_the_page_number_and_count()
    {
        var pages = new List<(int Number, int Count, int Section)>();
        ImageSource paper = ImageSource.FromBytes(PngSamples.Rgb(8, 4, alpha: false, interlaced: false));
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc
            .WithTitle("t")
            .WithLanguage("en")
            .Section(s => s
                .Background(paper)
                .Decorate((canvas, page) => pages.Add((page.Number, page.Count, page.Section)))
                .Content(c =>
                {
                    c.Paragraph("One");
                    c.PageBreak();
                    c.Paragraph("Two");
                }))
            .Section(s => s
                .Background(paper)
                .Watermark("CONFIDENTIAL", new Style { FontSize = 40 })
                .Content(c => c.Paragraph("Three"))));

        Assert.Equal([(1, 3, 0), (2, 3, 0)], pages);
        byte[] bytes = Save(pdf);
        Assert.Equal(1, CountOf(System.Text.Encoding.Latin1.GetString(bytes), "/Subtype /Image"));
        using PdfDocument reopened = PdfDocument.Open(bytes);
        Assert.Contains("CONFIDENTIAL", PagesText(reopened), StringComparison.Ordinal);
        Assert.Equal("Document(P,P,Sect(P))", Shape(reopened.StructureTree!.Children));
        FlowBuildingBlockTests.AssertQpdfPasses(bytes);
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
