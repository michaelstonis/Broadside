using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// The hit-test index a display list yields (viewer capability 4): glyph quads with their Unicode text in content order, image and
/// path bounds and link regions, queried by point and by rectangle in page space (default user space, ISO 32000-2 §8.3.2.3).
/// </summary>
public sealed class HitTestIndexTests
{
    private const double Tolerance = 1e-3;

    [Fact]
    public void A_point_inside_the_second_glyph_of_text_truetype_embedded_returns_that_glyph_with_its_unicode_and_quad()
    {
        // "HI" at 24 pt from (72, 700): H advances 800/1000 em (19.2), I 400/1000 em (9.6); the descriptor's Descent -200 and
        // Ascent 800 put the box from 695.2 to 719.2.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-truetype-embedded.pdf"));
        HitTestIndex index = document.Pages[0].CreateDisplayList().CreateHitTestIndex();

        HitTestItem hit = Assert.Single(Matches(index.Find(new PathPoint(96, 705), HitTestKinds.Glyphs)));

        Assert.Equal(new HitTestItem(HitTestKinds.Glyphs, 1), hit);
        Assert.Equal(2, index.GlyphCount);
        HitTestGlyph glyph = index.GetGlyph(hit.Index);
        Assert.Equal("I", index.GetGlyphText(glyph).ToString());
        Assert.Equal((uint)'I', glyph.CharacterCode);
        Assert.Equal(1, glyph.CodeLength);
        Assert.Equal(TextRenderingMode.Fill, glyph.RenderingMode);
        Assert.False(glyph.IsVertical);
        AssertQuad(new Quad(new(91.2, 695.2), new(100.8, 695.2), new(100.8, 719.2), new(91.2, 719.2)), glyph.Quad);
        Assert.Equal("HI", index.Text.ToString());
    }

    [Fact]
    public void Paths_inside_a_transparency_group_are_indexed_where_they_land()
    {
        // A form XObject with a transparency group (§11.6.6, #114), drawn at half opacity under a translation: the backend gets
        // one DrawGroup call, and the collector replays its content.
        byte[] file = ContentSamples.OnePage(
            "q /Half gs 1 0 0 1 200 300 cm /G Do Q"u8.ToArray(),
            "/ExtGState << /Half << /ca 0.5 >> >> /XObject << /G 5 0 R >>",
            ContentSamples.Stream("0 0 1 rg 0 0 50 50 re f", "/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Group << /S /Transparency /I true >>"));
        using PdfDocument document = PdfDocument.Open(file);
        DisplayList list = document.Pages[0].CreateDisplayList();
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        Assert.Single(recorder.Groups);

        HitTestIndex index = list.CreateHitTestIndex();
        Assert.Single(Matches(index.Find(new PathPoint(225, 325), HitTestKinds.Paths)));
        Assert.Empty(Matches(index.Find(new PathPoint(25, 25), HitTestKinds.Paths)));
    }

    [Fact]
    public void Rotated_text_returns_rotated_quads_not_upright_boxes()
    {
        // Line 1: Tm [0.866025 0.5 -0.5 0.866025 100 500] (30 degrees); line 2: cm [0 1 -1 0 300 300] (90 degrees), text at the origin.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-rotated.pdf"));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        Assert.Equal(4, index.GlyphCount);
        Assert.Equal("HIHI", index.Text.ToString());

        // The I of line 1 starts 19.2 along the 30-degree baseline; its box runs 9.6 along it and from -4.8 to 19.2 across it.
        HitTestItem tilted = Assert.Single(Matches(index.Find(new PathPoint(117.1846, 518.2354), HitTestKinds.Glyphs)));
        Assert.Equal(1, tilted.Index);
        AssertQuad(
            new Quad(new(119.02768, 505.44308), new(127.34152, 510.24308), new(115.34152, 531.02768), new(107.02768, 526.22768)),
            index.GetGlyph(1).Quad);

        // (127, 506) is inside the upright box around that I but outside the rotated one, and outside the H.
        Assert.Empty(Matches(index.Find(new PathPoint(127, 506), HitTestKinds.Glyphs)));

        // The I of line 2 runs up the page: text-space x maps to page y, text-space y to page -x.
        HitTestItem upright = Assert.Single(Matches(index.Find(new PathPoint(292, 324), HitTestKinds.Glyphs)));
        Assert.Equal(3, upright.Index);
        AssertQuad(new Quad(new(304.8, 319.2), new(304.8, 328.8), new(280.8, 328.8), new(280.8, 319.2)), index.GetGlyph(3).Quad);
    }

    [Fact]
    public void A_rectangle_query_returns_the_glyphs_it_touches_in_content_order_and_tests_rotated_boxes_exactly()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-rotated.pdf"));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        Assert.Equal([0, 1, 2, 3], Indexes(index.Find(new PdfRectangle(0, 0, 612, 792), HitTestKinds.Glyphs)));
        Assert.Equal([2, 3], Indexes(index.Find(new PdfRectangle(282, 305, 302, 325), HitTestKinds.Glyphs)));

        // Inside the upright box of line 1's I near its lower right corner, but clear of the rotated box.
        Assert.Empty(Matches(index.Find(new PdfRectangle(126.5, 505.5, 127.2, 506.5), HitTestKinds.Glyphs)));
        Assert.Equal([1], Indexes(index.Find(new PdfRectangle(117, 518, 117.5, 518.5), HitTestKinds.Glyphs)));
    }

    [Fact]
    public void A_link_annotation_is_returned_with_its_action_for_a_point_inside_its_quad_points()
    {
        // Rect [72 700 300 724]; QuadPoints: the upper strip 72-300 x 712-724 and the lower 72-150 x 700-712.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-link.pdf"));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        HitTestLink link = Assert.Single(index.Links);
        Assert.Same(document.Pages[0].Annotations[0], link.Annotation);
        Assert.Equal(new PdfRectangle(72, 700, 300, 724), link.Bounds);
        Assert.Equal(2, link.Region.Count);

        Assert.Equal(new HitTestItem(HitTestKinds.Links, 0), Assert.Single(Matches(index.Find(new PathPoint(100, 705), HitTestKinds.All))));
        Assert.Equal(new HitTestItem(HitTestKinds.Links, 0), Assert.Single(Matches(index.Find(new PathPoint(250, 718), HitTestKinds.Links))));
        PdfUriAction action = Assert.IsType<PdfUriAction>(index.Links[0].Action);
        Assert.Equal("https://example.com/", action.Uri);
        Assert.Null(index.Links[0].Destination);

        // Inside Rect but outside both quadrilaterals.
        Assert.Empty(Matches(index.Find(new PathPoint(250, 705), HitTestKinds.All)));
        Assert.Empty(Matches(index.Find(new PathPoint(100, 705), HitTestKinds.Glyphs)));
        Assert.Single(Matches(index.Find(new PdfRectangle(200, 702, 260, 714), HitTestKinds.Links)));
        Assert.Empty(Matches(index.Find(new PdfRectangle(200, 702, 260, 708), HitTestKinds.Links)));

        // The content's own index holds no links: they are the page's.
        Assert.Empty(document.Pages[0].CreateDisplayList().CreateHitTestIndex().Links);
    }

    [Theory]
    [InlineData("/F 2", 0)]
    [InlineData("/F 32", 0)]
    [InlineData("/F 4", 1)]
    [InlineData("/OC 5 0 R", 0)]
    public void Hidden_and_no_view_links_and_links_whose_optional_content_is_off_are_left_out(string entries, int expected)
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R] /D << /OFF [5 0 R] >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [4 0 R] >>",
            $"<< /Type /Annot /Subtype /Link /Rect [10 10 50 50] {entries} /A << /S /URI /URI (https://example.com/) >> >>",
            "<< /Type /OCG /Name (Off) >>");
        using PdfDocument document = PdfDocument.Open(file);

        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        Assert.Equal(expected, index.Links.Count);
        Assert.Equal(expected, Matches(index.Find(new PathPoint(30, 30), HitTestKinds.Links)).Count);
    }

    [Fact]
    public void A_link_whose_quad_points_leave_its_rectangle_is_hit_over_the_rectangle()
    {
        byte[] file = TestPdf.OnePage(
            "/MediaBox [0 0 612 792] /Annots [<< /Type /Annot /Subtype /Link /Rect [10 10 50 50] /QuadPoints [0 0 60 0 60 20 0 20] "
            + "/Dest [3 0 R /Fit] >>]");
        using PdfDocument document = PdfDocument.Open(file);

        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        Assert.Single(Matches(index.Find(new PathPoint(30, 40), HitTestKinds.Links)));
        Assert.Empty(Matches(index.Find(new PathPoint(5, 5), HitTestKinds.Links)));
        Assert.NotNull(index.Links[0].Destination);
    }

    [Fact]
    public void Images_are_indexed_by_the_unit_square_under_their_transform()
    {
        // A 2x2 inline image scaled to 100x100 at (72, 600).
        using PdfDocument document = PdfDocument.Open(Corpus.Path("inline-image.pdf"));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        HitTestShape image = Assert.Single(index.Images.ToArray());
        AssertQuad(new Quad(new(72, 600), new(172, 600), new(172, 700), new(72, 700)), image.Quad);
        Assert.Equal(new PdfRectangle(72, 600, 172, 700), image.Bounds);
        Assert.Equal(new HitTestItem(HitTestKinds.Images, 0), Assert.Single(Matches(index.Find(new PathPoint(100, 650), HitTestKinds.All))));
        Assert.Empty(Matches(index.Find(new PathPoint(50, 650), HitTestKinds.All)));
    }

    [Fact]
    public void Paths_are_indexed_by_their_bounds_under_their_transform_widened_for_strokes_and_cut_to_the_clip()
    {
        byte[] content = System.Text.Encoding.ASCII.GetBytes(
            "q 0 1 -1 0 300 100 cm 0 0 50 20 re f Q "
            + "4 w 1 j 1 J 400 400 m 500 400 l S "
            + "q 0 0 100 100 re W n 50 50 200 200 re f Q "
            + "q 0 0 10 10 re W n 500 500 20 20 re f Q");
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(content));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        HitTestShape[] paths = index.Paths.ToArray();
        Assert.Equal(3, paths.Length);

        // The rectangle 50 x 20 rotated 90 degrees about (300, 100): x from 280 to 300, y from 100 to 150.
        AssertQuad(new Quad(new(300, 100), new(300, 150), new(280, 150), new(280, 100)), paths[0].Quad);
        Assert.Equal(new PdfRectangle(280, 100, 300, 150), paths[0].Bounds);

        // A 4-unit round-joined stroke reaches 2 x √2 past its points.
        double grow = 2 * Math.Sqrt(2);
        Assert.Equal(new PdfRectangle(400 - grow, 400 - grow, 500 + grow, 400 + grow), paths[1].Bounds);
        Assert.Equal(new HitTestItem(HitTestKinds.Paths, 1), Assert.Single(Matches(index.Find(new PathPoint(450, 401), HitTestKinds.All))));

        // Cut to the clip; the fill the clip removes entirely is not indexed.
        Assert.Equal(new PdfRectangle(50, 50, 100, 100), paths[2].Bounds);
        Assert.Empty(Matches(index.Find(new PathPoint(150, 150), HitTestKinds.All)));
        Assert.Single(Matches(index.Find(new PathPoint(75, 75), HitTestKinds.Paths)));
    }

    [Fact]
    public void A_tiling_pattern_paint_is_indexed_by_its_shape_and_its_cell_contents_are_not()
    {
        // The page fills 0 0 200 200 under 2 0 0 2 0 0 cm with a tiling pattern whose cell paints two squares (issue #112).
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-tiling-colored.pdf"));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        HitTestShape path = Assert.Single(index.Paths.ToArray());
        Assert.Equal(new PdfRectangle(0, 0, 400, 400), path.Bounds);
        Assert.Equal(new HitTestItem(HitTestKinds.Paths, 0), Assert.Single(Matches(index.Find(new PathPoint(399, 1), HitTestKinds.All))));
    }

    [Fact]
    public void Invisible_text_is_indexed_with_its_rendering_mode()
    {
        // Four lines of HI in modes 0 to 3, then an I in mode 0.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-render-modes.pdf"));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        Assert.Equal(9, index.GlyphCount);
        Assert.Equal("HIHIHIHII", index.Text.ToString());
        Assert.Equal(TextRenderingMode.Invisible, index.GetGlyph(6).RenderingMode);
        Assert.Equal(TextRenderingMode.Invisible, index.GetGlyph(7).RenderingMode);
        Assert.Equal(TextRenderingMode.Fill, index.GetGlyph(8).RenderingMode);
        PdfRectangle invisible = index.GetGlyph(6).Quad.Bounds;
        var center = new PathPoint((invisible.Left + invisible.Right) / 2, (invisible.Bottom + invisible.Top) / 2);
        Assert.Equal([6], Indexes(index.Find(center, HitTestKinds.Glyphs)));
    }

    [Fact]
    public void Type_3_glyphs_are_indexed_with_their_unicode_though_not_painted_yet()
    {
        // (abc) in two Type 3 fonts; a ToUnicode CMap maps 97-99 to U+25A0, U+25B2, U+25CF.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        Assert.Equal(6, index.GlyphCount);
        Assert.Equal("■▲●■▲●", index.Text.ToString());
    }

    [Fact]
    public void A_code_a_substitute_draws_as_several_glyphs_is_one_glyph_in_the_index()
    {
        // Six codes; f_i and uni00410042 are drawn as two glyphs each when a substitute has their components.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-glyph-names.pdf"));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        Assert.Equal(6, index.GlyphCount);
        Assert.Equal("fi", index.GetGlyphText(index.GetGlyph(2)).ToString());
    }

    [Fact]
    public void Queries_with_no_kinds_or_points_that_are_not_finite_match_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-truetype-embedded.pdf"));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        Assert.Empty(Matches(index.Find(new PathPoint(96, 705), HitTestKinds.None)));
        Assert.Empty(Matches(index.Find(new PathPoint(double.NaN, 705), HitTestKinds.All)));
        Assert.Empty(Matches(index.Find(new PdfRectangle(0, 0, double.PositiveInfinity, 1), HitTestKinds.All)));
        Assert.Empty(Matches(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.GetGlyph(2));
    }

    [Fact]
    public void An_empty_page_has_an_empty_index()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();

        Assert.Equal(0, index.GlyphCount);
        Assert.True(index.Images.IsEmpty);
        Assert.True(index.Paths.IsEmpty);
        Assert.Empty(index.Links);
        Assert.Empty(Matches(index.Find(new PathPoint(0, 0), HitTestKinds.All)));
        Assert.Empty(Matches(index.Find(new PdfRectangle(0, 0, 612, 792), HitTestKinds.All)));
    }

    [Fact]
    public void Matches_on_a_dense_page_agree_with_testing_every_glyph()
    {
        // 2,000 glyphs over several lines: whatever cell a point falls in, the grid returns exactly the glyphs whose box holds it.
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(2000)));
        HitTestIndex index = document.Pages[0].CreateHitTestIndex();
        Assert.True(index.GlyphCount >= 2000);

        for (int probe = 0; probe < 500; probe++)
        {
            // Spread deterministically over the glyphs and over each glyph's upright box (corners included).
            int target = probe * 7919 % index.GlyphCount;
            PdfRectangle box = index.GetGlyph(target).Quad.Bounds;
            var point = new PathPoint(box.Left + (probe * 37 % 101 / 100.0 * box.Width), box.Bottom + (probe * 61 % 101 / 100.0 * box.Height));
            var expected = new List<int>();
            for (int glyph = 0; glyph < index.GlyphCount; glyph++)
            {
                if (Inside(index.GetGlyph(glyph).Quad, point))
                {
                    expected.Add(glyph);
                }
            }

            Assert.Equal(expected, Indexes(index.Find(point, HitTestKinds.Glyphs)));
        }
    }

    private static List<HitTestItem> Matches(HitTestMatches matches)
    {
        var items = new List<HitTestItem>();
        foreach (HitTestItem item in matches)
        {
            items.Add(item);
        }

        return items;
    }

    private static List<int> Indexes(HitTestMatches matches) => [.. Matches(matches).Select(item => item.Index)];

    /// <summary>Whether a point is inside a convex quad, by the signs of the cross products along its edges (independent of the index).</summary>
    private static bool Inside(Quad quad, PathPoint point)
    {
        PathPoint[] corners = [quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft];
        int sign = 0;
        for (int i = 0; i < 4; i++)
        {
            PathPoint a = corners[i];
            PathPoint b = corners[(i + 1) % 4];
            int current = Math.Sign(((b.X - a.X) * (point.Y - a.Y)) - ((b.Y - a.Y) * (point.X - a.X)));
            if (current != 0)
            {
                if (sign != 0 && current != sign)
                {
                    return false;
                }

                sign = current;
            }
        }

        return sign != 0;
    }

    private static void AssertQuad(Quad expected, Quad actual)
    {
        AssertPoint(expected.BottomLeft, actual.BottomLeft);
        AssertPoint(expected.BottomRight, actual.BottomRight);
        AssertPoint(expected.TopRight, actual.TopRight);
        AssertPoint(expected.TopLeft, actual.TopLeft);
    }

    private static void AssertPoint(PathPoint expected, PathPoint actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
    }
}
