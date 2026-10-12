using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Renders corpus pages end to end through the software rasterizer (spec story 11): display list, page mapping, scan conversion and
/// colour, observed as the pixels of the bitmap. Expected colours and positions come from the corpus README (what poppler shows) and
/// the spec's geometry, not from the rasterizer.
/// </summary>
public sealed class PageRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Red = new(255, 0, 0, 255);
    private static readonly Rgba Green = new(0, 255, 0, 255);

    [Theory]
    [InlineData(72, 612, 792)]
    [InlineData(150, 1275, 1650)]
    public void An_empty_page_renders_white_at_the_size_of_its_crop_box(double dpi, int width, int height)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(dpi));

        Assert.Equal(width, bitmap.Width);
        Assert.Equal(height, bitmap.Height);
        for (int y = 0; y < bitmap.Height; y++)
        {
            Assert.True(bitmap.GetRow(y).IndexOfAnyExcept((byte)0xFF) < 0, $"row {y} is not white");
        }
    }

    [Fact]
    public void The_non_zero_rule_fills_the_centre_of_a_pentagram_and_the_even_odd_rule_leaves_it_empty()
    {
        using Bitmap bitmap = RenderCorpus("paths-fill-rules.pdf");

        // Image y = 300 - user y. (A) f: centre pentagon has winding 2, inside. (B) f*: centre crossed twice, outside; a point is inside.
        Assert.Equal(Red, bitmap.At(65, 75));
        Assert.Equal(White, bitmap.At(195, 75));
        Assert.Equal(new Rgba(0, 0, 255, 255), bitmap.At(195, 30));
    }

    [Fact]
    public void Nested_subpaths_fill_by_their_winding_numbers()
    {
        using Bitmap bitmap = RenderCorpus("paths-fill-rules.pdf");

        // (C) clockwise inner circle in a counterclockwise one, f: a hole. (D) same direction, f: solid. (E) same direction, f*: a ring.
        Assert.Equal(White, bitmap.At(325, 75));
        Assert.NotEqual(White, bitmap.At(285, 75));
        Assert.Equal(new Rgba(0, 153, 0, 255), bitmap.At(65, 230));
        Assert.Equal(new Rgba(0, 153, 0, 255), bitmap.At(25, 230));
        Assert.Equal(White, bitmap.At(195, 230));
        Assert.Equal(new Rgba(77, 77, 77, 255), bitmap.At(150, 230));
    }

    [Fact]
    public void Open_subpaths_are_closed_curves_follow_the_transform_and_a_trailing_moveto_paints_nothing()
    {
        using Bitmap bitmap = RenderCorpus("paths-fill-rules.pdf");
        var purple = new Rgba(128, 0, 128, 255);

        // (F) under a 30 degree rotation about (330, 75): the open triangle's centroid (-20, -15) maps to (320.2, 52.0), the lens's
        // inside (35, 0) to (360.3, 92.5); the trailing moveto at the local origin (330, 75) lies outside both.
        Assert.Equal(purple, bitmap.At(320, 300 - 52));
        Assert.Equal(purple, bitmap.At(360, 300 - 93));
        Assert.Equal(White, bitmap.At(330, 300 - 75));
    }

    [Fact]
    public void A_pixel_half_covered_by_an_edge_gets_half_the_colour()
    {
        // Black rectangle from x = 10.5 to 20.5: pixels 10 and 20 are half covered (§10.7.4, analytic coverage), 11 to 19 fully.
        using Bitmap bitmap = RenderContent("0 0 0 rg 10.5 0 10 792 re f");

        Assert.Equal(White, bitmap.At(9, 400));
        Assert.InRange(bitmap.At(10, 400).R, 126, 129);
        Assert.Equal(new Rgba(0, 0, 0, 255), bitmap.At(11, 400));
        Assert.Equal(new Rgba(0, 0, 0, 255), bitmap.At(19, 400));
        Assert.InRange(bitmap.At(20, 400).R, 126, 129);
        Assert.Equal(White, bitmap.At(21, 400));
    }

    [Fact]
    public void A_shape_covering_a_quarter_of_a_pixel_gives_it_a_quarter_of_the_colour()
    {
        // The pixel (100, 400) spans user x 100..101, y 391..392; the rectangle covers x 100.5..101, y 391.5..392 of it.
        using Bitmap bitmap = RenderContent("0 0 0 rg 100.5 391.5 0.5 0.5 re f");

        Assert.InRange(bitmap.At(100, 400).R, 190, 193);
        Assert.Equal(White, bitmap.At(101, 400));
        Assert.Equal(White, bitmap.At(100, 399));
    }

    [Fact]
    public void A_degenerate_subpath_paints_the_device_pixel_under_its_point()
    {
        // §8.5.3.3.1: a subpath of one point encloses the single device pixel under it: (100.3, 792 - 200.7) is in pixel (100, 591).
        using Bitmap bitmap = RenderContent("0 0 0 rg 100.3 200.7 m h f 300 300 m 300 300 l f");

        Assert.Equal(new Rgba(0, 0, 0, 255), bitmap.At(100, 591));
        Assert.Equal(White, bitmap.At(101, 591));
        Assert.Equal(White, bitmap.At(100, 590));
        Assert.Equal(new Rgba(0, 0, 0, 255), bitmap.At(300, 492));
    }

    [Fact]
    public void Shapes_far_beyond_the_page_are_clipped_without_losing_what_they_cover()
    {
        // A rectangle reaching 10^10 units in every direction; a curve from (0, 0) to (612, 0) bulging 7.5e8 units up, which
        // crosses the bottom edge at about x = -92 and x = 704 (B(t) ≈ (−3t·10^9, 3t·10^9) near t = 0) and so covers the page; a
        // triangle wholly left of the page, whose edges cancel on the left border.
        using Bitmap covered = RenderContent("0 0 1 rg -10000000000 -10000000000 20000000000 20000000000 re f");
        using Bitmap curve = RenderContent("1 0 0 rg 0 0 m -1000000000 1000000000 1000000000 1000000000 612 0 c f");
        using Bitmap left = RenderContent("0 0 1 rg -5000 700 m -4000 700 l -4000 800 l f");

        for (int y = 0; y < covered.Height; y += 97)
        {
            for (int x = 0; x < covered.Width; x += 89)
            {
                Assert.Equal(new Rgba(0, 0, 255, 255), covered.At(x, y));
                Assert.Equal(Red, curve.At(x, y));
                Assert.Equal(White, left.At(x, y));
            }
        }

        Assert.Equal(Red, curve.At(0, 791));
        Assert.Equal(Red, curve.At(611, 0));
        Assert.Equal(White, left.At(0, 42));
    }

    [Theory]
    [InlineData(0, 100, 200, 0, 0, 99, 199)]
    [InlineData(1, 200, 100, 199, 0, 0, 99)]
    [InlineData(2, 100, 200, 99, 199, 0, 0)]
    public void Rotated_pages_turn_clockwise_with_their_size(int page, int width, int height, int redX, int redY, int greenX, int greenY)
    {
        // page-rotation.pdf: red square at the user-space origin, green in the opposite corner of a 200 x 100 page, unrotated red is
        // bottom left. Rotate 90 puts it top left, 180 top right, 270 bottom right (clockwise, §7.7.3.3 Table 31).
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("page-rotation.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[page]);

        Assert.Equal((width, height), (bitmap.Width, bitmap.Height));
        Assert.Equal(Red, bitmap.At(redX, redY));
        Assert.Equal(Green, bitmap.At(greenX, greenY));
        Assert.Equal(White, bitmap.At(width / 2, height / 2));
    }

    [Fact]
    public void The_user_unit_scales_the_page_and_the_crop_box_cuts_it()
    {
        // Page 4: CropBox [20 10 180 90], UserUnit 2: 160 x 80 units of 2 points, 320 x 160 pixels at 72 DPI. The red square
        // (0..40, 0..40) shows as crop x 0..20, y 0..30 (pixels 0..40 across, rows 100..160); the green one (160..200, 60..100) as
        // crop x 140..160, y 50..80 (pixels 280..320, rows 0..60).
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("page-rotation.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[3]);

        Assert.Equal((320, 160), (bitmap.Width, bitmap.Height));
        Assert.Equal(Red, bitmap.At(0, 159));
        Assert.Equal(Red, bitmap.At(39, 100));
        Assert.Equal(White, bitmap.At(40, 100));
        Assert.Equal(White, bitmap.At(39, 99));
        Assert.Equal(Green, bitmap.At(280, 0));
        Assert.Equal(Green, bitmap.At(319, 59));
        Assert.Equal(White, bitmap.At(279, 59));
    }

    [Fact]
    public void An_extra_rotation_and_scale_add_to_the_page_rotation()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("page-rotation.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        RenderOptions options = new RenderOptions().WithScale(2).WithRotation(-90);

        // Page 1 (Rotate 90) turned back by 90: the unrotated page, twice the size; red bottom left.
        using Bitmap bitmap = rasterizer.Render(document.Pages[0], options);

        Assert.Equal(new PixelSize(400, 200), options.GetPixelSize(document.Pages[0]));
        Assert.Equal((400, 200), (bitmap.Width, bitmap.Height));
        Assert.Equal(Red, bitmap.At(0, 199));
        Assert.Equal(Red, bitmap.At(79, 120));
        Assert.Equal(White, bitmap.At(80, 120));
        Assert.Equal(Green, bitmap.At(399, 0));
    }

    [Fact]
    public void Colours_convert_through_the_engines_colour_management()
    {
        // A colour management that turns every colour blue: a red fill must come out blue, so the rasterizer converts through it.
        using PdfDocument document = PdfDocument.Open(
            ContentSamples.OnePage(Encoding.ASCII.GetBytes("1 0 0 rg 0 0 612 792 re f")),
            new PdfOptions().UseColorManagement(new BlueColorManagement()));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        Assert.Equal(new Rgba(0, 0, 255, 255), bitmap.At(300, 400));
    }

    [Fact]
    public void A_page_renders_into_caller_memory_and_leaves_the_rest_of_it_alone()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("page-rotation.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        const int width = 120;
        const int height = 210;
        const int stride = (width * 4) + 16;
        byte[] memory = new byte[stride * height];
        memory.AsSpan().Fill(0x11);
        using var bitmap = new Bitmap(memory, width, height, stride);

        rasterizer.Render(document.Pages[0], bitmap, new RenderOptions());

        Assert.Equal(Red, bitmap.At(0, 0));
        Assert.Equal(White, bitmap.At(99, 100));
        Assert.Equal(new Rgba(0x11, 0x11, 0x11, 0x11), bitmap.At(100, 100));
        Assert.Equal(new Rgba(0x11, 0x11, 0x11, 0x11), bitmap.At(0, 200));
        Assert.True(memory.AsSpan((width * 4) + (stride * 50), 16).IndexOfAnyExcept((byte)0x11) < 0, "the padding after a row was written");
        Assert.Throws<ArgumentException>(() => rasterizer.Render(document.Pages[1], bitmap, new RenderOptions()));
    }

    [Fact]
    public void A_display_list_draws_over_a_transparent_bitmap_without_a_background()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("page-rotation.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        PdfPage page = document.Pages[1];
        var options = new RenderOptions();
        PixelSize size = options.GetPixelSize(page);
        using var bitmap = new Bitmap(size.Width, size.Height);

        rasterizer.Render(page.CreateDisplayList(), bitmap, options.GetDeviceTransform(page));

        Assert.Equal(new Rgba(0, 0, 0, 0), bitmap.At(100, 50));
        Assert.Equal(Red, bitmap.At(199, 0));
    }

    private static Bitmap RenderCorpus(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }

    private static Bitmap RenderContent(string content)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes(content)));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }

    /// <summary>Converts every colour of every space to RGB blue.</summary>
    private sealed class BlueColorManagement : IColorManagement
    {
        public IColorConverter CreateConverter(PdfColorSpace source, ColorConversion conversion) => new Blue(source.ComponentCount);

        private sealed class Blue(int inputs) : IColorConverter
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
