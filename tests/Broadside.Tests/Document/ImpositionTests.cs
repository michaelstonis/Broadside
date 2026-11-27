using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Graphics;
using Broadside.Text;

namespace Broadside.Tests.Document;

/// <summary>
/// N-up sheets, booklets and tiles (issue #647): each source page drawn as a form XObject (ISO 32000-2 §8.10.1) of its crop box
/// (§14.11.2), its rotation applied (§7.7.3.3), placed in its cell. Placement is checked where the text of each source page lands.
/// </summary>
public sealed class ImpositionTests
{
    private static readonly PdfRectangle Letter = new(0, 0, 612, 792);

    [Fact]
    public void Two_up_puts_two_portrait_pages_side_by_side_on_a_landscape_sheet()
    {
        using PdfDocument source = Source(3);

        using PdfDocument sheets = source.CreateNUp(2, 1);

        Assert.Equal(2, sheets.Pages.Count);
        Assert.Equal(new PdfRectangle(0, 0, 792, 612), sheets.Pages[0].MediaBox);
        PathPoint p1 = Center(sheets.Pages[0], "P1");
        PathPoint p2 = Center(sheets.Pages[0], "P2");
        Assert.InRange(p1.X, 0, 396);
        Assert.InRange(p2.X, 396, 792);
        Assert.Equal(p1.Y, p2.Y, 1);
        Assert.InRange(Center(sheets.Pages[1], "P3").X, 0, 396);
        Assert.DoesNotContain("P4", sheets.Pages[1].ExtractText().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Four_up_scales_each_page_into_a_quarter_of_the_sheet_and_centers_it()
    {
        using PdfDocument source = Source(4);

        using PdfDocument sheets = source.CreateNUp(2, 2);

        PdfPage sheet = Assert.Single(sheets.Pages);
        Assert.Equal(Letter, sheet.MediaBox);

        // Each label is at its page's center, so it lands at its cell's center: (153, 594), (459, 594), (153, 198), (459, 198).
        AssertNear(new PathPoint(153, 594), Center(sheet, "P1"));
        AssertNear(new PathPoint(459, 594), Center(sheet, "P2"));
        AssertNear(new PathPoint(153, 198), Center(sheet, "P3"));
        AssertNear(new PathPoint(459, 198), Center(sheet, "P4"));
    }

    [Theory]
    [InlineData(PdfImpositionOrder.LeftToRightThenDown, new[] { "P1", "P2", "P3", "P4" })]
    [InlineData(PdfImpositionOrder.RightToLeftThenDown, new[] { "P2", "P1", "P4", "P3" })]
    [InlineData(PdfImpositionOrder.TopToBottomThenRight, new[] { "P1", "P3", "P2", "P4" })]
    public void Every_order_fills_the_cells_as_named(PdfImpositionOrder order, string[] topLeftTopRightBottomLeftBottomRight)
    {
        using PdfDocument source = Source(4);

        using PdfDocument sheets = source.CreateNUp(2, 2, new PdfImpositionOptions().WithOrder(order));

        PdfPage sheet = sheets.Pages[0];
        PathPoint[] cells = [new(153, 594), new(459, 594), new(153, 198), new(459, 198)];
        for (int index = 0; index < 4; index++)
        {
            AssertNear(cells[index], Center(sheet, topLeftTopRightBottomLeftBottomRight[index]));
        }
    }

    [Fact]
    public void Margin_and_gutter_shrink_the_cells()
    {
        using PdfDocument source = Source(2);

        using PdfDocument sheets = source.CreateNUp(2, 1, new PdfImpositionOptions().WithMargin(36).WithGutter(20).WithSheetSize(new PdfRectangle(0, 0, 800, 600)));

        // Cells are 354 wide: [36, 390] and [410, 764].
        AssertNear(new PathPoint(213, 300), Center(sheets.Pages[0], "P1"));
        AssertNear(new PathPoint(587, 300), Center(sheets.Pages[0], "P2"));
    }

    [Fact]
    public void A_page_whose_orientation_differs_from_its_cell_is_turned_a_quarter()
    {
        using PdfDocument source = PdfDocument.Create();
        Label(source, source.Pages[0], "P1");
        PdfPage landscape = source.Pages.Add(new PdfRectangle(0, 0, 792, 612));
        Label(source, landscape, "P2");

        using PdfDocument sheets = source.CreateNUp(2, 1);

        TextPage text = sheets.Pages[0].ExtractText();
        Quad upright = Word(text, "P1").Quad;
        Quad turned = Word(text, "P2").Quad;
        Assert.Equal(upright.BottomLeft.Y, upright.BottomRight.Y, 1);
        Assert.Equal(turned.BottomLeft.X, turned.BottomRight.X, 1);
        AssertNear(new PathPoint(594, 306), Center(sheets.Pages[0], "P2"));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void A_rotated_source_is_placed_as_it_is_displayed(int rotation)
    {
        using PdfDocument source = Source(2);
        source.Pages[0].Rotation = rotation;

        using PdfDocument sheets = source.CreateNUp(2, 1);

        // Displayed portrait (180) stays upright in its cell; displayed landscape (90, 270) is turned back to fit the portrait cell.
        AssertNear(new PathPoint(198, 306), Center(sheets.Pages[0], "P1"));
        AssertNear(new PathPoint(594, 306), Center(sheets.Pages[0], "P2"));
    }

    [Theory]
    [InlineData(5, new[] { 8, 1, 2, 7, 6, 3, 4, 5 })]
    [InlineData(8, new[] { 8, 1, 2, 7, 6, 3, 4, 5 })]
    [InlineData(13, new[] { 16, 1, 2, 15, 14, 3, 4, 13, 12, 5, 6, 11, 10, 7, 8, 9 })]
    public void A_booklet_pads_to_a_multiple_of_four_and_orders_the_sides_for_saddle_stitching(int pages, int[] expected)
    {
        Assert.Equal(expected, PageImposition.BookletOrder(pages).SelectMany(side => (int[])[side.Left, side.Right]));

        using PdfDocument source = Source(pages);
        using PdfDocument booklet = source.CreateBooklet();

        Assert.Equal(expected.Length / 2, booklet.Pages.Count);
        Assert.Equal(new PdfRectangle(0, 0, 1224, 792), booklet.Pages[0].MediaBox);
        for (int side = 0; side < booklet.Pages.Count; side++)
        {
            string text = booklet.Pages[side].ExtractText().Text;
            int left = expected[2 * side];
            int right = expected[(2 * side) + 1];
            if (left <= pages)
            {
                Assert.InRange(Center(booklet.Pages[side], $"P{left}").X, 0, 612);
            }

            if (right <= pages)
            {
                Assert.InRange(Center(booklet.Pages[side], $"P{right}").X, 612, 1224);
            }

            Assert.Equal((left <= pages ? 1 : 0) + (right <= pages ? 1 : 0), text.Split('P', StringSplitOptions.RemoveEmptyEntries).Length);
        }
    }

    [Fact]
    public void A_right_to_left_booklet_swaps_each_pair()
    {
        using PdfDocument source = Source(4);
        source.Catalog[new CosName("ViewerPreferences")] = new CosDictionary { [new CosName("Direction")] = new CosName("R2L") };

        using PdfDocument booklet = source.CreateBooklet();

        Assert.InRange(Center(booklet.Pages[0], "P1").X, 0, 612);
        Assert.InRange(Center(booklet.Pages[0], "P4").X, 612, 1224);
    }

    [Fact]
    public void Tiles_split_each_page_and_enlarge_each_tile_with_the_overlap_shared()
    {
        using PdfDocument source = PdfDocument.Create();
        PdfPage page = source.Pages[0];
        page.MediaBox = new PdfRectangle(0, 0, 1000, 500);
        using (PdfCanvas canvas = page.CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(source.GetStandard14Font(Standard14Font.Helvetica), 12);
            canvas.MoveText(100, 250);
            canvas.ShowText("West");
            canvas.MoveText(395, 0);
            canvas.ShowText("Seam");
            canvas.MoveText(300, 0);
            canvas.ShowText("East");
            canvas.EndText();
        }

        using PdfDocument tiles = source.CreateTiles(2, 1, new PdfImpositionOptions().WithOverlap(20).WithSheetSize(new PdfRectangle(0, 0, 510, 500)));

        Assert.Equal(2, tiles.Pages.Count);

        // Tiles are 510 wide: [0, 510] and [490, 1000]; the seam text at x 495 is on both.
        string west = tiles.Pages[0].ExtractText().Text;
        string east = tiles.Pages[1].ExtractText().Text;
        Assert.Contains("West", west, StringComparison.Ordinal);
        Assert.Contains("Seam", west, StringComparison.Ordinal);
        Assert.Contains("Seam", east, StringComparison.Ordinal);
        Assert.Contains("East", east, StringComparison.Ordinal);
        Assert.Equal(100, Word(tiles.Pages[0].ExtractText(), "West").Quad.BottomLeft.X, 1);
        Assert.Equal(5, Word(tiles.Pages[1].ExtractText(), "Seam").Quad.BottomLeft.X, 1);
        Assert.Equal(305, Word(tiles.Pages[1].ExtractText(), "East").Quad.BottomLeft.X, 1);
        string content = System.Text.Encoding.ASCII.GetString(PageContent(tiles, tiles.Pages[1]));
        Assert.Contains("re", content, StringComparison.Ordinal);
        Assert.Contains("W", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Tiles_are_enlarged_to_the_sheet_by_default()
    {
        using PdfDocument source = Source(1);

        using PdfDocument tiles = source.CreateTiles(2, 2);

        Assert.Equal(4, tiles.Pages.Count);
        Assert.All(tiles.Pages, tile => Assert.Equal(Letter, tile.MediaBox));
    }

    [Fact]
    public void An_empty_range_makes_an_empty_document_and_bad_grids_throw()
    {
        using PdfDocument source = Source(2);

        using PdfDocument empty = source.CreateNUp(2, 2, pages: 1..1);

        Assert.Empty(empty.Pages);
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CreateNUp(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CreateTiles(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CreateNUp(2, 1, pages: 0..5));
        Assert.Throws<ArgumentException>(() => source.CreateNUp(2, 1, new PdfImpositionOptions().WithMargin(500)));
        Assert.Throws<ArgumentException>(() => source.CreateTiles(2, 1, new PdfImpositionOptions().WithOverlap(700)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfImpositionOptions().WithGutter(-1));
    }

    [Fact]
    public void The_new_document_saves_and_reopens_without_diagnostics_and_the_source_is_unchanged()
    {
        using PdfDocument source = Source(3);
        CosObject contents = source.Pages[0].Dictionary[KnownNames.Contents];

        using PdfDocument sheets = source.CreateNUp(2, 1, new PdfImpositionOptions().WithCellFrames());
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(sheets));

        Assert.Equal(2, reopened.Pages.Count);
        Assert.Contains("P1", reopened.Pages[0].ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(reopened.Diagnostics);
        Assert.Same(contents, source.Pages[0].Dictionary[KnownNames.Contents]);
        Assert.Equal(3, source.Pages.Count);
    }

    [Fact]
    public void Printable_annotations_are_drawn_in_by_default_and_left_out_on_request()
    {
        using PdfDocument source = Source(1);
        source.Pages[0].AddFreeTextAnnotation(new PdfRectangle(100, 100, 300, 140), "Margin note", new PdfAnnotationOptions { Flags = PdfAnnotationFlags.Print });

        using PdfDocument withNotes = source.CreateNUp(1, 1);
        using PdfDocument without = source.CreateNUp(1, 1, new PdfImpositionOptions().WithAnnotations(false));

        Assert.Contains("Margin", withNotes.Pages[0].ExtractText().Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin", without.Pages[0].ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(withNotes.Pages[0].Annotations);
    }

    /// <summary>A document of <paramref name="count"/> Letter pages, page <c>n</c> labeled <c>Pn</c> around its center.</summary>
    internal static PdfDocument Source(int count)
    {
        PdfDocument document = PdfDocument.Create();
        for (int index = 0; index < count; index++)
        {
            PdfPage page = index == 0 ? document.Pages[0] : document.Pages.Add(Letter);
            Label(document, page, $"P{index + 1}");
        }

        return document;
    }

    /// <summary>Writes <paramref name="text"/> in 24-point Helvetica centered on the page's media box.</summary>
    internal static void Label(PdfDocument document, PdfPage page, string text)
    {
        PdfRectangle box = page.MediaBox;
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.BeginText();
        canvas.SetFont(font, 24);
        double width = text.Length * 24 * 0.6;
        canvas.MoveText(((box.Left + box.Right) / 2) - (width / 2), ((box.Bottom + box.Top) / 2) - 8);
        canvas.ShowText(text);
        canvas.EndText();
    }

    private static TextWord Word(TextPage text, string word) =>
        text.Words.First(candidate => text.GetWordText(candidate).SequenceEqual(word));

    private static PathPoint Center(PdfPage page, string word)
    {
        PdfRectangle bounds = Word(page.ExtractText(), word).Quad.Bounds;
        return new PathPoint((bounds.Left + bounds.Right) / 2, (bounds.Bottom + bounds.Top) / 2);
    }

    /// <summary>Within 12 points: the label is centered by an estimate of its width.</summary>
    private static void AssertNear(PathPoint expected, PathPoint actual)
    {
        Assert.InRange(actual.X, expected.X - 12, expected.X + 12);
        Assert.InRange(actual.Y, expected.Y - 12, expected.Y + 12);
    }

    private static byte[] PageContent(PdfDocument document, PdfPage page) =>
        document.Resolve(page.Dictionary[KnownNames.Contents]) is CosStream stream ? document.DecodeStream(stream).ToArray() : [];
}
