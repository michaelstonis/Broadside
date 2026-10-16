using Broadside.Layout;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>
/// Tables detected on untagged pages (issue #189): ruled tables from their lines, unruled ones from aligned white space, with merged
/// cells, header rows and tables continued across a page break, checked cell for cell against <c>layout-tables.pdf</c>.
/// </summary>
public sealed class TableDetectionTests
{
    [Fact]
    public void Tables_sit_in_the_reading_order_between_the_paragraphs()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tables.pdf"));

        LayoutTree tree = document.ExtractLayout();

        Assert.Equal(
            ["Heading", "Paragraph", "Table", "Paragraph", "Table", "Paragraph", "Heading", "Paragraph", "Table", "Table", "Paragraph"],
            tree.Root.Children.Select(static node => node.Kind.ToString()));
        Assert.All(tree.Pages, static page => Assert.True(page.IsDetected));
    }

    [Fact]
    public void A_ruled_table_reads_its_spans_from_the_lines_left_out()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tables.pdf"));

        LayoutTable table = Tables(document.ExtractLayout(), 0)[0];

        Assert.Equal((6, 4), (table.RowCount, table.ColumnCount));
        Assert.Equal(
            [
                "0,0 2x1 H:Region; 0,1 2x1 H:Product; 0,2 1x2 H:Sales",
                "1,2 H:Q1; 1,3 H:Q2",
                "2,0 2x1:North; 2,1:Apples; 2,2:120; 2,3:135",
                "3,1:Pears; 3,2:80; 3,3:95",
                "4,0:South; 4,1:Apples; 4,2:150; 4,3:160",
                "5,0 1x2:Total; 5,2:350; 5,3:390",
            ],
            Grid(table));
        var cell = (LayoutTableCell)table.Children[2].Children[2];
        Assert.Equal(["Sales", "Q1"], cell.Headers.Select(static header => header.Text));
        Assert.Equal(new PdfRectangle(272, 626, 352, 644), cell.Extents[0].Bounds);
        Assert.Null(table.PreviousPart);
        Assert.Null(table.NextPart);
    }

    [Fact]
    public void An_unruled_table_reads_its_columns_from_white_space_and_a_heading_over_two_columns_spans_them()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tables.pdf"));

        LayoutTable table = Tables(document.ExtractLayout(), 0)[1];

        Assert.Equal((6, 4), (table.RowCount, table.ColumnCount));
        Assert.Equal(
            [
                "0,0 H:; 0,1 1x2 H:Temperature; 0,3 H:",
                "1,0 H:City; 1,1 H:Min; 1,2 H:Max; 1,3 H:Rain",
                "2,0:Oslo; 2,1:-4; 2,2:3; 2,3:49",
                "3,0:Lima; 3,1:15; 3,2:22; 3,3:1",
                "4,0:Cairo; 4,1:14; 4,2:25; 4,3:5",
                "5,0:Perth; 5,1:9; 5,2:18; 5,3:146",
            ],
            Grid(table));
        var rain = (LayoutTableCell)table.Children[5].Children[3];
        Assert.Equal(["Rain"], rain.Headers.Select(static header => header.Text));
        Assert.Empty(table.Children[0].Children[0].Extents);
    }

    [Fact]
    public void A_table_spanning_a_page_break_is_two_tables_flagged_as_continued()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tables.pdf"));

        LayoutTree tree = document.ExtractLayout();

        LayoutTable first = Assert.Single(Tables(tree, 1));
        LayoutTable second = Assert.Single(Tables(tree, 2));
        Assert.Same(second, first.NextPart);
        Assert.Same(first, second.PreviousPart);
        Assert.Null(first.PreviousPart);
        Assert.Null(second.NextPart);
        Assert.Equal((12, 3), (first.RowCount, first.ColumnCount));
        Assert.Equal((5, 3), (second.RowCount, second.ColumnCount));
        Assert.Equal("0,0 H:Item; 0,1 H:Qty; 0,2 H:Price", Grid(first)[0]);
        Assert.Equal("11,0:Part 11; 11,1:33; 11,2:13.75", Grid(first)[^1]);
        Assert.Equal("0,0:Part 12; 0,1:36; 0,2:15.00", Grid(second)[0]);
        Assert.Equal("4,0:Part 16; 4,1:48; 4,2:20.00", Grid(second)[^1]);
    }

    [Fact]
    public void Each_page_keeps_every_glyph_once_with_tables_cell_by_cell()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tables.pdf"));

        LayoutTree tree = document.ExtractLayout();

        for (int index = 0; index < document.Pages.Count; index++)
        {
            Assert.Equal(document.Pages[index].ExtractText().Glyphs.Count, tree.Pages[index].Text.Glyphs.Count);
        }

        string again = string.Join('\n', document.ExtractLayout().Root.Children.Select(static node => node.Text));
        Assert.Equal(string.Join('\n', tree.Root.Children.Select(static node => node.Text)), again);
    }

    [Fact]
    public void Tables_repeat_page_after_page_and_are_detected_the_same_each_time()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.Tables(3));

        LayoutTree tree = document.ExtractLayout();

        LayoutTable[] tables = [.. tree.Root.Children.OfType<LayoutTable>()];
        Assert.Equal(6, tables.Length);
        foreach (LayoutTable[] page in tables.Chunk(2))
        {
            string[] ruled = Grid(page[0]);
            Assert.Equal((21, 5), (page[0].RowCount, page[0].ColumnCount));
            Assert.Equal("0,0 H:Head 1; 0,1 H:Head 2; 0,2 H:Head 3; 0,3 H:Head 4; 0,4 H:Head 5", ruled[0]);
            Assert.Equal("1,0 2x1:Group 1; 1,1:48.1; 1,2:59.1; 1,3:70.1; 1,4:81.1", ruled[1]);
            Assert.Equal("2,1:85.2; 2,2:96.2; 2,3:107.2; 2,4:118.2", ruled[2]);
            Assert.Equal(95, page[0].Children.Sum(static row => row.Children.Count));
            Assert.Equal((21, 5), (page[1].RowCount, page[1].ColumnCount));
            Assert.Equal("0,0 H:Col 1; 0,1 H:Col 2; 0,2 H:Col 3; 0,3 H:Col 4; 0,4 H:Col 5", Grid(page[1])[0]);
            Assert.Equal("20,0:Name 20; 20,1:261; 20,2:262; 20,3:263; 20,4:264", Grid(page[1])[^1]);
            Assert.Null(page[0].NextPart);
        }
    }

    [Fact]
    public void Detection_off_finds_no_tables()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tables.pdf"));

        LayoutTree tree = document.ExtractLayout(new LayoutOptions().WithDetection(LayoutDetection.None));

        Assert.Empty(tree.Root.Children);
    }

    private static LayoutTable[] Tables(LayoutTree tree, int pageIndex) =>
        [.. tree.Root.Children.OfType<LayoutTable>().Where(table => table.Extents[0].Page.PageIndex == pageIndex)];

    /// <summary>Each row as <c>row,column[ RxC][ H]:text</c> per cell.</summary>
    internal static string[] Grid(LayoutTable table) =>
        [.. table.Children.Select(static row => string.Join("; ", row.Children.Cast<LayoutTableCell>().Select(static cell =>
            $"{cell.Row},{cell.Column}{(cell.RowSpan != 1 || cell.ColumnSpan != 1 ? $" {cell.RowSpan}x{cell.ColumnSpan}" : "")}"
            + $"{(cell.IsHeader ? " H" : "")}:{cell.Text.Replace('\n', ' ').Trim()}")))];
}
