using Broadside.Fonts;
using Broadside.Layout;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Layout;

/// <summary>
/// Figures and captions detected on untagged pages (issue #190): vector drawings and images become <see cref="LayoutFigure"/>s
/// holding the text inside them, captions are found by their labels next to figures and tables, invisible recognised text over a
/// scan stays in its figure, and glyphs with no Unicode keep their placeholders, checked against <c>layout-figures.pdf</c>.
/// </summary>
public sealed class FigureDetectionTests
{
    [Fact]
    public void Figures_take_their_place_in_the_reading_order_and_their_captions_move_into_them()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-figures.pdf"));

        LayoutTree tree = document.ExtractLayout();

        Assert.Equal(
            ["Heading", "Paragraph", "Figure", "Paragraph", "Table", "Figure", "Paragraph", "Heading", "Figure", "Paragraph", "Paragraph"],
            tree.Root.Children.Select(static node => node.Kind.ToString()));
        Assert.All(tree.Pages, static page => Assert.True(page.IsDetected));
        Assert.Equal("Figure 1 shows sales by quarter; the paragraph after it is running\ntext that refers to the figure and is not a caption.", tree.Root.Children[3].Text);
    }

    [Fact]
    public void A_chart_drawn_with_paths_is_a_figure_holding_its_labels_with_its_caption_last()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-figures.pdf"));

        var chart = Assert.IsType<LayoutFigure>(document.ExtractLayout().Root.Children[2]);

        // The axes run from (100, 540) to (380, 680); the labels under the axis widen the box down to their glyphs.
        PdfRectangle box = chart.BoundingBox!.Value;
        Assert.Equal(100, box.Left, 1);
        Assert.Equal(380, box.Right, 1);
        Assert.Equal(680, box.Top, 1);
        Assert.InRange(box.Bottom, 525, 530);
        LayoutNode caption = Assert.Single(chart.Children);
        Assert.Equal(LayoutNodeKind.Caption, caption.Kind);
        Assert.Equal("Figure 1: Quarterly sales drawn as a bar chart.", caption.Text);
        Assert.Equal("Sales\nQ1 Q2 Q3 Q4\nFigure 1: Quarterly sales drawn as a bar chart.", chart.Text);

        // The figure's extent covers its labels and its caption, which follow each other in the page's rebuilt text.
        LayoutExtent extent = Assert.Single(chart.Extents);
        Assert.Equal(caption.Extents[0].GlyphStart + caption.Extents[0].GlyphCount, extent.GlyphStart + extent.GlyphCount);
        Assert.True(extent.Bounds!.Value.Bottom < box.Bottom, "the extent reaches the caption below the chart");
    }

    [Fact]
    public void A_table_captioned_above_has_its_caption_first()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-figures.pdf"));

        var table = Assert.IsType<LayoutTable>(document.ExtractLayout().Root.Children[4]);

        Assert.Equal((3, 2), (table.RowCount, table.ColumnCount));
        Assert.Equal(LayoutNodeKind.Caption, table.Children[0].Kind);
        Assert.Equal("Table 1: Fruit prices.", table.Children[0].Text);
        Assert.All(table.Children.Skip(1), static row => Assert.Equal(LayoutNodeKind.TableRow, row.Kind));
        Assert.StartsWith("Table 1: Fruit prices.\n", table.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_is_a_figure_placed_by_its_matrix()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-figures.pdf"));

        var image = Assert.IsType<LayoutFigure>(document.ExtractLayout().Root.Children[5]);

        Assert.Equal(new PdfRectangle(72, 230, 192, 320), image.BoundingBox);
        Assert.Equal("Figure 2. A raster image placed by its matrix.", Assert.Single(image.Children).Text);
    }

    [Fact]
    public void Invisible_text_recognised_over_a_scan_stays_in_the_figure()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-figures.pdf"));

        LayoutTree tree = document.ExtractLayout();

        var scan = Assert.IsType<LayoutFigure>(tree.Root.Children[8]);
        Assert.Equal(new PdfRectangle(72, 480, 372, 680), scan.BoundingBox);
        Assert.Equal("Recognised text laid over\na scanned image.\nFigure 3: A scan with its recognised text.", scan.Text);
        LayoutExtent extent = scan.Extents[0];
        TextPage text = extent.Page.Text;
        Assert.Contains(text.Glyphs.Skip(extent.GlyphStart).Take(extent.GlyphCount), static glyph => (glyph.Flags & TextGlyphFlags.Invisible) != 0);
        Assert.DoesNotContain(tree.Root.Children, static node => node.Kind == LayoutNodeKind.Paragraph && node.Text.Contains("Recognised", StringComparison.Ordinal));
    }

    [Fact]
    public void Glyphs_of_a_Type_3_font_without_Unicode_are_flagged_placeholders_and_their_procedures_are_not_figures()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-figures.pdf"));

        LayoutNode line = document.ExtractLayout().Root.Children[10];

        Assert.Equal(LayoutNodeKind.Paragraph, line.Kind);
        Assert.Equal("����", line.Text);
        LayoutExtent extent = Assert.Single(line.Extents);
        TextGlyph[] glyphs = [.. extent.Page.Text.Glyphs.Skip(extent.GlyphStart).Take(extent.GlyphCount)];
        Assert.Equal(4, glyphs.Length);
        Assert.All(glyphs, static glyph => Assert.Equal(UnicodeSource.Unmapped, glyph.UnicodeSource));
        Assert.All(glyphs, static glyph => Assert.IsType<PdfType3Font>(glyph.Font));
    }

    [Fact]
    public void A_Type_3_font_with_ToUnicode_reads_as_a_paragraph_and_its_glyph_drawings_are_not_figures()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));

        LayoutTree tree = document.ExtractLayout();

        Assert.True(tree.Pages[0].IsDetected);
        LayoutNode paragraph = Assert.Single(tree.Root.Children);
        Assert.Equal(LayoutNodeKind.Paragraph, paragraph.Kind);
        Assert.Equal("■▲●\n■▲●", paragraph.Text);
    }

    [Fact]
    public void Every_page_of_charts_scatter_plots_and_images_gives_three_captioned_figures()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.Figures(3));

        LayoutTree tree = document.ExtractLayout();

        LayoutFigure[] figures = [.. tree.Root.Children.OfType<LayoutFigure>()];
        Assert.Equal(9, figures.Length);
        Assert.Equal(
            Enumerable.Range(1, 9).Select(static number => $"Figure {number}"),
            figures.Select(static figure => Assert.Single(figure.Children).Text[..(figure.Children[0].Text.IndexOfAny([':', '.']))]));
        Assert.Equal(LayoutNodeKind.Caption, figures[2].Children[0].Kind);
        Assert.StartsWith("Figure 3: An image.", figures[2].Text, StringComparison.Ordinal);
        Assert.Equal(new PdfRectangle(72, 300, 372, 500), figures[1].BoundingBox);
        Assert.Contains("B20", figures[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(tree.Root.Children, static node => node.Kind == LayoutNodeKind.Paragraph && node.Text.Contains("Figure", StringComparison.Ordinal));
    }

    [Fact]
    public void Detection_is_deterministic()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-figures.pdf"));

        string first = LayoutProjection.Of(document.ExtractLayout());
        string second = LayoutProjection.Of(document.ExtractLayout());

        Assert.Equal(first, second);
    }
}
