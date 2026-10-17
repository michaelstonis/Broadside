using System.Text;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Constant alpha and blend modes through the software rasterizer (ISO 32000-2 §11.3, §11.6.3, §11.6.4.4), observed as pixels.
/// Pages are 612 x 792 at 72 DPI, so pixel (x, y) has its centre at user (x + 0.5, 791.5 − y). Expected values come from the
/// compositing formula of §11.3.3 and §11.3.6 and the blend functions of Tables 134 and 135, evaluated here by hand or in
/// <see cref="BlendFunction"/>; colours and alphas pass through 8 bits on the way, so results may move by a level or two.
/// </summary>
public sealed class CompositingTests
{
    private const string States =
        "/ExtGState << /Half << /ca 0.5 >> /QuarterStroke << /CA 0.25 >> /Clear << /ca 0 /CA 0 >> /Multiply << /BM /Multiply >> "
        + "/Difference << /BM /Difference >> >>";

    private static readonly Rgba White = new(255, 255, 255, 255);

    [Fact]
    public void A_fill_at_nonstroking_alpha_one_half_mixes_its_colour_with_the_backdrop()
    {
        using Bitmap bitmap = RenderContent("/Half gs 1 0 0 rg 100 100 200 200 re f");

        // Cr = (1 − 0.5) × white + 0.5 × red: (255, 127.5, 127.5).
        AssertNear(new Rgba(255, 128, 128, 255), bitmap.At(200, 591));
        Assert.Equal(White, bitmap.At(50, 591));
    }

    [Fact]
    public void Strokes_take_the_stroking_alpha_and_fills_the_nonstroking_one()
    {
        using Bitmap bitmap = RenderContent("/QuarterStroke gs 20 w 100 400 m 500 400 l S 100 100 100 100 re f");

        // The stroke: 0.75 × 255 = 191.25; the fill keeps ca = 1 and paints black.
        AssertNear(new Rgba(191, 191, 191, 255), bitmap.At(300, 391));
        Assert.Equal(new Rgba(0, 0, 0, 255), bitmap.At(150, 641));
    }

    [Fact]
    public void Overlapping_translucent_fills_composite_one_after_the_other()
    {
        using Bitmap bitmap = RenderContent("/Half gs 100 100 200 200 re f 200 100 200 200 re f");

        // One coat: 127.5; two: 63.75.
        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(150, 591));
        AssertNear(new Rgba(64, 64, 64, 255), bitmap.At(250, 591));
    }

    [Fact]
    public void A_restore_brings_back_the_opacity_of_its_save()
    {
        using Bitmap bitmap = RenderContent("q /Half gs 100 100 100 100 re f Q 300 100 100 100 re f");

        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(150, 641));
        Assert.Equal(new Rgba(0, 0, 0, 255), bitmap.At(350, 641));
    }

    [Fact]
    public void Alpha_zero_paints_nothing()
    {
        using Bitmap bitmap = RenderContent("/Clear gs 20 w 100 100 200 200 re B /Sh0 sh", "/Shading << /Sh0 5 0 R >>", Shading("[0 0 0]", "[0 0 0]"));

        Assert.Equal(White, bitmap.At(200, 591));
        Assert.Equal(White, bitmap.At(100, 591));
        Assert.Equal(White, bitmap.At(500, 100));
    }

    [Fact]
    public void Images_stencil_masks_and_shadings_take_the_nonstroking_alpha()
    {
        // A 1 x 1 black image, a stencil mask painting black, and an extended black shading under the clip, each at ca 0.5.
        using Bitmap bitmap = RenderContent(
            "/Half gs q 100 0 0 100 50 50 cm BI /W 1 /H 1 /CS /G /BPC 8 ID \u0000 EI Q "
            + "q 100 0 0 100 250 50 cm BI /W 1 /H 1 /IM true ID \u0000 EI Q "
            + "q 450 50 100 100 re W n /Sh0 sh Q",
            "/Shading << /Sh0 5 0 R >>",
            Shading("[0 0 0]", "[0 0 0]"));

        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(100, 691));
        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(300, 691));
        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(500, 691));
    }

    [Fact]
    public void Glyphs_fill_with_the_nonstroking_alpha_and_stroke_with_the_stroking_alpha()
    {
        // TextSamples draws "I" (code 73) as a rectangle of its own size; mode 0 fills, mode 1 strokes.
        string content = "/Half gs /QuarterStroke gs BT /F1 200 Tf 100 300 Td (I) Tj 1 Tr 20 w 200 0 Td (I) Tj ET";
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Encoding.ASCII.GetBytes(content), States));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        Rgba filled = Darkest(bitmap, 100, 300, 441, 441);
        Rgba stroked = Darkest(bitmap, 300, 500, 441, 441);
        AssertNear(new Rgba(128, 128, 128, 255), filled);
        AssertNear(new Rgba(191, 191, 191, 255), stroked);
    }

    [Fact]
    public void A_blend_mode_combines_the_source_with_the_backdrop()
    {
        // Backdrop: (1, 0.5, 0) orange; source (0.5, 1, 1) multiplied: (0.5, 0.5, 0) = (127.5, 127.5, 0). Difference of white over
        // the same backdrop inverts it: (0, 0.5, 1).
        using Bitmap bitmap = RenderContent(
            "1 0.5 0 rg 50 50 500 500 re f q /Multiply gs 0.5 1 1 rg 100 100 100 100 re f Q /Difference gs 1 1 1 rg 300 100 100 100 re f");

        AssertNear(new Rgba(128, 128, 0, 255), bitmap.At(150, 641));
        AssertNear(new Rgba(0, 127, 255, 255), bitmap.At(350, 641));
    }

    [Fact]
    public void Over_a_transparent_backdrop_a_blend_mode_paints_the_source_colour()
    {
        // §11.3.6: with αb = 0, Cr = Cs whatever the blend function; at ca 0.5 the pixel is the source at half alpha (premultiplied).
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(
            Encoding.ASCII.GetBytes("/Multiply gs 1 0 0 rg 100 100 100 100 re f /Half gs 300 100 100 100 re f"), States));
        using var rasterizer = new SoftwareRasterizer();
        PdfPage page = document.Pages[0];
        var options = new RenderOptions();
        PixelSize size = options.GetPixelSize(page);
        using var bitmap = new Bitmap(size.Width, size.Height);

        rasterizer.Render(page.CreateDisplayList(), bitmap, options.GetDeviceTransform(page));

        Assert.Equal(new Rgba(255, 0, 0, 255), bitmap.At(150, 641));
        AssertNear(new Rgba(128, 0, 0, 128), bitmap.At(350, 641));
        Assert.Equal(new Rgba(0, 0, 0, 0), bitmap.At(250, 641));
    }

    [Fact]
    public void A_shading_pattern_composites_with_its_own_extgstate_alpha()
    {
        // §11.6.7: the pattern's ExtGState ca 0.5 applies to its shading; its CA (a stroking parameter) does not.
        using Bitmap bitmap = RenderContent(
            "/Pattern cs /P0 scn 100 100 200 200 re f",
            "/Pattern << /P0 5 0 R >>",
            "<< /Type /Pattern /PatternType 2 /ExtGState << /ca 0.5 /CA 0 >> /Shading " + Shading("[0 0 0]", "[0 0 0]") + " >>");

        AssertNear(new Rgba(128, 128, 128, 255), bitmap.At(200, 591));
    }

    [Fact]
    public void Extgstate_params_pdf_applies_its_alphas_and_blend_modes()
    {
        // GS1: BM Multiply, CA 0.5, ca 0.25 and a luminosity soft mask of 0.5 gray over the whole rectangle (#115); the
        // rectangle 0..20 is black multiplied at 0.25 × 0.5: 0.875 × 255, as poppler gives. Then GS2's blend mode Screen: a black stroke
        // screened over white leaves white.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("extgstate-params.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        AssertNear(new Rgba(223, 223, 223, 255), bitmap.At(10, 781));
        Assert.Equal(White, bitmap.At(20, 766));
    }

    [Fact]
    public void Transparency_blend_modes_pdf_follows_the_compositing_formula_for_every_mode_and_alpha()
    {
        // 16 cells in table order; in each, a horizontal gradient backdrop and the source (0.2, 0.7, 0.9) at ca 1 (y 110..170) and
        // ca 0.5 (y 30..90), the backdrop alone in y 90..110. With αb = 1: Cr = (1 − αs) × Cb + αs × B(Cb, Cs).
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("transparency-blend-modes.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);
        double[] source = [0.2, 0.7, 0.9];

        for (int mode = 0; mode < 16; mode++)
        {
            int left = mode % 4 * 153;
            int bottom = 792 - ((mode / 4) + 1) * 198;
            foreach (int dx in (int[])[25, 60, 95, 128])
            {
                int x = left + dx;
                Rgba backdropPixel = bitmap.At(x, 691 - bottom);
                double[] backdrop = [backdropPixel.R / 255.0, backdropPixel.G / 255.0, backdropPixel.B / 255.0];
                double[] blended = BlendFunction(mode, backdrop, source);
                foreach ((double alpha, int row) in (ReadOnlySpan<(double, int)>)[(1.0, 651 - bottom), (0.5, 731 - bottom)])
                {
                    var expected = new Rgba(
                        Channel(((1 - alpha) * backdrop[0]) + (alpha * blended[0])),
                        Channel(((1 - alpha) * backdrop[1]) + (alpha * blended[1])),
                        Channel(((1 - alpha) * backdrop[2]) + (alpha * blended[2])),
                        255);
                    AssertNear(expected, bitmap.At(x, row), tolerance: 3, $"mode {mode}, x {x}, alpha {alpha}");
                }
            }
        }
    }

    /// <summary>B(Cb, Cs) on RGB, as Tables 134 and 135 define it, for the modes in table order.</summary>
    private static double[] BlendFunction(int mode, double[] cb, double[] cs)
    {
        if (mode >= 12)
        {
            return mode switch
            {
                12 => SetLum(SetSat(cs, Sat(cb)), Lum(cb)),
                13 => SetLum(SetSat(cb, Sat(cs)), Lum(cb)),
                14 => SetLum(cs, Lum(cb)),
                _ => SetLum(cb, Lum(cs)),
            };
        }

        var result = new double[3];
        for (int i = 0; i < 3; i++)
        {
            double b = cb[i];
            double s = cs[i];
            result[i] = mode switch
            {
                0 => s,
                1 => b * s,
                2 => b + s - (b * s),
                3 => HardLight(s, b),
                4 => Math.Min(b, s),
                5 => Math.Max(b, s),
                6 => b == 0 ? 0 : b >= 1 - s ? 1 : b / (1 - s),
                7 => b == 1 ? 1 : 1 - b >= s ? 0 : 1 - ((1 - b) / s),
                8 => HardLight(b, s),
                9 => s <= 0.5 ? b - ((1 - (2 * s)) * b * (1 - b)) : b + (((2 * s) - 1) * ((b <= 0.25 ? ((((16 * b) - 12) * b) + 4) * b : Math.Sqrt(b)) - b)),
                10 => Math.Abs(b - s),
                _ => b + s - (2 * b * s),
            };
        }

        return result;

        static double HardLight(double b, double s) => s <= 0.5 ? b * 2 * s : b + ((2 * s) - 1) - (b * ((2 * s) - 1));
    }

    private static double Lum(double[] c) => (0.3 * c[0]) + (0.59 * c[1]) + (0.11 * c[2]);

    private static double Sat(double[] c) => c.Max() - c.Min();

    private static double[] SetLum(double[] c, double l)
    {
        double d = l - Lum(c);
        double[] result = [c[0] + d, c[1] + d, c[2] + d];
        double lum = Lum(result);
        double n = result.Min();
        double x = result.Max();
        for (int i = 0; i < 3; i++)
        {
            if (n < 0)
            {
                result[i] = lum + ((result[i] - lum) * lum / (lum - n));
            }
        }

        for (int i = 0; i < 3; i++)
        {
            if (x > 1)
            {
                result[i] = lum + ((result[i] - lum) * (1 - lum) / (x - lum));
            }
        }

        return result;
    }

    private static double[] SetSat(double[] c, double s)
    {
        int[] order = [0, 1, 2];
        Array.Sort(order, (a, b) => c[a].CompareTo(c[b]));
        double[] result = new double[3];
        double min = c[order[0]];
        double max = c[order[2]];
        if (max > min)
        {
            result[order[1]] = (c[order[1]] - min) * s / (max - min);
            result[order[2]] = s;
        }

        return result;
    }

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    private static Rgba Darkest(Bitmap bitmap, int left, int right, int top, int bottom)
    {
        Rgba darkest = White;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                Rgba pixel = bitmap.At(x, y);
                if (pixel.R < darkest.R)
                {
                    darkest = pixel;
                }
            }
        }

        return darkest;
    }

    private static string Shading(string c0, string c1) =>
        $"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 612 0] /Extend [true true] /Function << /FunctionType 2 /Domain [0 1] /C0 {c0} /C1 {c1} /N 1 >> >>";

    private static void AssertNear(Rgba expected, Rgba actual, int tolerance = 1, string? where = null)
    {
        bool near = Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance
            && Math.Abs(expected.A - actual.A) <= tolerance;
        Assert.True(near, $"Expected {expected} within {tolerance}, got {actual}{(where is null ? string.Empty : " at " + where)}.");
    }

    private static Bitmap RenderContent(string content, string resources = "", params string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), States + " " + resources, objects));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }
}
