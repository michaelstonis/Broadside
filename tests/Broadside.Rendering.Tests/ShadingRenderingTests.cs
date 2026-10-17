using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Axial and radial shadings through the software rasterizer (ISO 32000-2 §8.7.4.5.3, §8.7.4.5.4, §8.7.4.3), observed as pixels:
/// <c>sh</c> fills the clip without the Background, shading patterns fill and stroke with it, Extend, Domain, BBox and the
/// AntiAlias hint are honoured, and colours convert through the colour-management extension point. Pages are 612 x 792 at 72 DPI,
/// so pixel (x, y) has its centre at user (x + 0.5, 791.5 − y). Expected colours are worked out by hand from the spec's formulas
/// for a red-to-blue Type 2 function, (1 − s, 0, s), rounded; the lookup table may move them by one level.
/// </summary>
public sealed class ShadingRenderingTests
{
    private const string RedToBlue = "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>";

    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Red = new(255, 0, 0, 255);
    private static readonly Rgba Blue = new(0, 0, 255, 255);

    [Fact]
    public void An_axial_shading_extended_at_its_start_is_red_before_the_axis_blends_along_it_and_is_unpainted_after_it()
    {
        // shading-type2-axial.pdf: Coords [72 400 540 400], Extend [true false], clipped to y 300..500 (rows 292..491).
        using Bitmap bitmap = RenderCorpus("shading-type2-axial.pdf");

        Assert.Equal(Red, bitmap.At(30, 392));
        Assert.Equal(Red, bitmap.At(0, 292));

        // x = 306.5: s = 234.5 / 468 = 0.501, (127.2, 0, 127.8).
        AssertNear(new Rgba(127, 0, 128, 255), bitmap.At(306, 392));

        // x = 189.5: s = 0.25, (191.25, 0, 63.75).
        AssertNear(new Rgba(191, 0, 64, 255), bitmap.At(189, 392));
        Assert.Equal(White, bitmap.At(560, 392));
        Assert.Equal(White, bitmap.At(306, 200));
        Assert.Equal(White, bitmap.At(306, 495));
    }

    [Fact]
    public void A_radial_cone_extended_at_both_ends_paints_the_greatest_s_through_each_point()
    {
        // shading-type3-radial.pdf: Coords [200 400 20 400 420 100], circles (200, 400) r 20 and (400, 420) r 100, both
        // extended, clipped to y 200..600.
        using Bitmap bitmap = RenderCorpus("shading-type3-radial.pdf");

        // At (300.5, 400.5), q = (100.5, 0.5), d = (200, 20), Δr = 80: a = 34000, b = 21710, c = 9700.5; the roots are
        // s = 0.2627 and s = 0.9884, and the later circle wins: (2.96, 0, 252.04).
        AssertNear(new Rgba(3, 0, 252, 255), bitmap.At(300, 391));

        // Far right of the end circle on the axis: extended past s = 1, blue. Above the cone, near the start: no circle, white.
        Assert.Equal(Blue, bitmap.At(600, 391));
        Assert.Equal(White, bitmap.At(150, 300));

        // The start's extension shrinks to radius 0 at s = −0.25, at (150, 395): left of that apex nothing is painted.
        Assert.Equal(Red, bitmap.At(170, 391));
        Assert.Equal(White, bitmap.At(120, 391));
    }

    [Fact]
    public void Radial_spheres_take_the_colour_of_the_last_circle_through_each_point_and_paint_nothing_outside_the_larger_circle()
    {
        using Bitmap bitmap = RenderCorpus("shading-radial-cases.pdf");

        // A: from the point (86, 706) to the circle (111, 681) r 70. At the end centre, 25√2 (1 − s) = 70 s, s = 0.3356.
        AssertNear(new Rgba(169, 0, 86, 255), bitmap.At(110, 110), tolerance: 3);

        // At the start point, s = 0: red. Outside the end circle, inside the cell: unpainted (no Extend).
        AssertNear(Red, bitmap.At(85, 85), tolerance: 6);
        Assert.Equal(White, bitmap.At(30, 30));

        // B: from the circle (306, 681) r 70 to the point (331, 706): at the end point s = 1, blue; at the start centre
        // 25√2 s = 70 (1 − s), s = 0.6644.
        AssertNear(Blue, bitmap.At(330, 85), tolerance: 6);
        AssertNear(new Rgba(86, 0, 169, 255), bitmap.At(305, 110), tolerance: 3);
        Assert.Equal(White, bitmap.At(225, 30));
    }

    [Fact]
    public void A_radial_shading_extended_at_its_larger_end_fills_the_whole_area_except_inside_the_inner_circle()
    {
        // C: from (501, 681) r 15 to (516, 681) r 50, Extend [false true], clipped to the cell 411..591 x 591..771.
        using Bitmap bitmap = RenderCorpus("shading-radial-cases.pdf");

        Assert.Equal(Blue, bitmap.At(415, 25));
        Assert.Equal(Blue, bitmap.At(585, 195));
        Assert.Equal(White, bitmap.At(500, 110));
        Assert.Equal(White, bitmap.At(600, 110));
    }

    [Fact]
    public void Circles_touching_inside_and_outside_follow_the_spec_s_parametric_circles()
    {
        using Bitmap bitmap = RenderCorpus("shading-radial-cases.pdf");

        // D: from (81, 486) r 30 to (111, 486) r 60, the start touching the end at x = 51 (the quadratic in s is linear). On the
        // axis at x = 161.5: 161.5 − (81 + 30 s) = 30 + 30 s, s = 0.8417. Inside the start circle: unpainted.
        AssertNear(new Rgba(40, 0, 215, 255), bitmap.At(161, 305));
        Assert.Equal(White, bitmap.At(80, 305));
        Assert.Equal(White, bitmap.At(180, 305));

        // E: from (261, 486) r 15 to (321, 486) r 45, touching from outside at x = 276, extended at both ends. The start's
        // extension shrinks to radius 0 at s = −0.5, the apex x = 231: at x = 240.5 its circle s = −0.183 passes, red; at
        // x = 230.5 no circle with a radius passes, white; at x = 380.5, s = 1.16 > 1, blue.
        Assert.Equal(Red, bitmap.At(240, 305));
        Assert.Equal(White, bitmap.At(230, 305));
        Assert.Equal(Blue, bitmap.At(380, 305));
        Assert.Equal(White, bitmap.At(225, 230));
    }

    [Fact]
    public void Sh_ignores_the_background_that_a_shading_pattern_paints()
    {
        // Cell A's shading has Background [0.8 0.8 0.8]: sh leaves its corner white; cell F, the same shading as a pattern,
        // paints its corner light gray (0.8 × 255 = 204) and its sphere the same colours as cell A.
        using Bitmap bitmap = RenderCorpus("shading-radial-cases.pdf");

        Assert.Equal(White, bitmap.At(30, 30));
        Assert.Equal(new Rgba(204, 204, 204, 255), bitmap.At(415, 225));
        Assert.Equal(new Rgba(204, 204, 204, 255), bitmap.At(585, 390));
        AssertNear(bitmap.At(110, 110), bitmap.At(110 + 390, 110 + 195));
    }

    [Fact]
    public void A_shading_pattern_fills_its_path_with_the_shading_in_pattern_space_and_the_background_outside_the_shading()
    {
        // pattern-shading-axial.pdf: the square 50..550 filled with an axial shading from x = 100 to 400 (pattern space scaled by
        // 0.5), not extended, Background 0.9 gray (229.5, rounded up).
        using Bitmap bitmap = RenderCorpus("pattern-shading-axial.pdf");

        var background = new Rgba(230, 230, 230, 255);
        Assert.Equal(background, bitmap.At(75, 492));
        Assert.Equal(background, bitmap.At(500, 492));
        AssertNear(new Rgba(127, 0, 128, 255), bitmap.At(250, 492));
        Assert.Equal(White, bitmap.At(20, 492));
        Assert.Equal(White, bitmap.At(250, 760));
    }

    [Fact]
    public void A_stroke_in_a_shading_pattern_paints_the_shading_where_the_stroke_covers()
    {
        using Bitmap bitmap = RenderContent(
            "/Pattern CS /P0 SCN 20 w 100 400 m 500 400 l S",
            "/Pattern << /P0 5 0 R >>",
            "<< /Type /Pattern /PatternType 2 /Shading << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [100 0 500 0] " + RedToBlue + " >> >>");

        AssertNear(new Rgba(127, 0, 128, 255), bitmap.At(300, 391));
        AssertNear(new Rgba(127, 0, 128, 255), bitmap.At(300, 383));
        Assert.Equal(White, bitmap.At(300, 370));
        Assert.Equal(White, bitmap.At(300, 410));
    }

    [Fact]
    public void Sh_maps_the_shading_through_the_ctm_and_fills_the_whole_clip()
    {
        // In a space scaled by 2, the axis 0..100 spans device x 0..200 and is extended at both ends, so the whole page is painted.
        using Bitmap bitmap = RenderContent(
            "2 0 0 2 0 0 cm /Sh0 sh",
            "/Shading << /Sh0 5 0 R >>",
            "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 100 0] /Extend [true true] " + RedToBlue + " >>");

        // Device x = 99.5 is user x = 49.75: s = 0.4975, (128.1, 0, 126.9).
        AssertNear(new Rgba(128, 0, 127, 255), bitmap.At(99, 400));
        Assert.Equal(Blue, bitmap.At(300, 0));
        Assert.Equal(Blue, bitmap.At(611, 791));
    }

    [Fact]
    public void The_domain_maps_the_axis_onto_part_of_the_function()
    {
        // Domain [0.5 1]: the start of the axis takes f(0.5), purple, the end f(1), blue.
        using Bitmap bitmap = RenderContent(
            "/Sh0 sh",
            "/Shading << /Sh0 5 0 R >>",
            "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [100 0 500 0] /Domain [0.5 1] /Extend [true true] " + RedToBlue + " >>");

        AssertNear(new Rgba(128, 0, 128, 255), bitmap.At(50, 400));
        AssertNear(new Rgba(64, 0, 191, 255), bitmap.At(299, 400));
        Assert.Equal(Blue, bitmap.At(600, 400));
    }

    [Fact]
    public void The_bounding_box_clips_the_shading_and_a_pattern_s_background_fills_around_it()
    {
        const string shading = "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 612 0] /Extend [true true] /BBox [100 100 200 200] "
            + "/Background [0 1 0] " + RedToBlue + " >>";
        using Bitmap sh = RenderContent("/Sh0 sh", "/Shading << /Sh0 5 0 R >>", shading);
        using Bitmap pattern = RenderContent(
            "/Pattern cs /P0 scn 50 50 300 300 re f",
            "/Pattern << /P0 5 0 R >>",
            "<< /Type /Pattern /PatternType 2 /Shading 6 0 R >>",
            shading);

        Assert.NotEqual(White, sh.At(150, 792 - 150));
        Assert.Equal(White, sh.At(90, 792 - 150));
        Assert.Equal(White, sh.At(150, 792 - 210));
        Assert.Equal(sh.At(150, 792 - 150), pattern.At(150, 792 - 150));
        Assert.Equal(new Rgba(0, 255, 0, 255), pattern.At(90, 792 - 150));
        Assert.Equal(new Rgba(0, 255, 0, 255), pattern.At(300, 792 - 300));
        Assert.Equal(White, pattern.At(400, 792 - 150));
    }

    [Fact]
    public void The_anti_alias_hint_smooths_the_edge_where_the_shading_stops()
    {
        // The axis starts half way across pixel column 100 and is not extended back. Without the hint the pixel's centre, on the
        // edge, decides alone (painted); with it the pixel is half covered.
        const string plain = "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [100.5 0 600 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [1 0 0] /N 1 >> >>";
        using Bitmap hard = RenderContent("/Sh0 sh", "/Shading << /Sh0 5 0 R >>", plain);
        using Bitmap smooth = RenderContent("/Sh0 sh", "/Shading << /Sh0 5 0 R >>", plain.Replace("<< /ShadingType 2", "<< /AntiAlias true /ShadingType 2", StringComparison.Ordinal));

        Assert.Equal(Red, hard.At(100, 400));
        Assert.Equal(White, hard.At(99, 400));
        AssertNear(new Rgba(255, 128, 128, 255), smooth.At(100, 400));
        Assert.Equal(Red, smooth.At(101, 400));
        Assert.Equal(White, smooth.At(99, 400));
    }

    [Fact]
    public void Shading_colours_convert_through_the_colour_management_extension_point()
    {
        // A DeviceCMYK shading: its colours reach the engine's IColorManagement, here one that makes every colour blue.
        using PdfDocument document = PdfDocument.Open(
            ContentSamples.OnePage(
                Encoding.ASCII.GetBytes("/Sh0 sh"),
                "/Shading << /Sh0 5 0 R >>",
                "<< /ShadingType 3 /ColorSpace /DeviceCMYK /Coords [300 400 0 300 400 100] /Function << /FunctionType 2 /Domain [0 1] /C0 [0 1 1 0] /C1 [1 0 0 0] /N 1 >> >>"),
            new PdfOptions().UseColorManagement(new BlueColorManagement()));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        Assert.Equal(Blue, bitmap.At(300, 392));
        Assert.Equal(Blue, bitmap.At(340, 392));
        Assert.Equal(White, bitmap.At(450, 392));
    }

    private static void AssertNear(Rgba expected, Rgba actual, int tolerance = 1)
    {
        bool near = Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance
            && expected.A == actual.A;
        Assert.True(near, $"Expected {expected} within {tolerance}, got {actual}.");
    }

    private static Bitmap RenderCorpus(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }

    private static Bitmap RenderContent(string content, string resources = "", params string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes(content), resources, objects));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }

    /// <summary>Converts every colour of every space to RGB blue.</summary>
    private sealed class BlueColorManagement : IColorManagement
    {
        public IColorConverter CreateConverter(PdfColorSpace source, ColorConversion conversion) => new BlueConverter(source.ComponentCount);

        private sealed class BlueConverter(int inputs) : IColorConverter
        {
            public int InputCount => inputs;

            public int OutputCount => 3;

            public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    destination[(3 * i)..((3 * i) + 3)].Clear();
                    destination[(3 * i) + 2] = 1;
                }
            }
        }
    }
}
