using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Transparency groups through the software rasterizer (ISO 32000-2 §11.4, §11.6.6, §11.7.4.4), observed as pixels. Pages are 612 x
/// 792 at 72 DPI, so pixel (x, y) has its centre at user (x + 0.5, 791.5 − y). Expected values come from the group compositing
/// formulas of §11.4.4 to §11.4.8 worked by hand; colours pass through 8 bits on the way, so results may move by a level or two.
/// </summary>
public sealed class GroupRenderingTests
{
    private const string States =
        "/ExtGState << /Half << /ca 0.5 >> /HalfStroke << /CA 0.5 >> /Multiply << /BM /Multiply >> /Difference << /BM /Difference >> "
        + "/HalfMultiply << /ca 0.5 /BM /Multiply >> /Clear << /ca 0 >> >>";

    private static readonly Rgba White = new(255, 255, 255, 255);

    [Fact]
    public void A_group_composites_at_its_opacity_as_one_object()
    {
        // Two overlapping red squares in a group painted at ca 0.5: the group is one object, so the overlap is no darker than the
        // rest, (1, 0.5, 0.5). Painted separately at ca 0.5 the overlap would take two coats, (1, 0.25, 0.25).
        using Bitmap bitmap = Render("/Half gs /Fm0 Do", Form("/Group << /S /Transparency >>", "1 0 0 rg 100 100 200 200 re f 200 100 200 200 re f"));

        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(150, 591));
        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(250, 591));
        Assert.Equal(White, bitmap.At(450, 591));
    }

    [Theory]
    [InlineData("", 128)]
    [InlineData("/I true", 255)]
    public void Blend_modes_inside_a_non_isolated_group_see_the_backdrop_and_inside_an_isolated_one_do_not(string isolated, int red)
    {
        // Red multiplied over the page's gray (0.5): (0.5, 0, 0) when the group's initial backdrop is the page; over the transparent
        // backdrop of an isolated group there is nothing to multiply, so the red composites as it is (§11.4.5).
        using Bitmap bitmap = Render(
            "0.5 g 0 0 612 792 re f /Fm0 Do",
            Form($"/Group << /S /Transparency {isolated} >>", "/Multiply gs 1 0 0 rg 100 100 200 200 re f"));

        AssertNear(new Rgba((byte)red, 0, 0, 255), bitmap.At(200, 591));
        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(450, 591));
    }

    [Theory]
    [InlineData("/K true", 128)]
    [InlineData("", 64)]
    public void In_a_knockout_group_a_later_object_replaces_an_earlier_one_where_they_overlap(string knockout, int overlap)
    {
        // Two black squares at ca 0.5: in a knockout group the second composites with the initial backdrop (white) only, so the
        // overlap is one coat (0.5); otherwise it takes two (0.25) (§11.4.6).
        using Bitmap bitmap = Render(
            "/Fm0 Do",
            Form($"/Group << /S /Transparency /I true {knockout} >>", "/Half gs 100 100 200 200 re f 200 100 200 200 re f"));

        AssertNear(new Rgba((byte)overlap, (byte)overlap, (byte)overlap, 255), bitmap.At(250, 591));
        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(150, 591));
        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(350, 591));
    }

    [Fact]
    public void A_paint_of_alpha_zero_still_knocks_out_what_is_under_its_shape()
    {
        // Shape and opacity are separate (§11.4.6): the second square has shape 1 and opacity 0, so in a knockout group it replaces
        // the black under it with the initial backdrop (transparent, then the white page) and adds nothing of its own.
        using Bitmap bitmap = Render(
            "/Fm0 Do",
            Form("/Group << /S /Transparency /I true /K true >>", "100 100 200 200 re f /Clear gs 200 100 200 200 re f"));

        Assert.Equal(White, bitmap.At(250, 591));
        AssertNear(new Rgba(0, 0, 0, 255), bitmap.At(150, 591));
    }

    [Fact]
    public void A_non_isolated_knockout_group_knocks_out_to_the_backdrop_before_the_group()
    {
        // Over a blue page, black at ca 0.5 then red at ca 0.5 in a non-isolated knockout group: the red composites with the blue
        // only, (0.5, 0, 0.5); the black is gone where they overlap.
        using Bitmap bitmap = Render(
            "0 0 1 rg 0 0 612 792 re f /Fm0 Do",
            Form("/Group << /S /Transparency /K true >>", "/Half gs 0 g 100 100 200 200 re f 1 0 0 rg 200 100 200 200 re f"));

        AssertNear(new Rgba(128, 0, 128, 255), bitmap.At(250, 591));
        AssertNear(new Rgba(0, 0, 128, 255), bitmap.At(150, 591));
    }

    [Fact]
    public void A_group_composites_with_the_blend_mode_at_Do_once()
    {
        // The group's red composites onto white through Difference: |1 − (1, 0, 0)| = (0, 1, 1). Inside, the blend mode starts as
        // Normal (§11.6.6), so the red is not differenced with the group's own (empty) backdrop first.
        using Bitmap bitmap = Render("/Difference gs /Fm0 Do", Form("/Group << /S /Transparency /I true >>", "1 0 0 rg 100 100 200 200 re f"));

        AssertNear(new Rgba(0, 255, 255, 255), bitmap.At(200, 591));
    }

    [Fact]
    public void A_non_isolated_group_in_a_blend_mode_composites_its_colour_with_the_backdrop_removed()
    {
        // Over gray (0.5), a non-isolated group holds red multiplied over the gray backdrop, Cn = (0.5, 0, 0) with group alpha 1.
        // Its colour without the backdrop (§11.4.4, Result: C = Cn + (Cn − C0)(α0 / αg − α0)) is Cn itself, which the group's
        // Multiply then composites onto the gray: (0.25, 0, 0). Treated as isolated it would have been red, giving (0.5, 0, 0).
        using Bitmap bitmap = Render(
            "0.5 g 0 0 612 792 re f /Multiply gs /Fm0 Do",
            Form("/Group << /S /Transparency >>", "/Multiply gs 1 0 0 rg 100 100 200 200 re f"));

        AssertNear(new Rgba(64, 0, 0, 255), bitmap.At(200, 591), tolerance: 2);
    }

    [Fact]
    public void A_group_is_composited_through_the_clip_in_force_where_it_is_painted()
    {
        using Bitmap bitmap = Render(
            "q 100 100 50 50 re W n /Half gs /Fm0 Do Q",
            Form("/Group << /S /Transparency >>", "1 0 0 rg 0 0 612 792 re f"));

        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(125, 666));
        Assert.Equal(White, bitmap.At(175, 666));
        Assert.Equal(White, bitmap.At(125, 600));
    }

    [Fact]
    public void A_translucent_stroke_over_its_own_fill_composites_with_the_backdrop_only()
    {
        // b with a 20-unit blue stroke at CA 0.5 over a red fill: one object in a knockout group (§11.7.4.4), so inside the square
        // the stroke's half composites with the white page, (0.5, 0.5, 1), not with the red, (0.5, 0, 0.5).
        using Bitmap bitmap = Render("/HalfStroke gs 20 w 1 0 0 rg 0 0 1 RG 100 100 200 200 re b");

        AssertNear(new Rgba(128, 128, 255, 255), bitmap.At(105, 591));
        AssertNear(new Rgba(128, 128, 255, 255), bitmap.At(95, 591));
        Assert.Equal(new Rgba(255, 0, 0, 255), bitmap.At(200, 591));
    }

    [Fact]
    public void Colours_in_a_cmyk_group_blend_as_complemented_cmyk()
    {
        // Cyan multiplied with magenta in an isolated DeviceCMYK group: the complements multiply, (0 1 1 1) × (1 0 1 1) =
        // (0 0 1 1), so C = M = 1 (§11.3.4); the result shows as DeviceCMYK (1 1 0 0) does.
        byte[] file = File("/Fm0 Do", Form(
            "/Group << /S /Transparency /I true /CS /DeviceCMYK >>",
            "1 0 0 0 k 100 100 200 200 re f /Multiply gs 0 1 0 0 k 100 100 200 200 re f",
            States));
        using PdfDocument document = PdfDocument.Open(file);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        AssertNear(Expected(document, PdfDeviceCmykColorSpace.Instance, 1, 1, 0, 0), bitmap.At(200, 591));
    }

    [Fact]
    public void Black_in_a_cmyk_group_blends_on_its_own_plane()
    {
        // K 0.5 multiplied with K 0.5: complements 0.5 × 0.5 = 0.25, so K = 0.75 (§11.3.4); C, M, Y stay 0.
        byte[] file = File("/Fm0 Do", Form(
            "/Group << /S /Transparency /I true /CS /DeviceCMYK >>",
            "0 0 0 0.5 k 100 100 200 200 re f /Multiply gs 100 100 200 200 re f",
            States));
        using PdfDocument document = PdfDocument.Open(file);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        AssertNear(Expected(document, PdfDeviceCmykColorSpace.Instance, 0, 0, 0, 0.75f), bitmap.At(200, 591), tolerance: 2);
    }

    [Fact]
    public void A_page_with_a_cmyk_group_composites_onto_white_in_cmyk()
    {
        // Cyan at ca 0.5 on a page whose group blends in DeviceCMYK: half the ink, C = 0.5 (§11.4.7), not half-way in RGB between
        // white and cyan's RGB.
        byte[] file = ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Group << /S /Transparency /CS /DeviceCMYK >> /Resources << {States} >> /Contents 4 0 R >>",
            ContentSamples.Stream("/Half gs 1 0 0 0 k 100 100 200 200 re f"),
        ]);
        using PdfDocument document = PdfDocument.Open(file);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        AssertNear(Expected(document, PdfDeviceCmykColorSpace.Instance, 0.5f, 0, 0, 0), bitmap.At(200, 591));
        Assert.Equal(White, bitmap.At(450, 591));
    }

    [Fact]
    public void A_gray_group_holds_gray()
    {
        // Red painted into an isolated DeviceGray group becomes its gray, 0.3 (§10.4.2.2 via §11.6.6), once the group needs a
        // layer (here its opacity).
        using Bitmap bitmap = Render("/Half gs /Fm0 Do", Form("/Group << /S /Transparency /I true /CS /DeviceGray >>", "1 0 0 rg 100 100 200 200 re f"));

        // 0.3 × 255 = 76.5 over white at one half: 165.75.
        AssertNear(new Rgba(166, 166, 166, 255), bitmap.At(200, 591));
    }

    [Theory]
    [InlineData("/Multiply gs ", 128)]
    [InlineData("", 255)]
    public void A_tiling_pattern_s_cell_is_a_non_isolated_group_whose_blend_modes_see_the_backdrop(string blend, int red)
    {
        // §11.6.7: red multiplied over the page's gray inside the cell gives (0.5, 0, 0); in the Normal mode the cell is the same
        // isolated or not, and its red composites as it is.
        string cell = ContentSamples.Stream(
            blend + "1 0 0 rg 0 0 10 10 re f",
            "/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10 /Resources << " + States + " >>");
        using Bitmap bitmap = Render("0.5 g 0 0 612 792 re f /Pattern cs /P0 scn 100 100 200 200 re f", "/Pattern << /P0 5 0 R >>", [cell]);

        AssertNear(new Rgba((byte)red, 0, 0, 255), bitmap.At(200, 591));
        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(450, 591));
    }

    [Fact]
    public void Groups_nested_eight_deep_composite_each_at_their_opacity()
    {
        // Eight nested groups each painted at ca 0.5 around one black square: 0.5^8 of black over white.
        var objects = new List<string>();
        for (int level = 0; level < 8; level++)
        {
            string content = level == 0 ? "100 100 200 200 re f" : "/Half gs /Fm Do";
            string resources = level == 0 ? States : $"/XObject << /Fm {5 + level - 1} 0 R >> " + States;
            objects.Add(Form("/Group << /S /Transparency >>", content, resources));
        }

        using Bitmap bitmap = Render("/Half gs /Fm Do", "/XObject << /Fm 12 0 R >>", [.. objects]);

        // 255 × (1 − 0.5^8) ≈ 254.
        AssertNear(new Rgba(254, 254, 254, 255), bitmap.At(200, 591));
    }

    [Theory]
    [InlineData("/CS /DeviceCMYK")]
    [InlineData("/CS /DeviceGray")]
    public void A_function_based_shading_in_an_isolated_group_paints_in_the_groups_colour_space(string space)
    {
        // A Type 1 shading of one colour (#110 paints it through the mesh layer) at ca 0.5 in a group: in a gray group it is the
        // colour's gray level; in a CMYK group it goes through CMYK and back, close to the plain render (§11.3.4).
        string shading = "<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] /Matrix [612 0 0 792 0 0] /Function 7 0 R >>";
        string function = ContentSamples.Stream("{pop pop 0.2 0.6 0.4}", "/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]");
        string resources = States + " /Shading << /Sh 6 0 R >>";
        const string content = "/Half gs 100 100 200 200 re W n /Sh sh";
        using Bitmap plain = Render("/Fm0 Do", Form("", content, resources), shading, function);
        using Bitmap grouped = Render("/Fm0 Do", Form($"/Group << /S /Transparency /I true {space} >>", content, resources), shading, function);

        Rgba actual = grouped.At(200, 591);
        if (space.Contains("Gray", StringComparison.Ordinal))
        {
            // Level 0.3 × 0.2 + 0.59 × 0.6 + 0.11 × 0.4 = 0.458, half over white: 0.729.
            AssertNear(new Rgba(186, 186, 186, 255), actual, 3);
        }
        else
        {
            AssertNear(plain.At(200, 591), actual, 40);
        }

        Assert.Equal(White, grouped.At(450, 591));
    }

    private static Rgba Expected(PdfDocument document, PdfColorSpace space, params float[] components)
    {
        Span<float> rgb = stackalloc float[3];
        document.GetColorConverter(space).Convert(components, rgb);
        return new Rgba(Channel(rgb[0]), Channel(rgb[1]), Channel(rgb[2]), 255);
    }

    private static byte Channel(float value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    private static void AssertNear(Rgba expected, Rgba actual, int tolerance = 1)
    {
        bool near = Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance
            && Math.Abs(expected.A - actual.A) <= tolerance;
        Assert.True(near, $"Expected {expected} within {tolerance}, got {actual}.");
    }

    private static string Form(string entries, string content, string resources = States) =>
        ContentSamples.Stream(content, $"/Type /XObject /Subtype /Form /BBox [0 0 612 792] {entries} /Resources << {resources} >>");

    private static byte[] File(string content, params string[] objects) =>
        ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), States + " /XObject << /Fm0 5 0 R >>", objects);

    private static Bitmap Render(string content, params string[] objects) => Render(content, "/XObject << /Fm0 5 0 R >>", objects);

    private static Bitmap Render(string content, string resources, string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), States + " " + resources, objects));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }
}
