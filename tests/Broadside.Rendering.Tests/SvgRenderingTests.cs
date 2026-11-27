using System.Text;
using Broadside.Graphics;

namespace Broadside.Rendering.Tests;

/// <summary>
/// SVG imported as a form XObject (issue #643), drawn on a page the size of the form, saved, reopened and rasterized at 96 DPI, so
/// one SVG pixel is one device pixel. Each test reads pixels the SVG semantics fix (ISO 32000-2 §8.10, §8.7.4.5, §11.6.5, §11.6.6).
/// </summary>
public sealed class SvgRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Red = new(255, 0, 0, 255);
    private static readonly Rgba Blue = new(0, 0, 255, 255);
    private static readonly Rgba Lime = new(0, 255, 0, 255);

    [Fact]
    public void A_rect_fills_its_area_in_svg_coordinates_with_y_down()
    {
        using Bitmap bitmap = Render("""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><rect x="0" y="0" width="50" height="20" fill="red"/></svg>""");

        Assert.Equal(100, bitmap.Width);
        AssertNear(Red, bitmap.At(10, 5));
        AssertNear(White, bitmap.At(10, 50));
        AssertNear(White, bitmap.At(75, 5));
    }

    [Fact]
    public void A_view_box_scales_the_content_into_the_viewport()
    {
        using Bitmap bitmap = Render("""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 10 10"><rect x="5" y="5" width="5" height="5" fill="blue"/></svg>""");

        AssertNear(Blue, bitmap.At(75, 75));
        AssertNear(White, bitmap.At(25, 25));
    }

    [Fact]
    public void Path_data_with_arcs_and_relative_commands_draws_a_closed_shape()
    {
        using Bitmap bitmap = Render("""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><path d="M10 50a40 40 0 1 0 80 0a40 40 0 1 0-80 0z" fill="lime"/></svg>""");

        AssertNear(Lime, bitmap.At(50, 50));
        AssertNear(Lime, bitmap.At(50, 15));
        AssertNear(White, bitmap.At(12, 12));
    }

    [Fact]
    public void Transforms_nest_in_order_with_the_first_outermost()
    {
        using Bitmap bitmap = Render("""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><g transform="translate(50 0)"><rect transform="scale(2)" width="10" height="10" fill="red"/></g></svg>""");

        AssertNear(Red, bitmap.At(65, 15));
        AssertNear(White, bitmap.At(45, 15));
        AssertNear(White, bitmap.At(75, 25));
    }

    [Fact]
    public void A_stroke_is_centered_on_the_path_with_its_width()
    {
        using Bitmap bitmap = Render("""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><line x1="0" y1="50" x2="100" y2="50" stroke="blue" stroke-width="20"/></svg>""");

        AssertNear(Blue, bitmap.At(50, 42));
        AssertNear(Blue, bitmap.At(50, 58));
        AssertNear(White, bitmap.At(50, 35));
    }

    [Fact]
    public void A_linear_gradient_in_bounding_box_units_blends_across_the_shape()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <defs><linearGradient id="g"><stop offset="0" stop-color="#000"/><stop offset="1" stop-color="#fff"/></linearGradient></defs>
              <rect x="0" y="0" width="100" height="100" fill="url(#g)"/>
            </svg>
            """);

        Assert.InRange(bitmap.At(2, 50).R, 0, 15);
        Assert.InRange(bitmap.At(50, 50).R, 115, 140);
        Assert.InRange(bitmap.At(97, 50).R, 240, 255);
    }

    [Fact]
    public void A_repeated_gradient_starts_again_each_period()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <linearGradient id="g" x2="0.5" spreadMethod="repeat"><stop offset="0" stop-color="#000"/><stop offset="1" stop-color="#fff"/></linearGradient>
              <rect width="100" height="100" fill="url(#g)"/>
            </svg>
            """);

        Assert.InRange(bitmap.At(48, 50).R, 230, 255);
        Assert.InRange(bitmap.At(52, 50).R, 0, 25);
    }

    [Fact]
    public void A_radial_gradient_is_lighter_toward_its_edge()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <radialGradient id="g"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue"/></radialGradient>
              <rect width="100" height="100" fill="url(#g)"/>
            </svg>
            """);

        Rgba center = bitmap.At(50, 50);
        Rgba edge = bitmap.At(50, 2);
        Assert.True(center.R > 230 && center.B < 25, center.ToString());
        Assert.True(edge.B > 230 && edge.R < 25, edge.ToString());
    }

    [Fact]
    public void Stop_opacity_makes_the_gradient_transparent_through_a_soft_mask()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <linearGradient id="g"><stop offset="0" stop-color="blue" stop-opacity="0"/><stop offset="1" stop-color="blue"/></linearGradient>
              <rect width="100" height="100" fill="url(#g)"/>
            </svg>
            """);

        AssertNear(White, bitmap.At(1, 50), 12);
        AssertNear(Blue, bitmap.At(98, 50), 12);
    }

    [Fact]
    public void Group_opacity_composites_the_group_once()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <g opacity="0.5"><rect width="60" height="100" fill="red"/><rect x="40" width="60" height="100" fill="red"/></g>
            </svg>
            """);

        // The overlap is no darker than either rectangle alone.
        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(20, 50), 2);
        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(50, 50), 2);
    }

    [Fact]
    public void Fill_opacity_applies_to_a_single_paint_without_a_group()
    {
        using Bitmap bitmap = Render("""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><rect width="100" height="100" fill="blue" fill-opacity="0.5"/></svg>""");

        AssertNear(new Rgba(128, 128, 255, 255), bitmap.At(50, 50), 2);
    }

    [Fact]
    public void A_clip_path_limits_painting_to_its_shapes()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <clipPath id="c"><circle cx="50" cy="50" r="20"/></clipPath>
              <rect width="100" height="100" fill="red" clip-path="url(#c)"/>
            </svg>
            """);

        AssertNear(Red, bitmap.At(50, 50));
        AssertNear(White, bitmap.At(10, 10));
        AssertNear(White, bitmap.At(50, 85));
    }

    [Fact]
    public void A_clip_path_in_bounding_box_units_scales_with_the_element()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <clipPath id="c" clipPathUnits="objectBoundingBox"><rect width="0.5" height="1"/></clipPath>
              <rect x="20" y="20" width="60" height="60" fill="blue" clip-path="url(#c)"/>
            </svg>
            """);

        AssertNear(Blue, bitmap.At(30, 50));
        AssertNear(White, bitmap.At(70, 50));
    }

    [Fact]
    public void A_luminance_mask_hides_where_it_is_black()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <mask id="m" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><rect width="50" height="100" fill="white"/></mask>
              <rect width="100" height="100" fill="red" mask="url(#m)"/>
            </svg>
            """);

        AssertNear(Red, bitmap.At(25, 50), 2);
        AssertNear(White, bitmap.At(75, 50), 2);
    }

    [Fact]
    public void Use_instantiates_a_symbol_at_its_position()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="100" height="100">
              <defs><symbol id="s" viewBox="0 0 10 10"><rect width="10" height="10" fill="lime"/></symbol></defs>
              <use xlink:href="#s" x="50" y="50" width="20" height="20"/>
            </svg>
            """);

        AssertNear(Lime, bitmap.At(60, 60));
        AssertNear(White, bitmap.At(75, 75));
        AssertNear(White, bitmap.At(40, 40));
    }

    [Fact]
    public void A_style_sheet_class_rule_wins_over_a_presentation_attribute_and_loses_to_the_style_attribute()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <style>.a { fill: blue } g > rect.b { fill: lime }</style>
              <rect class="a" width="30" height="30" fill="red"/>
              <g><rect class="a b" x="40" width="30" height="30"/></g>
              <rect class="a" x="70" width="30" height="30" style="fill: red"/>
            </svg>
            """);

        AssertNear(Blue, bitmap.At(15, 15));
        AssertNear(Lime, bitmap.At(55, 15));
        AssertNear(Red, bitmap.At(85, 15));
    }

    [Fact]
    public void A_pattern_tiles_its_content()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <pattern id="p" patternUnits="userSpaceOnUse" width="20" height="20"><rect width="10" height="20" fill="blue"/></pattern>
              <rect width="100" height="100" fill="url(#p)"/>
            </svg>
            """);

        AssertNear(Blue, bitmap.At(5, 50));
        AssertNear(White, bitmap.At(15, 50));
        AssertNear(Blue, bitmap.At(45, 50));
        AssertNear(White, bitmap.At(55, 50));
    }

    [Fact]
    public void Multiply_blend_mode_darkens_the_backdrop()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <rect width="100" height="100" fill="#ffff00"/>
              <rect width="100" height="100" fill="#00ffff" style="mix-blend-mode: multiply"/>
            </svg>
            """);

        AssertNear(Lime, bitmap.At(50, 50), 2);
    }

    [Fact]
    public void Text_is_drawn_upright_at_its_baseline()
    {
        using Bitmap bitmap = Render("""<svg xmlns="http://www.w3.org/2000/svg" width="200" height="100"><text x="10" y="80" font-size="80" font-family="sans-serif">H</text></svg>""");

        // The stems of an H span the cap height above the baseline; nothing is drawn below it.
        int dark = 0;
        for (int y = 30; y < 78; y++)
        {
            dark += bitmap.At(18, y).R < 100 ? 1 : 0;
        }

        Assert.True(dark > 30, $"{dark} dark pixels in the stem");
        AssertNear(White, bitmap.At(18, 95));
    }

    [Fact]
    public void An_embedded_png_data_url_is_drawn_into_its_box()
    {
        // A 1 x 1 opaque red PNG.
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC";
        using Bitmap bitmap = Render($"""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><image href="data:image/png;base64,{png}" x="0" y="0" width="50" height="50" preserveAspectRatio="none"/></svg>""");

        AssertNear(Red, bitmap.At(25, 25), 2);
        AssertNear(White, bitmap.At(75, 75));
    }

    [Fact]
    public void A_nested_svg_clips_to_its_viewport()
    {
        using Bitmap bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <svg x="10" y="10" width="40" height="40"><rect width="100" height="100" fill="blue"/></svg>
            </svg>
            """);

        AssertNear(Blue, bitmap.At(30, 30));
        AssertNear(White, bitmap.At(70, 70));
    }

    /// <summary>Imports <paramref name="svg"/>, draws it on a page of its size, saves, reopens and renders at 96 DPI.</summary>
    internal static Bitmap Render(string svg)
    {
        using var saved = new MemoryStream();
        using (PdfDocument document = PdfDocument.Create())
        {
            PdfFormXObject form = document.CreateFormXObjectFromSvg(Encoding.UTF8.GetBytes(svg));
            PdfRectangle box = form.BoundingBox!.Value;
            PdfPage page = document.Pages.Add(box);
            using (PdfCanvas canvas = page.CreateCanvas())
            {
                canvas.DrawForm(form);
            }

            document.Pages.RemoveAt(0);
            document.Save(saved);
        }

        using PdfDocument reopened = PdfDocument.Open(saved.ToArray());
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(reopened.Pages[0], new RenderOptions().WithDpi(96));
    }

    private static void AssertNear(Rgba expected, Rgba actual, int tolerance = 1)
    {
        bool near = Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance;
        Assert.True(near, $"Expected {expected} within {tolerance}, got {actual}.");
    }
}
