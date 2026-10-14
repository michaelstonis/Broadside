using System.Text;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Tiling patterns through the software rasterizer (ISO 32000-2 §8.7.3), observed as pixels: the cell is replicated at XStep and
/// YStep in pattern space over the painted area only, overlapping where the steps are smaller than the bounding box, painted once
/// where they are huge, in the colour given with an uncoloured pattern, and composited with the paint's alpha. Pages are 612 x 792
/// at 72 DPI, so pixel (x, y) has its centre at user (x + 0.5, 791.5 − y).
/// </summary>
public sealed class TilingRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Red = new(255, 0, 0, 255);
    private static readonly Rgba Blue = new(0, 0, 255, 255);

    [Fact]
    public void A_coloured_pattern_repeats_its_cell_every_step_from_the_pattern_origin_within_the_fill()
    {
        // pattern-tiling-colored.pdf: the fill covers user 0..400 (2 0 0 2 0 0 cm, then 0 0 200 200 re); the pattern matrix puts
        // the cell origins at 10 + 25k (not scaled by the cm, §8.7.2); red at [0, 10]², blue at [10, 20]² of each cell.
        using Bitmap bitmap = RenderCorpus("pattern-tiling-colored.pdf");

        Assert.Equal(Red, bitmap.At(15, 776));
        Assert.Equal(Blue, bitmap.At(25, 766));
        Assert.Equal(White, bitmap.At(32, 776));
        Assert.Equal(Red, bitmap.At(40, 776));
        Assert.Equal(Red, bitmap.At(390, 401));

        // The last column's blue square (395..405) is cut by the fill at 400; the next cell (410) is outside it.
        Assert.Equal(Blue, bitmap.At(398, 766));
        Assert.Equal(White, bitmap.At(402, 766));
        Assert.Equal(White, bitmap.At(415, 776));
        Assert.Equal(White, bitmap.At(5, 776));
    }

    [Fact]
    public void An_uncoloured_pattern_paints_its_stencil_in_the_colour_given_with_it()
    {
        // pattern-tiling-uncolored.pdf: triangles (0,0) (12,0) (6,12) every 15 units in 0.8 0.2 0, over the fill 50..350. The
        // cell at (60, 60): its inside at (66.5, 63.5), outside it at (61.5, 70.5), the gap between cells at (73.5, 63.5).
        using Bitmap bitmap = RenderCorpus("pattern-tiling-uncolored.pdf");

        Assert.Equal(new Rgba(204, 51, 0, 255), bitmap.At(66, 728));
        Assert.Equal(White, bitmap.At(61, 721));
        Assert.Equal(White, bitmap.At(73, 728));
        Assert.Equal(White, bitmap.At(36, 728));
    }

    [Fact]
    public void A_pattern_selected_in_a_form_tiles_in_the_form_s_space()
    {
        // pattern-in-form.pdf: pattern space is [0.5 0 0 0.5 150 150], so 20-unit cells are 10 units apart from (150, 150) and
        // their green 10-unit squares are 5 units wide.
        using Bitmap bitmap = RenderCorpus("pattern-in-form.pdf");

        Assert.Equal(new Rgba(0, 128, 0, 255), bitmap.At(152, 639));
        Assert.Equal(White, bitmap.At(157, 639));
        Assert.Equal(new Rgba(0, 128, 0, 255), bitmap.At(162, 639));
        Assert.Equal(White, bitmap.At(147, 639));
    }

    [Fact]
    public void A_pattern_that_paints_with_itself_renders_promptly_and_records_its_recursion()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-recursive.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        Assert.Equal(White, bitmap.At(5, 786));
        Assert.Contains(document.Diagnostics, static d => d.Code == "ContentPatternRecursion");
    }

    [Fact]
    public void Steps_smaller_than_the_bounding_box_overlap_neighbouring_cells()
    {
        // BBox [0 0 20 20], steps 10: a square at [12, 16]² of each cell, outside its first step, shows up every 10 units (at 2
        // and 12 within 0..20); each cell filling its box at alpha 0.5 is covered by four cells everywhere: 255 × (1 − α)^4.
        using Bitmap squares = RenderContent(
            "/Pattern cs /P0 scn 0 0 100 100 re f",
            "/Pattern << /P0 5 0 R >>",
            Cell("0 0 0 rg 12 12 4 4 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 20 20] /XStep 10 /YStep 10"));
        Assert.Equal(new Rgba(0, 0, 0, 255), squares.At(3, 788));
        Assert.Equal(new Rgba(0, 0, 0, 255), squares.At(13, 788));
        Assert.Equal(new Rgba(0, 0, 0, 255), squares.At(53, 738));
        Assert.Equal(White, squares.At(7, 788));
        Assert.Equal(White, squares.At(9, 781));

        using Bitmap layered = RenderContent(
            "/Pattern cs /P0 scn 0 0 100 100 re f",
            "/Pattern << /P0 5 0 R >>",
            Cell("/G0 gs 0 0 0 rg 0 0 20 20 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 20 20] /XStep 10 /YStep 10", "/ExtGState << /G0 << /ca 0.5 >> >>"));

        // α = 128/255 per cell: 255 × (127/255)^4 = 15.75.
        Rgba grey = layered.At(50, 741);
        Assert.InRange(grey.R, 14, 18);
        Assert.Equal(grey.R, grey.G);
        Assert.Equal(grey.R, grey.B);
        Assert.Equal(grey, layered.At(5, 786));
    }

    [Fact]
    public void Steps_larger_than_the_painted_area_paint_one_cell()
    {
        // XStep and YStep 10000: over the whole page only the cell at the pattern origin (100, 100) shows, 20 x 20 pixels.
        using Bitmap bitmap = RenderContent(
            "/Pattern cs /P0 scn 0 0 612 792 re f",
            "/Pattern << /P0 5 0 R >>",
            Cell("1 0 0 rg 0 0 20 20 re f", "/PaintType 1 /TilingType 2 /BBox [0 0 20 20] /XStep 10000 /YStep 10000 /Matrix [1 0 0 1 100 100]"));

        Assert.Equal(400, CountPixels(bitmap, Red));
        Assert.Equal(Red, bitmap.At(100, 691));
        Assert.Equal(Red, bitmap.At(119, 672));
        Assert.Equal(White, bitmap.At(120, 672));
    }

    [Fact]
    public void Constant_spacing_rounds_the_steps_to_whole_pixels_so_abutting_cells_leave_no_seams()
    {
        // TilingType 1, BBox and steps 7.3: each cell is distorted to 7 pixels and placed on the grid, so a cell that fills its
        // box covers every pixel of the fill exactly once.
        using Bitmap bitmap = RenderContent(
            "/Pattern cs /P0 scn 0 0 100 100 re f",
            "/Pattern << /P0 5 0 R >>",
            Cell("0 0 1 rg 0 0 7.3 7.3 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 7.3 7.3] /XStep 7.3 /YStep 7.3 /Matrix [1 0 0 1 0.4 0.4]"));

        for (int y = 693; y < 792; y++)
        {
            for (int x = 1; x < 100; x++)
            {
                Assert.Equal(Blue, bitmap.At(x, y));
            }
        }

        Assert.Equal(White, bitmap.At(100, 750));
    }

    [Fact]
    public void A_rotated_pattern_space_turns_the_tiles()
    {
        // Matrix [0 1 -1 0 300 300]: pattern x runs up the page, pattern y to the left. A cell with red at [0, 10] x [0, 5] and the
        // steps 20: the red part of the origin cell covers user x 295..300, y 300..310.
        using Bitmap bitmap = RenderContent(
            "/Pattern cs /P0 scn 200 200 200 200 re f",
            "/Pattern << /P0 5 0 R >>",
            Cell("1 0 0 rg 0 0 10 5 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 20 20] /XStep 20 /YStep 20 /Matrix [0 1 -1 0 300 300]"));

        Assert.Equal(Red, bitmap.At(297, 486));
        Assert.Equal(White, bitmap.At(302, 486));
        Assert.Equal(White, bitmap.At(297, 476));
        Assert.Equal(Red, bitmap.At(297, 466));
        Assert.Equal(Red, bitmap.At(277, 486));
    }

    [Fact]
    public void A_tiled_fill_composites_with_the_alpha_of_its_paint()
    {
        // ca 0.5 over white: an opaque red cell gives (255, 127.5, 127.5); the cell itself starts at alpha 1 (§11.6.7).
        using Bitmap bitmap = RenderContent(
            "/G0 gs /Pattern cs /P0 scn 0 0 100 100 re f",
            "/ExtGState << /G0 << /ca 0.5 >> >> /Pattern << /P0 5 0 R >>",
            Cell("1 0 0 rg 0 0 10 10 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10"));

        Rgba pixel = bitmap.At(50, 741);
        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 127, 128);
        Assert.Equal(pixel.G, pixel.B);
    }

    [Fact]
    public void A_stroke_in_a_tiling_pattern_tiles_the_area_the_stroke_covers()
    {
        // A 20-unit wide horizontal stroke at y = 400 in a solid red pattern: red along it, white above and below.
        using Bitmap bitmap = RenderContent(
            "/Pattern CS /P0 SCN 20 w 100 400 m 500 400 l S",
            "/Pattern << /P0 5 0 R >>",
            Cell("1 0 0 rg 0 0 10 10 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10"));

        Assert.Equal(Red, bitmap.At(300, 391));
        Assert.Equal(Red, bitmap.At(300, 382));
        Assert.Equal(White, bitmap.At(300, 370));
        Assert.Equal(White, bitmap.At(50, 391));
    }

    [Fact]
    public void A_pattern_inside_a_cell_tiles_within_each_tile_of_the_outer_one()
    {
        // The outer cell (steps 40) fills [0, 20]² with an inner pattern of 4-unit red squares every 8 units.
        using Bitmap bitmap = RenderContent(
            "/Pattern cs /P0 scn 0 0 200 200 re f",
            "/Pattern << /P0 5 0 R >>",
            Cell("/Pattern cs /P1 scn 0 0 20 20 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 40 40] /XStep 40 /YStep 40", "/Pattern << /P1 6 0 R >>"),
            Cell("1 0 0 rg 0 0 4 4 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 8 8] /XStep 8 /YStep 8"));

        Assert.Equal(Red, bitmap.At(2, 789));
        Assert.Equal(White, bitmap.At(6, 789));
        Assert.Equal(Red, bitmap.At(10, 789));
        Assert.Equal(White, bitmap.At(26, 789));
        Assert.Equal(Red, bitmap.At(42, 789));
    }

    private static int CountPixels(Bitmap bitmap, Rgba color)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                count += bitmap.At(x, y) == color ? 1 : 0;
            }
        }

        return count;
    }

    private static string Cell(string content, string entries, string resources = "") =>
        ContentSamples.Stream(content, $"/Type /Pattern /PatternType 1 /Resources << {resources} >> {entries}");

    private static Bitmap RenderCorpus(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }

    private static Bitmap RenderContent(string content, string resources, params string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes(content), resources, objects));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }
}
