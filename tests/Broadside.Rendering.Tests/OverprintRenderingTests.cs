using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Overprint preview (ISO 32000-2 §8.6.7, §11.7.4.5 Table 146) through the software rasterizer, observed as pixels. Pages are
/// 612 x 792 at 72 DPI, so pixel (x, y) has its centre at user (x + 0.5, 791.5 − y). Every test paints a backdrop square
/// (100..300, 400..600) and an overlay square (200..400, 300..500); they overlap in (200..300, 400..500). The expected colour of a
/// pixel is the CMYK (and spot) mixture Table 146 gives, converted for display the way the preview converts its colourants: each
/// spot colourant adds its tint times its process equivalent (at tint 1) to the process colourants, clipped at 1, and the result
/// goes through the document's DeviceCMYK converter.
/// </summary>
public sealed class OverprintRenderingTests
{
    private const string Resources =
        "/ExtGState << /Knock << /OP false /op false >> /Op0 << /OP true /op true /OPM 0 >> /Op1 << /OP true /op true /OPM 1 >>"
        + " /Stroke << /OP true /op false /OPM 1 >> /Half << /OP true /op true /OPM 1 /ca 0.5 >> >>"
        + " /ColorSpace << /Orange [/Separation /BroadsideOrange /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0]"
        + " /C1 [0 0.6 1 0] /N 1 >>] /Cyan [/Separation /Cyan /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0]"
        + " /C1 [1 0 0 0] /N 1 >>] /MG [/DeviceN [/Magenta /BroadsideGreen] /DeviceCMYK 5 0 R] >>";

    private const string MgTint = "<< /FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1 0 1] /Length 24 >>\nstream\n{dup 0.8 mul 3 1 roll 0}\nendstream";

    private static readonly Rgba White = new(255, 255, 255, 255);

    // Pixels: backdrop only, overlay only and the overlap.
    private static readonly (int X, int Y) Backdrop = (150, 241);
    private static readonly (int X, int Y) Overlay = (350, 441);
    private static readonly (int X, int Y) Both = (250, 341);

    [Fact]
    public void Overprint_preview_is_off_by_default_and_the_overlay_knocks_out_the_backdrop()
    {
        Assert.False(new RenderOptions().OverprintPreview);
        using Rendered page = Render("1 0 0 0 k", "/Op1 gs 0 1 0 0 k", preview: false);

        Assert.Equal(page.Cmyk(0, 1, 0, 0), page.At(Both));
    }

    [Fact]
    public void With_OPM_1_a_zero_DeviceCMYK_component_leaves_the_backdrop_colourant_unchanged()
    {
        using Rendered page = Render("1 0 0 0 k", "/Op1 gs 0 1 0 0 k");

        Assert.Equal(page.Cmyk(1, 0, 0, 0), page.At(Backdrop));
        Assert.Equal(page.Cmyk(0, 1, 0, 0), page.At(Overlay));
        Assert.Equal(page.Cmyk(1, 1, 0, 0), page.At(Both));
    }

    [Fact]
    public void With_OPM_1_a_DeviceCMYK_colour_of_all_zero_components_paints_nothing()
    {
        using Rendered page = Render("0.2 0.8 0.4 0 k", "/Op1 gs 0 0 0 0 k");

        Assert.Equal(page.Cmyk(0.2, 0.8, 0.4, 0), page.At(Both));
        Assert.Equal(White, page.At(Overlay));
    }

    [Fact]
    public void With_OPM_1_only_the_zero_components_keep_the_backdrop()
    {
        using Rendered page = Render("0.6 0 0.3 0 k", "/Op1 gs 0 0.5 0 0.2 k");

        Assert.Equal(page.Cmyk(0.6, 0.5, 0.3, 0.2), page.At(Both));
    }

    [Fact]
    public void With_OPM_0_every_DeviceCMYK_component_replaces_its_colourant_even_when_zero()
    {
        using Rendered page = Render("1 0 0 0 k", "/Op0 gs 0 1 0 0 k");

        Assert.Equal(page.Cmyk(0, 1, 0, 0), page.At(Both));
    }

    [Fact]
    public void Without_overprint_a_spot_colour_erases_the_process_colourants_and_with_it_adds_to_them()
    {
        using Rendered knocked = Render("1 0 0 0 k", "/Knock gs /Orange cs 1 scn");
        using Rendered overprinted = Render("1 0 0 0 k", "/Op0 gs /Orange cs 1 scn");

        // BroadsideOrange's process equivalent at tint 1 is 0 0.6 1 0.
        Assert.Equal(knocked.Cmyk(0, 0.6, 1, 0), knocked.At(Both));
        Assert.Equal(overprinted.Cmyk(0, 0.6, 1, 0), overprinted.At(Overlay));
        Assert.Equal(overprinted.Cmyk(1, 0.6, 1, 0), overprinted.At(Both));
    }

    [Fact]
    public void A_process_colour_that_overprints_leaves_a_spot_colourant_below_it()
    {
        // DeviceCMYK with OPM 0 replaces all four process colourants, not the spot; DeviceRGB too (converted to 1 1 0 0).
        using Rendered cmyk = Render("/Orange cs 1 scn", "/Op0 gs 0 1 0 0 k");
        using Rendered rgb = Render("/Orange cs 0.8 scn", "/Op1 gs 0 0 1 rg");
        using Rendered knocked = Render("/Orange cs 1 scn", "/Knock gs 0 1 0 0 k");

        Assert.Equal(cmyk.Cmyk(0, 1, 1, 0), cmyk.At(Both));
        Assert.Equal(rgb.Cmyk(1, 1, 0.8, 0), rgb.At(Both));
        Assert.Equal(knocked.Cmyk(0, 1, 0, 0), knocked.At(Both));
    }

    [Fact]
    public void A_Separation_or_DeviceN_colourant_named_like_a_process_colourant_marks_that_process_colourant()
    {
        using Rendered separation = Render("0 1 0 0 k", "/Op0 gs /Cyan cs 1 scn");
        using Rendered deviceN = Render("1 0 0 0 k", "/Op0 gs /MG cs 0.5 1 scn");

        Assert.Equal(separation.Cmyk(1, 1, 0, 0), separation.At(Both));

        // Magenta 0.5 and BroadsideGreen 1, whose process equivalent is 0.8 0 1 0; cyan 1 below stays.
        Assert.Equal(deviceN.Cmyk(0.8, 0.5, 1, 0), deviceN.At(Overlay));
        Assert.Equal(deviceN.Cmyk(1, 0.5, 1, 0), deviceN.At(Both));
    }

    [Fact]
    public void Strokes_overprint_by_OP_and_other_paints_by_op()
    {
        using Rendered page = Render("1 0 0 0 k", "/Stroke gs 0 1 0 0 k 200 300 200 200 re f", shape: "0 0 1 0 K 20 w 250 300 m 250 600 l S");

        Assert.Equal(page.Cmyk(0, 1, 0, 0), page.At((350, 341)));
        Assert.Equal(page.Cmyk(1, 0, 1, 0), page.At((250, 241)));
        Assert.Equal(page.Cmyk(0, 1, 1, 0), page.At((250, 341)));
    }

    [Fact]
    public void A_translucent_overprinting_paint_mixes_only_the_colourants_it_marks()
    {
        using Rendered page = Render("1 0 0 0 k", "/Half gs 0 1 0 0 k");

        Assert.Equal(page.Cmyk(1, 128 / 255.0, 0, 0), page.At(Both));
    }

    [Fact]
    public void Overprint_parameters_are_restored_with_the_graphics_state()
    {
        using Rendered page = Render("1 0 0 0 k", "q /Op1 gs Q 0 1 0 0 k");

        Assert.Equal(page.Cmyk(0, 1, 0, 0), page.At(Both));
    }

    [Fact]
    public void Annotations_are_drawn_over_the_preview_of_the_content()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        RenderOptions content = new RenderOptions().WithOverprintPreview(true).WithLayers(DisplayLayers.Content);
        RenderOptions all = new RenderOptions().WithOverprintPreview(true);

        using Bitmap withoutAnnotations = rasterizer.Render(document.Pages[0], content);
        using Bitmap withAnnotations = rasterizer.Render(document.Pages[0], all);
        using Bitmap plain = rasterizer.Render(document.Pages[0], new RenderOptions());

        Assert.False(withoutAnnotations.Pixels.SequenceEqual(withAnnotations.Pixels));

        // Where an annotation covers the page opaquely it looks as it does without the preview.
        bool any = false;
        for (int y = 0; y < plain.Height; y++)
        {
            for (int x = 0; x < plain.Width; x++)
            {
                if (withAnnotations.At(x, y) != withoutAnnotations.At(x, y) && plain.At(x, y) == withAnnotations.At(x, y))
                {
                    any = true;
                }
            }
        }

        Assert.True(any);
    }

    private static Rendered Render(string backdrop, string overlay, bool preview = true, string shape = "200 300 200 200 re f")
    {
        string content = $"q {backdrop} 100 400 200 200 re f Q q {overlay} {shape} Q";
        return new Rendered(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), Resources, MgTint), preview);
    }

    /// <summary>A rendered page and the document's colour converters, to compute expected pixels.</summary>
    private sealed class Rendered : IDisposable
    {
        private readonly PdfDocument _document;
        private readonly Bitmap _bitmap;

        public Rendered(byte[] file, bool preview)
        {
            _document = PdfDocument.Open(file);
            using var rasterizer = new SoftwareRasterizer();
            _bitmap = rasterizer.Render(_document.Pages[0], new RenderOptions().WithOverprintPreview(preview));
        }

        public Bitmap Bitmap => _bitmap;

        public Rgba At((int X, int Y) point) => _bitmap.At(point.X, point.Y);

        /// <summary>The pixel a CMYK mixture shows: the document's DeviceCMYK conversion of the 8-bit colourant values.</summary>
        public Rgba Cmyk(double c, double m, double y, double k)
        {
            if (c == 0 && m == 0 && y == 0 && k == 0)
            {
                return White;
            }

            Span<byte> cmyk = [Byte(c), Byte(m), Byte(y), Byte(k)];
            Span<byte> rgb = stackalloc byte[3];
            _document.GetColorConverter(PdfDeviceCmykColorSpace.Instance).Convert(cmyk, rgb, 1);
            return new Rgba(rgb[0], rgb[1], rgb[2], 255);
        }

        public void Dispose()
        {
            _bitmap.Dispose();
            _document.Dispose();
        }

        private static byte Byte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
