using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Region and tile rendering (viewer capability 1, spec #33 story 14): a region of a page, or a tile rendered from a cached display
/// list under the page's device transform moved by the tile's offset, has the pixels of the whole page's render at the same scale.
/// </summary>
public sealed class TileRenderingTests
{
    [Fact]
    public void A_four_by_four_mosaic_of_256_pixel_regions_equals_the_whole_page_byte_for_byte()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-fill-rules.pdf"));
        PdfPage page = document.Pages[0];
        using var rasterizer = new SoftwareRasterizer();
        const double scale = 3.5;

        // 1400 x 1050 pixels: the mosaic covers the top-left 1024 x 1024, which holds parts of every shape.
        using Bitmap whole = rasterizer.Render(page, new RenderOptions().WithScale(scale));
        Assert.Equal((1400, 1050), (whole.Width, whole.Height));
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                // Pixel (x, y) is page point (x / 3.5, 300 - y / 3.5).
                var region = new PdfRectangle(256 * column / scale, 300 - (256 * (row + 1) / scale), 256 * (column + 1) / scale, 300 - (256 * row / scale));
                RenderOptions options = new RenderOptions().WithScale(scale).WithRegion(region);

                using Bitmap tile = rasterizer.Render(page, options);

                Assert.Equal(new PixelRectangle(256 * column, 256 * row, 256, 256), options.GetPixelRectangle(page));
                AssertSamePixels(whole, tile, 256 * column, 256 * row, 0);
            }
        }
    }

    [Theory]
    [InlineData(0.77)]
    [InlineData(1.7)]
    [InlineData(4.123)]
    public void Tiles_at_three_zoom_levels_render_from_one_display_list_with_the_pixels_of_the_whole_page(double scale)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-fill-rules.pdf"));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        using var rasterizer = new SoftwareRasterizer();
        var options = new RenderOptions().WithScale(scale);
        using Bitmap whole = rasterizer.Render(page, options);

        RenderMosaic(rasterizer, list, options.GetDeviceTransform(page), whole, 256, maxDifference: 0);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_corpus_page_rendered_as_tiles_matches_its_whole_render(string fileName, int pageIndex)
    {
        // Tiles skip every paint whose bounds miss them; a bound too small would leave a hole. Away from the arithmetic of
        // paths-fill-rules.pdf, a tile's transform rounds differently from the page's in the last bit, which can move a pixel's
        // coverage across a rounding boundary: one level at most.
        using PdfDocument document = GoldenCorpus.Open(fileName);
        PdfPage page = document.Pages[pageIndex];
        DisplayList list = page.CreateDisplayList();
        using var rasterizer = new SoftwareRasterizer();
        var options = new RenderOptions().WithScale(1.7);
        using Bitmap whole = rasterizer.Render(page, options);

        RenderMosaic(rasterizer, list, options.GetDeviceTransform(page), whole, 96, maxDifference: 1);
    }

    public static TheoryData<string, int> Pages => GoldenCorpus.Pages;

    [Fact]
    public void A_region_is_cut_to_the_page_and_rounded_outwards_to_whole_pixels()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-fill-rules.pdf"));
        PdfPage page = document.Pages[0];

        // At scale 2, page x 10.2..20.3 is pixels 20.4..40.6, so 20..41; y 290..400 is above the page's top (300) down to pixel 20.
        RenderOptions options = new RenderOptions().WithScale(2).WithRegion(new PdfRectangle(10.2, 290, 20.3, 400));

        Assert.Equal(new PixelRectangle(20, 0, 21, 20), options.GetPixelRectangle(page));
        Assert.Equal(new PixelSize(21, 20), options.GetPixelSize(page));
        Assert.Equal(new Matrix(2, 0, 0, -2, -20, 600), options.GetDeviceTransform(page));
        Assert.Throws<ArgumentException>(() => options.WithRegion(new PdfRectangle(500, 0, 600, 10)).GetPixelSize(page));
    }

    [Fact]
    public void A_region_of_a_rotated_page_covers_the_pixels_its_corners_map_to()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-fill-rules.pdf"));
        PdfPage page = document.Pages[0];
        using var rasterizer = new SoftwareRasterizer();
        RenderOptions whole = new RenderOptions().WithRotation(90);
        using Bitmap wholeBitmap = rasterizer.Render(page, whole);

        // Turned clockwise, page point (x, y) lands on pixel (y, x): the region 100..250 x 50..120 is pixels 50..120 x 100..250.
        RenderOptions options = new RenderOptions().WithRotation(90).WithRegion(new PdfRectangle(100, 50, 250, 120));
        using Bitmap region = rasterizer.Render(page, options);

        Assert.Equal(new PixelRectangle(50, 100, 70, 150), options.GetPixelRectangle(page));
        AssertSamePixels(wholeBitmap, region, 50, 100, 0);
    }

    [Fact]
    public void Without_a_region_the_pixel_rectangle_is_the_whole_page()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-fill-rules.pdf"));

        Assert.Equal(new PixelRectangle(0, 0, 600, 450), new RenderOptions().WithScale(1.5).GetPixelRectangle(document.Pages[0]));
    }

    private static void RenderMosaic(SoftwareRasterizer rasterizer, DisplayList list, Matrix device, Bitmap whole, int size, int maxDifference)
    {
        for (int y = 0; y < whole.Height; y += size)
        {
            for (int x = 0; x < whole.Width; x += size)
            {
                using var tile = new Bitmap(Math.Min(size, whole.Width - x), Math.Min(size, whole.Height - y));
                tile.Pixels.Fill(0xFF);

                rasterizer.Render(list, tile, device * Matrix.CreateTranslation(-x, -y));

                AssertSamePixels(whole, tile, x, y, maxDifference);
            }
        }
    }

    private static void AssertSamePixels(Bitmap whole, Bitmap tile, int left, int top, int maxDifference)
    {
        for (int y = 0; y < tile.Height; y++)
        {
            ReadOnlySpan<byte> expected = whole.GetRow(top + y).Slice(left * 4, tile.Width * 4);
            ReadOnlySpan<byte> actual = tile.GetRow(y)[..(tile.Width * 4)];
            if (maxDifference == 0)
            {
                Assert.True(expected.SequenceEqual(actual), $"tile at ({left}, {top}) differs from the whole render in row {y}");
                continue;
            }

            for (int index = 0; index < actual.Length; index++)
            {
                int difference = Math.Abs(expected[index] - actual[index]);
                Assert.True(difference <= maxDifference, $"tile at ({left}, {top}) pixel ({(index / 4) + left}, {y + top}) channel {index % 4} differs by {difference}");
            }
        }
    }
}
