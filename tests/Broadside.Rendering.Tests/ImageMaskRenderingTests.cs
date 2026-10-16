using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Masked images through the software rasterizer (ISO 32000-2 §8.9.6, §11.6.5): stencil masks paint the fill colour, explicit and
/// colour key masks punch holes, soft masks and JPEG 2000 opacity composite, Matte is removed before compositing, and a mask is
/// resampled from its own resolution. The corpus images paint into the 100 x 100 square at (72, 600) unless noted: device x 72 to
/// 172, y 92 to 192 at 72 DPI on a Letter page. Expected values come from the corpus generator's inputs and the spec's arithmetic.
/// </summary>
public sealed class ImageMaskRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);

    // tests/Corpus/generate.py STENCIL_ARROW: 1 paints (Decode [1 0]).
    private static readonly int[][] Arrow =
    [
        [0, 0, 0, 1, 0, 0, 0, 0],
        [0, 0, 0, 1, 1, 0, 0, 0],
        [1, 1, 1, 1, 1, 1, 0, 0],
        [1, 1, 1, 1, 1, 1, 1, 0],
        [1, 1, 1, 1, 1, 1, 0, 0],
        [0, 0, 0, 1, 1, 0, 0, 0],
        [0, 0, 0, 1, 0, 0, 0, 0],
        [0, 0, 0, 0, 0, 0, 0, 0],
    ];

    [Fact]
    public void A_stencil_mask_paints_the_fill_colour_where_its_samples_say_and_leaves_the_backdrop_elsewhere()
    {
        using Bitmap bitmap = RenderCorpus("image-stencil-mask.pdf");

        // 8 x 8 samples of 12.5 pixels, painted red (§8.9.6.2).
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Rgba expected = Arrow[y][x] == 1 ? new Rgba(255, 0, 0, 255) : White;
                Assert.Equal(expected, bitmap.At((int)(72 + (12.5 * x) + 6), (int)(92 + (12.5 * y) + 6)));
            }
        }
    }

    [Fact]
    public void Stencil_masks_of_either_polarity_paint_the_same_pixels()
    {
        // ccitt-blackis1-mask.pdf: the same bitmap in red at device y 128 to 192 (BlackIs1 true, Decode [1 0]) and in blue 80 pixels
        // below (the defaults); x 72 to 264. Red (255, g, g) above matches blue (g, g, 255) below pixel for pixel.
        using Bitmap bitmap = RenderCorpus("ccitt-blackis1-mask.pdf");

        int painted = 0;
        for (int y = 128; y < 192; y++)
        {
            for (int x = 72; x < 264; x++)
            {
                Rgba red = bitmap.At(x, y);
                Rgba blue = bitmap.At(x, y + 80);
                Assert.Equal((byte)255, red.R);
                Assert.Equal(red.G, red.B);
                Assert.Equal(new Rgba(red.G, red.G, 255, 255), blue);
                painted += red.G < 128 ? 1 : 0;
            }
        }

        Assert.InRange(painted, 100, (192 - 72) * 64);
    }

    [Fact]
    public void An_explicit_mask_shows_the_image_inside_it_at_the_mask_s_own_resolution()
    {
        // A 4 x 4 image (25 pixels a sample) under a 16 x 16 disc mask (6.25 pixels a sample; 0 inside paints, §8.9.6.3). A mask
        // resampled to the image's 4 x 4 would make each 25-pixel block all image or all backdrop; the disc cuts the corner blocks.
        using Bitmap bitmap = RenderCorpus("image-explicit-mask.pdf");

        for (int my = 0; my < 16; my++)
        {
            for (int mx = 0; mx < 16; mx++)
            {
                bool inside = ((mx - 7.5) * (mx - 7.5)) + ((my - 7.5) * (my - 7.5)) <= 49;
                int x = mx / 4;
                int y = my / 4;
                Rgba expected = inside ? new Rgba((byte)(60 * x), (byte)(60 * y), (byte)(255 - (16 * ((4 * y) + x))), 255) : White;
                Assert.Equal(expected, bitmap.At((int)(72 + (6.25 * mx) + 3), (int)(92 + (6.25 * my) + 3)));
            }
        }
    }

    [Fact]
    public void An_explicit_mask_of_the_image_s_size_masks_sample_by_sample()
    {
        // A 2 x 1 black image with a 2 x 1 mask 0 1 (§8.9.6.3: 0 shows the image): the left half black, the right the backdrop.
        using Bitmap bitmap = RenderContent(
            "q 100 0 0 100 72 600 cm /Im0 Do Q",
            "/XObject << /Im0 5 0 R >>",
            ContentSamples.Stream("\u0000\u0000", "/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask 6 0 R"),
            ContentSamples.Stream("@", "/Type /XObject /Subtype /Image /Width 2 /Height 1 /ImageMask true"));

        Assert.Equal(new Rgba(0, 0, 0, 255), bitmap.At(100, 140));
        Assert.Equal(White, bitmap.At(150, 140));
    }

    [Fact]
    public void Colour_key_masking_leaves_the_backdrop_where_every_component_is_in_its_range()
    {
        // tests/Corpus/generate.py COLOR_KEY_PIXELS with Mask [250 255 0 5 0 5] over a blue square (§8.9.6.4).
        (int R, int G, int B)[][] pixels =
        [
            [(255, 0, 0), (252, 3, 1), (249, 0, 0), (255, 6, 0)],
            [(0, 0, 255), (255, 0, 0), (0, 255, 0), (250, 5, 5)],
            [(128, 128, 128), (0, 0, 0), (255, 255, 255), (251, 2, 4)],
            [(10, 20, 30), (40, 50, 60), (255, 0, 6), (250, 0, 0)],
        ];
        using Bitmap bitmap = RenderCorpus("image-color-key-mask.pdf");

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                (int r, int g, int b) = pixels[y][x];
                bool keyed = r >= 250 && g <= 5 && b <= 5;
                Rgba expected = keyed ? new Rgba(0, 0, 255, 255) : new Rgba((byte)r, (byte)g, (byte)b, 255);
                Assert.Equal(expected, bitmap.At(72 + (25 * x) + 12, 92 + (25 * y) + 12));
            }
        }
    }

    [Fact]
    public void A_soft_mask_of_another_size_composites_each_pixel_with_its_own_opacity()
    {
        // A 4 x 4 image (25 pixels a sample) with a 2 x 8 soft mask of alpha 32 y + 16 x (50 x 12.5 pixels a sample), over white
        // (§11.6.5.2): each pixel is a·c + (1 − a)·255, with the opacity of the mask sample under it, not of the image sample.
        using Bitmap bitmap = RenderCorpus("image-smask.pdf");

        for (int my = 0; my < 8; my++)
        {
            for (int mx = 0; mx < 2; mx++)
            {
                foreach (int dx in (int[])[6, 31])
                {
                    int px = 72 + (50 * mx) + dx;
                    int py = (int)(92 + (12.5 * my) + 6);
                    int x = (px - 72) / 25;
                    int y = (py - 92) / 25;
                    int alpha = (32 * my) + (16 * mx);
                    int[] color = [60 * x, 60 * y, 255 - (16 * ((4 * y) + x))];
                    Rgba pixel = bitmap.At(px, py);
                    byte[] actual = [pixel.R, pixel.G, pixel.B];
                    for (int c = 0; c < 3; c++)
                    {
                        double expected = (alpha / 255.0 * color[c]) + ((1 - (alpha / 255.0)) * 255);
                        Assert.InRange(actual[c], expected - 1, expected + 1);
                    }
                }
            }
        }
    }

    [Fact]
    public void Matte_is_removed_before_compositing_so_the_result_matches_the_colours_before_pre_blending()
    {
        // tests/Corpus/generate.py MATTE_ORIGINAL and MATTE_ALPHA: the image holds c' = m + a (c − m) against Matte [1 1 1]
        // (§11.6.5.2, Table 144). Drawn on a transparent bitmap, a premultiplied pixel is a·c of the original colour c; drawing the
        // pre-blended colours as they are would give a·c' (for a = 64 and c = (0 128 255): 48 56 64 instead of 0 32 64).
        (int R, int G, int B)[] original = [(10, 20, 30), (0, 128, 255), (200, 100, 50), (40, 80, 160)];
        int[] alphas = [0, 64, 128, 255];
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("image-smask-matte.pdf"));
        using Bitmap transparent = RenderTransparent(document.Pages[0]);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap onWhite = rasterizer.Render(document.Pages[0]);

        for (int i = 0; i < 4; i++)
        {
            int px = 72 + (50 * (i % 2)) + 25;
            int py = 92 + (50 * (i / 2)) + 25;
            int a = alphas[i];
            (int r, int g, int b) = original[i];
            Rgba premultiplied = transparent.At(px, py);
            Assert.Equal((byte)a, premultiplied.A);
            AssertNear(a * r / 255.0, premultiplied.R, 1);
            AssertNear(a * g / 255.0, premultiplied.G, 1);
            AssertNear(a * b / 255.0, premultiplied.B, 1);

            Rgba white = onWhite.At(px, py);
            AssertNear((a * r / 255.0) + 255 - a, white.R, 1);
            AssertNear((a * g / 255.0) + 255 - a, white.G, 1);
            AssertNear((a * b / 255.0) + 255 - a, white.B, 1);
        }
    }

    [Fact]
    public void Matte_on_an_indexed_image_is_removed_from_the_table_s_colours()
    {
        // (0 128 255) pre-blended against white at alpha 64 is (191 223 255), the table's only colour (§11.6.5.2: the matte is in
        // the base space, the table's colours are the values it applies to). Premultiplied: 64/255 x (0 128 255) = (0 32 64).
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(
            Encoding.Latin1.GetBytes("q 100 0 0 100 72 600 cm /Im0 Do Q"),
            "/XObject << /Im0 5 0 R >>",
            ContentSamples.Stream("\u0000", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace [/Indexed /DeviceRGB 0 <BFDFFF>] /SMask 6 0 R"),
            ContentSamples.Stream("@", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Matte [1 1 1]")));
        using Bitmap bitmap = RenderTransparent(document.Pages[0]);

        Rgba pixel = bitmap.At(120, 140);
        Assert.Equal((byte)64, pixel.A);
        AssertNear(0, pixel.R, 1);
        AssertNear(32, pixel.G, 1);
        AssertNear(64, pixel.B, 1);
    }

    [Fact]
    public void Opacity_in_JPEG_2000_data_composites_through_the_resolved_colour_space()
    {
        // jpx-smask-in-data.pdf: 15 x 11 samples, no ColorSpace (the codestream's sRGB resolves to DeviceRGB), SMaskInData 1 with
        // the opacity channel jpx_sample(x, y, 3) (§7.4.9, §8.9.5.1 Table 87). On a transparent bitmap each sample shows a·c with alpha a.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("jpx-smask-in-data.pdf"));
        using Bitmap bitmap = RenderTransparent(document.Pages[0]);

        for (int y = 0; y < 11; y++)
        {
            for (int x = 0; x < 15; x++)
            {
                Rgba pixel = bitmap.At((int)(72 + ((x + 0.5) * 100 / 15)), (int)(92 + ((y + 0.5) * 100 / 11)));
                int a = JpxSamples.Sample(x, y, 3, 8);
                Assert.Equal((byte)a, pixel.A);
                AssertNear(a * JpxSamples.Sample(x, y, 0, 8) / 255.0, pixel.R, 1);
                AssertNear(a * JpxSamples.Sample(x, y, 1, 8) / 255.0, pixel.G, 1);
                AssertNear(a * JpxSamples.Sample(x, y, 2, 8) / 255.0, pixel.B, 1);
            }
        }
    }

    [Theory]
    [InlineData("/SMask 6 0 R", "/ColorSpace /DeviceGray /BitsPerComponent 8", false)]
    [InlineData("/Mask 6 0 R", "/ImageMask true", true)]
    public void A_mask_finer_than_the_device_is_averaged_over_each_pixel(string reference, string maskEntries, bool oneBit)
    {
        // A black 1 x 1 image under a 200 x 2 mask of alternate open and closed columns, drawn 100 pixels wide: every pixel covers
        // one open and one closed column, so the image shows at half opacity everywhere; a mask decoded at the image's size, or
        // sampled at one point per pixel, would be all open or all closed.
        var mask = new StringBuilder();
        for (int row = 0; row < 2; row++)
        {
            if (oneBit)
            {
                mask.Append(new string('U', 25));
            }
            else
            {
                for (int x = 0; x < 200; x++)
                {
                    mask.Append(x % 2 == 0 ? 'ÿ' : '\u0000');
                }
            }
        }

        using Bitmap bitmap = RenderContent(
            "q 100 0 0 100 72 600 cm /Im0 Do Q",
            "/XObject << /Im0 5 0 R >>",
            ContentSamples.Stream("\u0000", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 " + reference),
            ContentSamples.Stream(mask.ToString(), "/Type /XObject /Subtype /Image /Width 200 /Height 2 " + maskEntries));

        for (int x = 73; x < 171; x++)
        {
            Assert.InRange(bitmap.At(x, 140).R, 126, 129);
        }
    }

    [Fact]
    public void A_masked_image_is_clipped_like_any_paint()
    {
        // The clip is the left half of the image's square (§8.5.4): the stencil arrow shows only there.
        using Bitmap bitmap = RenderContent(
            "72 600 50 100 re W n 1 0 0 rg q 100 0 0 100 72 600 cm BI /W 8 /H 8 /IM true /D [1 0] ID \u0010\u0018üþü\u0018\u0010\u0000 EI Q");

        Assert.Equal(new Rgba(255, 0, 0, 255), bitmap.At(72 + 6, 92 + 31));
        Assert.Equal(White, bitmap.At(72 + 75, 92 + 43));
        Assert.Equal(new Rgba(255, 0, 0, 255), bitmap.At(72 + 37, 92 + 43));
    }

    private static void AssertNear(double expected, byte actual, double tolerance) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);

    private static Bitmap RenderCorpus(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }

    private static Bitmap RenderTransparent(PdfPage page)
    {
        var options = new RenderOptions();
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        rasterizer.Render(page.CreateDisplayList(), bitmap, device);
        return bitmap;
    }

    private static Bitmap RenderContent(string content, string resources = "", params string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), resources, objects));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }
}
