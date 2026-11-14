using System.Text;
using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Smart dark mode (<see cref="ColorFilter.DarkMode"/>, spec #33 viewer capability 9): the lightness of text and vector colors is
/// inverted with their hue kept, images are left as they are, and the white paper turns black. Pages are Letter at 72 DPI, so device
/// y is 792 minus user y.
/// </summary>
public sealed class ColorFilterRenderingTests
{
    private static readonly Rgba Black = new(0, 0, 0, 255);

    [Fact]
    public void Dark_mode_on_image_smask_pdf_leaves_the_image_pixels_identical_to_light_mode_and_inverts_the_page_around_them()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("image-smask.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap light = rasterizer.Render(document.Pages[0]);
        using Bitmap dark = rasterizer.Render(document.Pages[0], new RenderOptions().WithColorFilter(ColorFilter.DarkMode));

        // The soft-masked image covers the 100 x 100 square at (72, 600): device x 72 to 172, y 92 to 192.
        int imagePixels = 0;
        for (int y = 0; y < light.Height; y++)
        {
            for (int x = 0; x < light.Width; x++)
            {
                bool image = x is >= 72 and < 172 && y is >= 92 and < 192;
                Rgba expected = image ? light.At(x, y) : Invert(light.At(x, y));
                Assert.Equal(expected, dark.At(x, y));
                imagePixels += image && light.At(x, y) != new Rgba(255, 255, 255, 255) ? 1 : 0;
            }
        }

        Assert.True(imagePixels > 1000, "the image shows through its soft mask");
        Assert.Equal(Black, dark.At(300, 400));
    }

    [Fact]
    public void Dark_mode_inverts_the_lightness_of_vector_colors_keeps_their_hue_and_leaves_images_and_what_is_drawn_over_them()
    {
        using PdfDocument document = PdfDocument.Open(MixedPage());
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap light = rasterizer.Render(document.Pages[0]);
        using Bitmap dark = rasterizer.Render(document.Pages[0], new RenderOptions().WithColorFilter(ColorFilter.DarkMode));

        Assert.Equal(Black, dark.At(550, 100)); // the paper
        Assert.Equal(new Rgba(127, 127, 255, 255), dark.At(50, 742)); // dark blue (0, 0, 128) becomes light blue
        Assert.Equal(new Rgba(255, 0, 0, 255), dark.At(150, 742)); // a saturated red keeps its lightness of one half
        Assert.Equal(new Rgba(191, 191, 191, 255), dark.At(250, 742)); // gray 64 becomes 191
        Assert.Equal(new Rgba(127, 127, 255, 255), dark.At(475, 467)); // a stencil mask paints a color, filtered like a fill

        // The image keeps its samples, and so does the black square drawn over it.
        Assert.Equal(light.At(320, 442), dark.At(320, 442));
        Assert.Equal(light.At(380, 442), dark.At(380, 442));
        Assert.Equal(new Rgba(200, 30, 30, 255), dark.At(320, 400));
        Assert.Equal(Black, dark.At(350, 452));
    }

    [Theory]
    [InlineData("paths-fill-rules.pdf")]
    [InlineData("color-operators.pdf")]
    [InlineData("text-render-modes.pdf")]
    public void Without_images_every_pixel_is_the_light_pixel_with_its_lightness_inverted(string file)
    {
        using PdfDocument document = GoldenCorpus.Open(file);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap light = rasterizer.Render(document.Pages[0]);
        using Bitmap dark = rasterizer.Render(document.Pages[0], new RenderOptions().WithColorFilter(ColorFilter.DarkMode));

        for (int y = 0; y < light.Height; y++)
        {
            for (int x = 0; x < light.Width; x++)
            {
                Assert.Equal(Invert(light.At(x, y)), dark.At(x, y));
            }
        }
    }

    [Fact]
    public void Inverting_the_lightness_twice_gives_back_every_premultiplied_pixel()
    {
        using PdfDocument document = PdfDocument.Open(MixedPage());
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap light = rasterizer.Render(document.Pages[0], new RenderOptions().WithTransparentBackground(true));
        using Bitmap twice = rasterizer.Render(document.Pages[0], new RenderOptions().WithTransparentBackground(true));
        using PdfDocument empty = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        DisplayList list = empty.Pages[0].CreateDisplayList();

        // An empty list draws nothing, so each filtered render only inverts what the bitmap held.
        rasterizer.ColorFilter = ColorFilter.DarkMode;
        rasterizer.Render(list, twice, Matrix.Identity);
        Assert.NotEqual(light.Pixels.ToArray(), twice.Pixels.ToArray());
        rasterizer.Render(list, twice, Matrix.Identity);

        Assert.Equal(light.Pixels.ToArray(), twice.Pixels.ToArray());
        Assert.Equal(0, twice.At(550, 100).A); // transparent stays transparent
    }

    [Fact]
    public void A_cached_display_list_drawn_over_white_paper_with_the_filter_equals_the_filtered_page_render_whole_or_sliced()
    {
        using PdfDocument document = PdfDocument.Open(MixedPage());
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(0.5);
        DisplayList list = page.CreateDisplayList();
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(page, new RenderOptions().WithScale(0.5).WithColorFilter(ColorFilter.DarkMode));
        PixelSize size = options.GetPixelSize(page);
        using var whole = new Bitmap(size.Width, size.Height);
        using var sliced = new Bitmap(size.Width, size.Height);
        using var unfiltered = new Bitmap(size.Width, size.Height);
        whole.Pixels.Fill(byte.MaxValue);
        sliced.Pixels.Fill(byte.MaxValue);
        unfiltered.Pixels.Fill(byte.MaxValue);

        rasterizer.ColorFilter = ColorFilter.DarkMode;
        rasterizer.Render(list, whole, options.GetDeviceTransform(page));
        using (ProgressiveRender render = rasterizer.BeginRender(list, sliced, options.GetDeviceTransform(page), CancellationToken.None, null))
        {
            while (!render.Continue(TimeSpan.Zero))
            {
            }
        }

        rasterizer.ColorFilter = ColorFilter.None;
        rasterizer.Render(list, unfiltered, options.GetDeviceTransform(page));

        Assert.Equal(expected.Pixels.ToArray(), whole.Pixels.ToArray());
        Assert.Equal(expected.Pixels.ToArray(), sliced.Pixels.ToArray());
        Assert.Equal(new Rgba(255, 255, 255, 255), unfiltered.At(size.Width - 2, 10)); // the same list, drawn again without it
    }

    [Fact]
    public void The_default_render_is_unchanged_and_overprint_preview_ignores_the_filter()
    {
        using PdfDocument document = PdfDocument.Open(MixedPage());
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap plain = rasterizer.Render(document.Pages[0]);
        using Bitmap none = rasterizer.Render(document.Pages[0], new RenderOptions().WithColorFilter(ColorFilter.None));
        using Bitmap preview = rasterizer.Render(document.Pages[0], new RenderOptions().WithOverprintPreview(true));
        using Bitmap darkPreview = rasterizer.Render(document.Pages[0], new RenderOptions().WithOverprintPreview(true).WithColorFilter(ColorFilter.DarkMode));

        Assert.Equal(ColorFilter.None, new RenderOptions().ColorFilter);
        Assert.Equal(plain.Pixels.ToArray(), none.Pixels.ToArray());
        Assert.Equal(preview.Pixels.ToArray(), darkPreview.Pixels.ToArray());
    }

    /// <summary>The lightness inversion of a premultiplied pixel: c + α − max − min per channel.</summary>
    internal static Rgba Invert(Rgba pixel)
    {
        int k = pixel.A - Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
        return new Rgba((byte)(pixel.R + k), (byte)(pixel.G + k), (byte)(pixel.B + k), pixel.A);
    }

    /// <summary>
    /// Squares of dark blue, red and gray along the bottom; a 2 x 1 RGB image over device x 300 to 400, y 392 to 492 with a black square
    /// drawn over it at x 340 to 360, y 442 to 462; and a dark blue 1 x 1 stencil mask at x 450 to 500, y 442 to 492.
    /// </summary>
    internal static byte[] MixedPage()
    {
        var content = new StringBuilder()
            .Append("0 0 0.5 rg 0 0 100 100 re f 1 0 0 rg 100 0 100 100 re f 0.25 g 200 0 100 100 re f\n")
            .Append("q 100 0 0 100 300 300 cm BI /W 2 /H 1 /CS /RGB /BPC 8 ID ")
            .Append(Encoding.Latin1.GetString([200, 30, 30, 30, 30, 200]))
            .Append("\nEI Q\n")
            .Append("0 g 340 330 20 20 re f\n")
            .Append("0 0 0.5 rg q 50 0 0 50 450 300 cm BI /W 1 /H 1 /IM true /BPC 1 ID ")
            .Append(Encoding.Latin1.GetString([0]))
            .Append("\nEI Q\n");
        return ContentSamples.OnePage(Encoding.Latin1.GetBytes(content.ToString()));
    }
}
