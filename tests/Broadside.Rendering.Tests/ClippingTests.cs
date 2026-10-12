using System.Text;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Clipping through the software rasterizer (ISO 32000-2 §8.5.4, §8.10.1), observed as pixels: paints after <c>W</c> or <c>W*</c> are
/// cut to the intersection of the clips in force, <c>Q</c> brings the earlier clip back, a form is cut to its bounding box, and clip
/// edges are anti-aliased like fill edges (§10.7.4). Pages are 612 x 792 at 72 DPI, so pixel (x, y) is user (x, 792 - y).
/// </summary>
public sealed class ClippingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Black = new(0, 0, 0, 255);

    [Fact]
    public void A_clip_set_with_w_n_cuts_the_paints_that_follow_to_its_inside()
    {
        using Bitmap bitmap = RenderContent("100 100 200 200 re W n 0 0 612 792 re f");

        Assert.Equal(Black, bitmap.At(101, 792 - 101));
        Assert.Equal(Black, bitmap.At(298, 792 - 299));
        Assert.Equal(White, bitmap.At(99, 600));
        Assert.Equal(White, bitmap.At(301, 600));
        Assert.Equal(White, bitmap.At(200, 792 - 301));
        Assert.Equal(White, bitmap.At(200, 792 - 99));
    }

    [Fact]
    public void A_clip_applies_to_later_paints_only_and_not_to_the_paint_of_its_own_path()
    {
        // Two counterclockwise squares, 100..300 and 150..250: f fills both (non-zero, winding 2 in the centre) in red, while W*
        // clips to the ring between them (even-odd), so the later blue page fill reaches the ring only.
        using Bitmap bitmap = RenderContent("1 0 0 rg 100 100 200 200 re 150 150 100 100 re W* f 0 0 1 rg 0 0 612 792 re f");

        Assert.Equal(new Rgba(255, 0, 0, 255), bitmap.At(200, 792 - 200));
        Assert.Equal(new Rgba(0, 0, 255, 255), bitmap.At(120, 792 - 120));
        Assert.Equal(White, bitmap.At(50, 792 - 50));
    }

    [Fact]
    public void Strokes_are_clipped_like_fills_so_a_stroke_outside_the_clip_paints_nothing()
    {
        // The clip is the square 100..300. A 20 wide stroke along x = 400 lies wholly outside it; one along x = 300 is cut to its
        // inner half; one along x = 200 is whole. A path both stroked and clipped (W S) is not clipped by its own path.
        using Bitmap bitmap = RenderContent(
            "100 100 200 200 re W n 20 w 400 50 m 400 350 l S 300 50 m 300 350 l S 200 50 m 200 350 l S");
        using Bitmap own = RenderContent("20 w 100 100 200 200 re W S");

        for (int y = 0; y < 792; y += 7)
        {
            for (int x = 380; x < 420; x++)
            {
                Assert.Equal(White, bitmap.At(x, y));
            }
        }

        Assert.Equal(Black, bitmap.At(295, 792 - 200));
        Assert.Equal(White, bitmap.At(305, 792 - 200));
        Assert.Equal(White, bitmap.At(300, 792 - 320));
        Assert.Equal(Black, bitmap.At(200, 792 - 200));
        Assert.Equal(White, bitmap.At(200, 792 - 90));
        Assert.Equal(Black, own.At(305, 792 - 200));
        Assert.Equal(Black, own.At(95, 792 - 200));
    }

    [Fact]
    public void The_non_zero_rule_keeps_the_centre_of_nested_squares_inside_the_clip_and_the_even_odd_rule_leaves_it_out()
    {
        using Bitmap nonZero = RenderContent("100 100 200 200 re 150 150 100 100 re W n 0 0 612 792 re f");
        using Bitmap evenOdd = RenderContent("100 100 200 200 re 150 150 100 100 re W* n 0 0 612 792 re f");

        Assert.Equal(Black, nonZero.At(200, 792 - 200));
        Assert.Equal(Black, nonZero.At(120, 792 - 120));
        Assert.Equal(White, evenOdd.At(200, 792 - 200));
        Assert.Equal(Black, evenOdd.At(120, 792 - 120));
        Assert.Equal(White, evenOdd.At(50, 792 - 50));
    }

    [Fact]
    public void Nested_clips_intersect()
    {
        // A triangle (100,100) (300,100) (200,300), then the stripe x 150..250: only their intersection is painted. At y = 280 the
        // triangle spans x 190..210.
        using Bitmap bitmap = RenderContent("100 100 m 300 100 l 200 300 l h W n 150 0 100 792 re W n 0 0 612 792 re f");

        // The same triangle, then a second one, (0,0) (200,0) (200,400): x <= 200 and y <= 2x.
        using Bitmap paths = RenderContent("100 100 m 300 100 l 200 300 l h W n 0 0 m 200 0 l 200 400 l h W n 0 0 612 792 re f");

        Assert.Equal(Black, bitmap.At(200, 792 - 150));
        Assert.Equal(White, bitmap.At(120, 792 - 110));
        Assert.Equal(White, bitmap.At(240, 792 - 280));
        Assert.Equal(White, bitmap.At(200, 792 - 50));
        Assert.Equal(Black, paths.At(150, 792 - 120));
        Assert.Equal(White, paths.At(250, 792 - 120));
        Assert.Equal(White, paths.At(110, 792 - 260));
    }

    [Fact]
    public void Clips_that_do_not_overlap_leave_nothing_to_paint()
    {
        using Bitmap rectangles = RenderContent("0 0 10 10 re W n 100 100 10 10 re W n 0 0 612 792 re f");
        using Bitmap paths = RenderContent("0 0 m 50 0 l 0 50 l h W n 100 100 m 150 100 l 100 150 l h W n 0 0 612 792 re f");

        AssertWhite(rectangles);
        AssertWhite(paths);
    }

    [Fact]
    public void Q_restores_the_clip_saved_by_q()
    {
        using Bitmap rectangle = RenderContent("q 100 100 50 50 re W n Q 0 0 612 792 re f");
        using Bitmap path = RenderContent("q 100 100 m 200 100 l 150 200 l h W n Q 0 0 612 792 re f");

        Assert.Equal(Black, rectangle.At(10, 10));
        Assert.Equal(Black, path.At(10, 10));
        Assert.Equal(Black, path.At(600, 780));
    }

    [Fact]
    public void A_clip_narrowed_inside_q_does_not_change_the_clip_saved_by_q()
    {
        // The triangle clip is saved; inside, it is narrowed to the left half of a second triangle and painted black; after Q the
        // whole triangle is the clip again and is painted red.
        using Bitmap bitmap = RenderContent(
            "100 100 m 300 100 l 200 300 l h W n q 0 0 m 200 0 l 200 400 l h W n 0 0 612 792 re f Q 1 0 0 rg 0 0 612 792 re f");

        Assert.Equal(new Rgba(255, 0, 0, 255), bitmap.At(150, 792 - 120));
        Assert.Equal(new Rgba(255, 0, 0, 255), bitmap.At(250, 792 - 120));
        Assert.Equal(White, bitmap.At(50, 792 - 50));
    }

    [Fact]
    public void Clip_edges_are_anti_aliased_like_fill_edges()
    {
        // A clip from x = 10.5 to 20.5: pixels 10 and 20 are half inside (§10.7.4). Once as one rectangle (no mask), once with a
        // second rectangle in the same path (a coverage mask).
        using Bitmap rectangle = RenderContent("10.5 0 10 792 re W n 0 0 612 792 re f");
        using Bitmap mask = RenderContent("10.5 0 10 792 re 300 0 10 792 re W n 0 0 612 792 re f");

        foreach (Bitmap bitmap in new[] { rectangle, mask })
        {
            Assert.Equal(White, bitmap.At(9, 400));
            Assert.InRange(bitmap.At(10, 400).R, 126, 129);
            Assert.Equal(Black, bitmap.At(15, 400));
            Assert.InRange(bitmap.At(20, 400).R, 126, 129);
            Assert.Equal(White, bitmap.At(21, 400));
        }

        Assert.Equal(Black, mask.At(305, 400));
    }

    [Fact]
    public void A_rectangle_clip_under_a_quarter_turn_or_an_oblique_transform_clips_to_its_mapped_outline()
    {
        // [0 1 -1 0 400 100] maps the rectangle 0 0 200 100 to user x 300..400, y 100..300. The 45 degree rotation about
        // (306, 396) turns the square -50..50 into a diamond, whose corners at the bounding box stay outside.
        using Bitmap quarter = RenderContent("0 1 -1 0 400 100 cm 0 0 200 100 re W n -1000 -1000 3000 3000 re f");
        using Bitmap oblique = RenderContent("0.7071 0.7071 -0.7071 0.7071 306 396 cm -50 -50 100 100 re W n -1000 -1000 3000 3000 re f");

        Assert.Equal(Black, quarter.At(350, 792 - 200));
        Assert.Equal(White, quarter.At(290, 792 - 200));
        Assert.Equal(White, quarter.At(350, 792 - 310));
        Assert.Equal(Black, oblique.At(306, 396));
        Assert.Equal(Black, oblique.At(306 + 65, 396));
        Assert.Equal(White, oblique.At(306 + 65, 396 - 65));
    }

    [Fact]
    public void A_form_is_clipped_to_its_bounding_box()
    {
        using Bitmap bitmap = RenderContent(
            "/Fm Do",
            "/XObject << /Fm 5 0 R >>",
            ContentSamples.Stream("0 0 612 792 re f", "/Type /XObject /Subtype /Form /BBox [100 100 200 200]"));

        Assert.Equal(Black, bitmap.At(150, 792 - 150));
        Assert.Equal(White, bitmap.At(90, 792 - 150));
        Assert.Equal(White, bitmap.At(150, 792 - 210));
    }

    [Fact]
    public async Task Clipping_paths_pdf_matches_its_reviewed_snapshot()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("clipping-paths.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);
        using MemoryStream png = PngSnapshots.Encode(bitmap);
        await Verify(png, "png");
    }

    private static void AssertWhite(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            Assert.True(bitmap.GetRow(y).IndexOfAnyExcept((byte)0xFF) < 0, $"row {y} is not white");
        }
    }

    private static Bitmap RenderContent(string content, string resources = "", params string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes(content), resources, objects));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }
}
