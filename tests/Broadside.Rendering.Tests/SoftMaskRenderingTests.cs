using System.Text;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Soft masks through the software rasterizer (ISO 32000-2 §11.5, §11.6.4.3, §11.6.5.1), observed as pixels. Pages are 612 x 792 at
/// 72 DPI, so pixel (x, y) has its centre at user (x + 0.5, 791.5 − y); row 591 is user y 200.5. The small masks' groups cover
/// x and y 100 to 300, so pixel (200, 591) is inside them and (400, 591) outside.
/// </summary>
public sealed class SoftMaskRenderingTests
{
    private const string States =
        "/ExtGState << /L << /SMask << /S /Luminosity /G 5 0 R >> >> /L1 << /SMask << /S /Luminosity /G 5 0 R /BC [1] >> >>"
        + " /A << /SMask << /S /Alpha /G 6 0 R >> >> /AT << /SMask << /S /Alpha /G 6 0 R /TR 7 0 R >> >>"
        + " /Big << /SMask << /S /Luminosity /G 8 0 R >> >> /BigShape << /SMask << /S /Luminosity /G 8 0 R >> /AIS true >>"
        + " /L25 << /SMask << /S /Luminosity /G 9 0 R >> >> /Inverted << /TR 7 0 R >> /Quarter << /ca 0.25 >> /None << /SMask /None >> >>"
        + " /XObject << /Fm0 10 0 R /Ko 11 0 R /Cmyk 12 0 R >>";

    private const string Red = "1 0 0 rg 0 0 612 792 re f";

    private static readonly Rgba White = new(255, 255, 255, 255);

    private static readonly string[] Objects =
    [
        Form("/Group << /S /Transparency /CS /DeviceGray >> /BBox [100 100 300 300]", "0.5 g 100 100 200 200 re f"), // 5
        Form("/Group << /S /Transparency >> /BBox [100 100 300 300]", "/Quarter gs 100 100 200 200 re f"), // 6
        "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>", // 7
        Form("/Group << /S /Transparency /CS /DeviceGray >> /BBox [0 0 612 792]", "0.5 g 0 0 612 792 re f"), // 8
        Form("/Group << /S /Transparency /CS /DeviceGray >> /BBox [0 0 612 792]", "0.25 g 0 0 612 792 re f"), // 9
        Form("/Group << /S /Transparency >> /BBox [0 0 612 792]", "1 0 0 rg 100 100 200 200 re f 200 100 200 200 re f"), // 10
        Form("/Group << /S /Transparency /K true >> /BBox [0 0 612 792]", "1 0 0 rg 100 100 200 200 re f /MASK gs 0 0 1 rg 100 100 200 200 re f"), // 11
        Form("/Group << /S /Transparency /I true /CS /DeviceCMYK >> /BBox [0 0 612 792]", "/L gs 0 1 0 0 k 0 0 612 792 re f"), // 12
    ];

    [Fact]
    public void A_luminosity_mask_scales_the_paint_by_its_groups_luminosity_and_the_backdrop_outside_it()
    {
        using Bitmap black = Render($"/L gs {Red}");
        using Bitmap white = Render($"/L1 gs {Red}");

        // Inside the group: luminosity 0.5, red at half opacity over white. Outside: BC 0 masks everything, BC 1 nothing (§11.5.3).
        AssertNear(new Rgba(255, 128, 128, 255), black.At(200, 591));
        Assert.Equal(White, black.At(400, 591));
        AssertNear(new Rgba(255, 128, 128, 255), white.At(200, 591));
        AssertNear(new Rgba(255, 0, 0, 255), white.At(400, 591));
    }

    [Fact]
    public void An_alpha_mask_takes_its_groups_alpha_through_its_transfer_function()
    {
        using Bitmap alpha = Render($"/A gs {Red}");
        using Bitmap inverted = Render($"/AT gs {Red}");

        // The group paints at alpha 0.25 and nothing outside; the inversion makes those 0.75 and 1 (§11.6.5.1, TR).
        AssertNear(new Rgba(255, 191, 191, 255), alpha.At(200, 591));
        Assert.Equal(White, alpha.At(400, 591));
        AssertNear(new Rgba(255, 64, 64, 255), inverted.At(200, 591));
        AssertNear(new Rgba(255, 0, 0, 255), inverted.At(400, 591));
    }

    [Fact]
    public void Setting_the_mask_to_None_paints_unmasked()
    {
        using Bitmap bitmap = Render($"/L gs /None gs {Red}");

        AssertNear(new Rgba(255, 0, 0, 255), bitmap.At(400, 591));
    }

    [Fact]
    public void The_pages_transfer_function_does_not_reach_the_masks_group()
    {
        // §11.7.5.2: the inversion applies to the red paint (cyan), not to the mask's 0.25 gray (which would become 0.75).
        using Bitmap bitmap = Render($"/Inverted gs /L25 gs {Red}");

        AssertNear(new Rgba(191, 255, 255, 255), bitmap.At(200, 591), 2);
    }

    [Fact]
    public void A_group_painted_under_a_mask_is_masked_once_as_a_whole()
    {
        // §11.6.4.3: the overlap of the group's two squares is masked with the rest, not twice.
        using Bitmap bitmap = Render("/Big gs /Fm0 Do");

        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(150, 591));
        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(250, 591));
        Assert.Equal(White, bitmap.At(450, 591));
    }

    [Fact]
    public void In_a_knockout_group_an_opacity_mask_knocks_out_by_shape_and_a_shape_mask_by_its_value()
    {
        // The blue square replaces the red one by its shape. An opacity mask leaves its shape whole, so no red is left and the blue
        // composites at 0.5 over the group's backdrop; a shape mask halves the shape and leaves the opacity whole
        // (§11.4.8: the element composites at αs / fs = 1), so half the red stays beside half an opaque blue.
        using Bitmap opacity = Render("/Ko Do", "/Big");
        using Bitmap shape = Render("/Ko Do", "/BigShape");

        AssertNear(new Rgba(128, 128, 255, 255), opacity.At(200, 591), 2);
        AssertNear(new Rgba(128, 0, 128, 255), shape.At(200, 591), 2);
    }

    [Fact]
    public void A_mask_inside_a_cmyk_group_masks_in_the_groups_colour_model()
    {
        using Bitmap bitmap = Render("/Cmyk Do");
        using Bitmap unmasked = Render("0 1 0 0 k 0 0 612 792 re f");

        Rgba inside = bitmap.At(200, 591);
        Rgba full = unmasked.At(200, 591);
        Assert.Equal(White, bitmap.At(400, 591));
        Assert.True(inside.G > full.G + 50 && inside.G < 250, $"Expected half the magenta of {full}, got {inside}.");
    }

    [Fact]
    public void Masks_apply_under_the_overprint_preview()
    {
        using Bitmap paint = Render("/L gs 0 1 0 0 k 0 0 612 792 re f", preview: true);
        using Bitmap group = Render("/L gs /Fm0 Do", preview: true);
        using Bitmap unmasked = Render("0 1 0 0 k 0 0 612 792 re f", preview: true);

        Rgba full = unmasked.At(200, 591);
        Rgba half = paint.At(200, 591);
        Assert.Equal(White, paint.At(400, 591));
        Assert.True(half.G > full.G + 50 && half.G < 250, $"Expected half the magenta of {full}, got {half}.");

        // The group's squares reach x 400; outside the mask's group (x > 300) nothing shows, inside it the red is half strength.
        Assert.Equal(White, group.At(350, 591));
        Rgba red = group.At(250, 591);
        Assert.True(red.R > 240 && red.G is > 90 and < 170, $"Expected half red, got {red}.");
    }

    private static string Form(string entries, string content) =>
        ContentSamples.Stream(content, $"/Type /XObject /Subtype /Form {entries} /Resources << {States} >>");

    private static Bitmap Render(string content, string knockoutMask = "/Big", bool preview = false)
    {
        string[] objects = [.. Objects.Select(o => o.Replace("/MASK gs", knockoutMask + " gs", StringComparison.Ordinal))];
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), States, objects));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0], new RenderOptions().WithOverprintPreview(preview));
    }

    private static void AssertNear(Rgba expected, Rgba actual, int tolerance = 1)
    {
        bool near = Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance
            && Math.Abs(expected.A - actual.A) <= tolerance;
        Assert.True(near, $"Expected {expected} within {tolerance}, got {actual}.");
    }
}
