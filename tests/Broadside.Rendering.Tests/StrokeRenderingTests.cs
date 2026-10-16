using System.Text;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Strokes rendered end to end (ISO 32000-2 §8.5.3.2 with the line parameters of §8.4.3 and stroke adjustment of §10.7.5), observed
/// as pixels. Each content stream starts with <c>1 0 0 -1 0 792 cm</c>, so user coordinates are the bitmap's pixel coordinates at
/// 72 DPI; expected pixels come from the geometry the clauses define, worked out in the comments.
/// </summary>
public sealed class StrokeRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Black = new(0, 0, 0, 255);

    [Fact]
    public void A_butt_capped_line_covers_exactly_the_rectangle_along_it()
    {
        using Bitmap bitmap = Render("10 w 100 50 m 200 50 l S");

        // Rows 45..54, columns 100..199 (§8.4.3.3 style 0: squared off at the endpoints).
        Assert.Equal(Black, bitmap.At(100, 45));
        Assert.Equal(Black, bitmap.At(199, 54));
        Assert.Equal(White, bitmap.At(99, 50));
        Assert.Equal(White, bitmap.At(200, 50));
        Assert.Equal(White, bitmap.At(150, 44));
        Assert.Equal(White, bitmap.At(150, 55));
    }

    [Fact]
    public void A_projecting_square_cap_extends_the_line_by_half_its_width()
    {
        using Bitmap bitmap = Render("2 J 10 w 100 50 m 200 50 l S");

        Assert.Equal(Black, bitmap.At(95, 45));
        Assert.Equal(Black, bitmap.At(204, 54));
        Assert.Equal(White, bitmap.At(94, 50));
        Assert.Equal(White, bitmap.At(205, 50));
    }

    [Fact]
    public void A_round_cap_is_a_half_disc_around_the_endpoint()
    {
        using Bitmap bitmap = Render("1 J 10 w 100 50 m 200 50 l S");

        // Pixel (96, 50) reaches at most 4.1 from (100, 50): inside the radius 5, like (203, 50) around (200, 50); pixel (95, 45)
        // comes no nearer than 5.7: outside.
        Assert.Equal(Black, bitmap.At(96, 50));
        Assert.Equal(Black, bitmap.At(203, 50));
        Assert.Equal(White, bitmap.At(95, 45));
        Assert.Equal(White, bitmap.At(205, 45));
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, false, true)]
    [InlineData(2, false, false)]
    public void Joins_are_mitered_rounded_or_bevelled(int join, bool tipPainted, bool roundPartPainted)
    {
        // Right, then down, 20 wide, around the corner (200, 100): the outer corner is (210, 90). Pixel (208, 92) lies beyond the
        // bevel line x - y = 110 and 11.3 from the corner (outside a round join); pixel (206, 94) is 8.5 from the corner (inside a
        // round join) and still beyond the bevel line.
        using Bitmap bitmap = Render($"{join} j 20 w 100 100 m 200 100 l 200 200 l S");

        Assert.Equal(tipPainted ? Black : White, bitmap.At(208, 92));
        Assert.Equal(roundPartPainted ? Black : White, bitmap.At(206, 94));
        Assert.Equal(Black, bitmap.At(200, 100));
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(3.5, false)]
    public void A_miter_longer_than_the_miter_limit_becomes_a_bevel(double limit, bool mitered)
    {
        // A 30 degree corner at (300, 100) between segments from (100, 100 - 200 tan 15°) and (100, 100 + 200 tan 15°): the miter
        // ratio is 1 / sin 15° = 3.86 (§8.4.3.5), so a limit of 4 keeps the miter and 3.5 cuts it. The miter tip lies 3.86 × 5 =
        // 19.3 beyond the corner on the axis; pixel (310, 99) is 10.5 beyond it, past a bevel (1.3 beyond).
        using Bitmap bitmap = Render($"{limit} M 10 w 100 46.41 m 300 100 l 100 153.59 l S");

        Assert.Equal(mitered ? Black : White, bitmap.At(310, 99));
        Assert.Equal(Black, bitmap.At(299, 99));
    }

    [Fact]
    public void Dashes_start_at_the_phase_and_cycle_through_the_array()
    {
        // [20 10] 5: 15 on (100..115), 10 off, 20 on (125..145), 10 off, 20 on (155..175), 10 off, 15 on (185..200).
        using Bitmap bitmap = Render("[20 10] 5 d 10 w 100 50 m 200 50 l S");

        Assert.Equal(Black, bitmap.At(100, 50));
        Assert.Equal(Black, bitmap.At(114, 50));
        Assert.Equal(White, bitmap.At(115, 50));
        Assert.Equal(White, bitmap.At(124, 50));
        Assert.Equal(Black, bitmap.At(125, 50));
        Assert.Equal(Black, bitmap.At(144, 50));
        Assert.Equal(White, bitmap.At(150, 50));
        Assert.Equal(Black, bitmap.At(160, 50));
        Assert.Equal(White, bitmap.At(180, 50));
        Assert.Equal(Black, bitmap.At(199, 50));
    }

    [Fact]
    public void Each_subpath_restarts_the_dash_pattern()
    {
        // Both subpaths start with a 10 on dash under [10 10] 0.
        using Bitmap bitmap = Render("[10 10] 0 d 10 w 100 50 m 135 50 l 100 80 m 135 80 l S");

        Assert.Equal(Black, bitmap.At(105, 50));
        Assert.Equal(White, bitmap.At(115, 50));
        Assert.Equal(Black, bitmap.At(125, 50));
        Assert.Equal(Black, bitmap.At(105, 80));
        Assert.Equal(White, bitmap.At(115, 80));
    }

    [Fact]
    public void Zero_length_dashes_get_caps_oriented_along_the_path()
    {
        // [0 20] with projecting square caps: 10 x 10 squares centred every 20 units along the line; with round caps, discs; with
        // butt caps, nothing (§8.5.3.2: zero-length dashes always paint their caps).
        using Bitmap square = Render("2 J [0 20] 0 d 10 w 100 50 m 200 50 l S");
        using Bitmap round = Render("1 J [0 20] 0 d 10 w 100 50 m 200 50 l S");
        using Bitmap butt = Render("0 J [0 20] 0 d 10 w 100 50 m 200 50 l S");

        Assert.Equal(Black, square.At(124, 54));
        Assert.Equal(White, square.At(130, 50));
        Assert.Equal(Black, round.At(120, 50));
        Assert.Equal(White, round.At(124, 54));
        Assert.True(IsBlank(butt));
    }

    [Theory]
    [InlineData("100 100 m 100 100 l")]
    [InlineData("100 100 m h")]
    [InlineData("100 100 m 100 100 100 100 100 100 c")]
    public void A_degenerate_subpath_paints_a_dot_only_with_round_caps(string subpath)
    {
        using Bitmap round = Render($"1 J 20 w {subpath} S");
        using Bitmap butt = Render($"0 J 20 w {subpath} S");
        using Bitmap square = Render($"2 J 20 w {subpath} S");

        Assert.Equal(Black, round.At(100, 100));
        Assert.Equal(Black, round.At(106, 106));
        Assert.Equal(White, round.At(108, 108));
        Assert.True(IsBlank(butt));
        Assert.True(IsBlank(square));
    }

    [Fact]
    public void A_lone_trailing_moveto_paints_nothing()
    {
        using Bitmap bitmap = Render("1 J 20 w 100 100 m 200 100 l 300 300 m S 400 400 m S");

        Assert.Equal(Black, bitmap.At(150, 100));
        Assert.Equal(White, bitmap.At(300, 300));
        Assert.Equal(White, bitmap.At(400, 400));
    }

    [Fact]
    public void A_closed_subpath_is_joined_where_it_closes_and_an_explicit_line_back_is_capped()
    {
        // re closes with h: a miter at (100, 100) paints its outer corner (95, 95). The same square drawn back to its start with l
        // has butt caps there instead (§8.5.3.2: a messy corner).
        using Bitmap closed = Render("10 w 100 100 100 100 re S");
        using Bitmap open = Render("10 w 100 100 m 200 100 l 200 200 l 100 200 l 100 100 l S");

        Assert.Equal(Black, closed.At(96, 96));
        Assert.Equal(White, open.At(96, 96));
        Assert.Equal(Black, open.At(96, 101));
        Assert.Equal(Black, open.At(196, 96));
    }

    [Fact]
    public void Dashes_of_a_closed_subpath_join_across_its_start_when_it_starts_and_ends_on()
    {
        // Perimeter 400, [30 10] 5: the last dash runs from 395 to 400 and continues as the first (0..25), so the corner (100, 100)
        // is a miter join of one dash rather than two butt caps; 25..35 is a gap.
        using Bitmap bitmap = Render("[30 10] 5 d 10 w 100 100 100 100 re S");

        Assert.Equal(Black, bitmap.At(96, 96));
        Assert.Equal(Black, bitmap.At(120, 100));
        Assert.Equal(White, bitmap.At(130, 100));
    }

    [Fact]
    public void A_zero_width_line_is_one_device_pixel_wide()
    {
        // §8.4.3.2: the thinnest line the device can render. The horizontal one falls on row 100, the vertical one on column 300.
        using Bitmap bitmap = Render("0 w 100 100.3 m 200 100.3 l S 300.6 150 m 300.6 250 l S");

        Assert.Equal(Black, bitmap.At(150, 100));
        Assert.Equal(White, bitmap.At(150, 99));
        Assert.Equal(White, bitmap.At(150, 101));
        Assert.Equal(Black, bitmap.At(300, 200));
        Assert.Equal(White, bitmap.At(299, 200));
        Assert.Equal(White, bitmap.At(301, 200));
    }

    [Fact]
    public void Stroke_adjustment_makes_a_one_unit_rule_on_a_pixel_boundary_one_crisp_pixel_row()
    {
        // A 1 wide rule centred on y = 100 covers half of rows 99 and 100; with SA true it is moved to cover one row whole (§10.7.5).
        using Bitmap plain = Render("1 w 100 100 m 200 100 l S");
        using Bitmap adjusted = Render("/SA gs 1 w 100 100 m 200 100 l S", "/ExtGState << /SA << /SA true >> >>");

        Assert.Equal(new Rgba(127, 127, 127, 255), plain.At(150, 99));
        Assert.Equal(new Rgba(127, 127, 127, 255), plain.At(150, 100));
        int[] rows = [.. Enumerable.Range(95, 10).Where(y => adjusted.At(150, y) != White)];
        Assert.Single(rows);
        Assert.Equal(Black, adjusted.At(150, rows[0]));
    }

    [Fact]
    public void Stroke_adjustment_rounds_the_width_to_whole_pixels()
    {
        // 2.4 wide becomes 2 pixels, 2.6 wide becomes 3: no more than half a pixel off (§10.7.5).
        using Bitmap narrow = Render("/SA gs 2.4 w 100 100.2 m 200 100.2 l S", "/ExtGState << /SA << /SA true >> >>");
        using Bitmap wide = Render("/SA gs 2.6 w 100 100.2 m 200 100.2 l S", "/ExtGState << /SA << /SA true >> >>");

        Assert.Equal(2, Enumerable.Range(90, 20).Count(y => narrow.At(150, y) == Black));
        Assert.Equal(0, Enumerable.Range(90, 20).Count(y => narrow.At(150, y) != Black && narrow.At(150, y) != White));
        Assert.Equal(3, Enumerable.Range(90, 20).Count(y => wide.At(150, y) == Black));
    }

    [Fact]
    public void A_non_uniform_transform_strokes_with_an_elliptical_pen()
    {
        // Under 4 0 0 1 the 2 wide pen is 8 pixels across horizontally and 2 vertically: the horizontal segment (y = 100) is 2 rows
        // thick, the vertical one (x = 50 × 4 = 200) 8 columns thick (§8.4.3.2).
        using Bitmap bitmap = Render("q 4 0 0 1 0 0 cm 2 w 25 100 m 50 100 l 50 200 l S Q");

        Assert.Equal(Black, bitmap.At(150, 99));
        Assert.Equal(Black, bitmap.At(150, 100));
        Assert.Equal(White, bitmap.At(150, 98));
        Assert.Equal(White, bitmap.At(150, 101));
        Assert.Equal(Black, bitmap.At(196, 150));
        Assert.Equal(Black, bitmap.At(203, 150));
        Assert.Equal(White, bitmap.At(195, 150));
        Assert.Equal(White, bitmap.At(204, 150));
    }

    [Fact]
    public void Overlapping_parts_of_one_stroke_paint_once_and_curves_are_stroked_smoothly()
    {
        // A self-crossing figure eight and a wide arc: no hole where the stroke overlaps itself, and the outline of the arc follows
        // the curve (a pixel on its centre line, between flattening vertices, is covered).
        using Bitmap bitmap = Render("0 0 1 RG 16 w 100 100 m 200 200 l 200 100 l 100 200 l h S 300 300 m 300 400 400 400 400 300 c S");

        Assert.Equal(new Rgba(0, 0, 255, 255), bitmap.At(150, 150));
        Assert.Equal(new Rgba(0, 0, 255, 255), bitmap.At(350, 375));
        Assert.Equal(White, bitmap.At(350, 350));
    }

    [Theory]
    [InlineData("2 w 100 50.5 m 200 50.5 l 150 50.5 m 250 50.5 l S", false, 175, 49, 120, 49)]
    [InlineData("2 w 100 50.5 m 200 50.5 l 150 50.5 m 250 50.5 l S", false, 175, 51, 120, 51)]
    [InlineData("3 w 50.75 100 m 50.75 200 l 50.75 150 m 50.75 250 l S", false, 49, 175, 49, 120)]
    [InlineData("3 w 50.75 100 m 50.75 200 l 50.75 150 m 50.75 250 l S", false, 52, 175, 52, 120)]
    [InlineData("2 J 2 w 100 50.5 m 200 50.5 l S", true, 103, 49, 101, 49)]
    [InlineData("2 J 2 w 50.25 100 m 50.25 200 l S", true, 49, 103, 49, 101)]
    public void Overlapping_pieces_of_one_stroke_do_not_darken_its_anti_aliased_edges(string content, bool dashed, int x, int y, int referenceX, int referenceY)
    {
        // A stroke paints one shape (ISO 32000-2 §8.5.3.1): where two pieces overlap (two subpaths along the same line, or the square
        // caps of neighbouring dashes 3 on, 1 off), an edge pixel half covered by both is still half covered, as it is where only one
        // piece reaches.
        content = (dashed ? "[3 1] 0 d " : string.Empty) + content;
        using Bitmap bitmap = Render(content);

        Rgba overlap = bitmap.At(x, y);
        Rgba reference = bitmap.At(referenceX, referenceY);
        Assert.NotEqual(White, reference);
        Assert.NotEqual(Black, reference);
        Assert.InRange(overlap.R, reference.R - 1, reference.R + 1);
    }

    [Theory]
    [InlineData("1000000000 w 1 J 1 j 0 0 m 1 1 l 2 0 l S", true)]
    [InlineData("[0.0001 0.0001] 0 d 2 w 0 300 m 100000 300 l S", true)]
    [InlineData("1 j 1 J 5 w 0 0 m 1000000000000000000000000000000 1 l -1000000000000000000000000000000 2 l S", true)]
    [InlineData("0 w 100000000000000000000 0 0 100000000000000000000 0 0 cm 0 0 m 1 1 l S", true)]
    [InlineData("10 w 0 0 0 0 0 0 cm 0 0 m 100 100 l S", false)]
    public void Extreme_widths_dash_patterns_coordinates_and_transforms_render_without_failing(string content, bool paints)
    {
        // A stroke dispenses at most 2^20 dashes, then draws the rest solid; coordinates and widths are bounded before scan conversion.
        using Bitmap bitmap = Render(content);

        Assert.Equal(paints, !IsBlank(bitmap));
    }

    private static bool IsBlank(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            if (bitmap.GetRow(y).IndexOfAnyExcept((byte)0xFF) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static Bitmap Render(string content, string resources = "")
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes("1 0 0 -1 0 792 cm " + content), resources));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }
}
