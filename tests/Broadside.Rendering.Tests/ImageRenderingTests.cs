using System.Text;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Images through the software rasterizer (ISO 32000-2 §8.9.4, §8.9.5, §8.9.7): the unit-square mapping, every bit depth, Indexed
/// images, Decode arrays, the codecs, nearest-sample enlargement without Interpolate, bilinear enlargement with it, and box-filter
/// reduction. Every image corpus file paints into the 100 x 100 square at (72, 600) unless noted: device x 72 to 172, y 92 to 192 at
/// 72 DPI on a Letter page. Expected colours come from the corpus README (what poppler shows) or from the spec's arithmetic.
/// </summary>
public sealed class ImageRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Black = new(0, 0, 0, 255);

    [Fact]
    public void A_2_bit_image_shows_its_four_gray_levels()
    {
        using Bitmap bitmap = RenderCorpus("image-2bpc.pdf");

        // 5 x 2 samples: texels 20 x 50 pixels; rows 0 1 2 3 0 and 3 3 2 1 1 -> 0, 85, 170, 255.
        int[][] expected = [[0, 85, 170, 255, 0], [255, 255, 170, 85, 85]];
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 5; column++)
            {
                byte gray = (byte)expected[row][column];
                Assert.Equal(new Rgba(gray, gray, gray, 255), bitmap.At(72 + (20 * column) + 10, 92 + (50 * row) + 25));
            }
        }
    }

    [Fact]
    public void A_4_bit_indexed_image_shows_its_palette_colours()
    {
        using Bitmap bitmap = RenderCorpus("image-4bpc-indexed.pdf");

        Rgba[][] expected =
        [
            [new(0, 255, 0, 255), new(85, 170, 255, 255), new(255, 0, 253, 255)],
            [new(153, 102, 203, 255), new(17, 238, 51, 255), new(238, 17, 202, 255)],
        ];
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Assert.Equal(expected[row][column], bitmap.At(72 + (100 * column / 3) + 16, 92 + (50 * row) + 25));
            }
        }
    }

    [Fact]
    public void A_16_bit_image_uses_both_bytes_of_each_sample()
    {
        using Bitmap bitmap = RenderCorpus("image-16bpc.pdf");

        // poppler shows the high bytes; the full 16-bit value rounds to one of them or a neighbour (0x12FF / 65535 x 255 = 18.9,
        // 0xFF12 / 65535 x 255 = 254.1), which only a reader of both bytes can produce.
        int[][][] expected =
        [
            [[18, 255, 0], [128, 127, 255], [0, 18, 171]],
            [[255, 0, 0], [255, 67, 128], [15, 240, 85]],
        ];
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Rgba pixel = bitmap.At(72 + (100 * column / 3) + 16, 92 + (50 * row) + 25);
                Assert.InRange(pixel.R - expected[row][column][0], -1, 1);
                Assert.InRange(pixel.G - expected[row][column][1], -1, 1);
                Assert.InRange(pixel.B - expected[row][column][2], -1, 1);
            }
        }
    }

    [Fact]
    public void A_decode_array_inverts_the_samples()
    {
        using Bitmap bitmap = RenderCorpus("image-decode-inverted.pdf");

        int[] expected = [255, 170, 85, 0];
        for (int column = 0; column < 4; column++)
        {
            byte gray = (byte)expected[column];
            Assert.Equal(new Rgba(gray, gray, gray, 255), bitmap.At(72 + (25 * column) + 12, 142));
        }
    }

    [Fact]
    public void An_enlarged_image_without_interpolate_has_hard_edges()
    {
        using Bitmap bitmap = RenderCorpus("inline-image.pdf");

        // The 2 x 2 checkerboard (black top left and bottom right) at 50 pixels per sample: the colour changes between two pixels.
        Assert.Equal(Black, bitmap.At(121, 117));
        Assert.Equal(White, bitmap.At(122, 117));
        Assert.Equal(White, bitmap.At(121, 167));
        Assert.Equal(Black, bitmap.At(122, 167));
        Assert.Equal(Black, bitmap.At(72, 92));
        Assert.Equal(Black, bitmap.At(171, 191));
        Assert.Equal(White, bitmap.At(71, 117));
        Assert.Equal(White, bitmap.At(172, 167));
        Assert.Equal(2, DistinctGrays(bitmap, 117, 72, 172));
    }

    [Fact]
    public void An_enlarged_image_with_interpolate_is_smooth()
    {
        using Bitmap bitmap = RenderContent("q 100 0 0 100 72 600 cm BI /W 2 /H 2 /CS /G /BPC 8 /I true ID \u0000ÿÿ\u0000 EI Q");

        // Bilinear between the sample centres (25 and 75 pixels into the square): a ramp across the middle, flat near the sides.
        Rgba left = bitmap.At(121, 117);
        Rgba right = bitmap.At(122, 117);
        Assert.InRange(left.R, 110, 145);
        Assert.InRange(right.R, 110, 145);
        Assert.True(left.R < right.R);
        Assert.Equal(Black, bitmap.At(80, 100));
        Assert.True(DistinctGrays(bitmap, 117, 72, 172) > 40);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_reduced_image_averages_the_samples_under_each_pixel(bool interpolate)
    {
        // 200 x 2 columns alternating black and white, shown 100 x 100: each pixel covers two columns exactly.
        var samples = new StringBuilder();
        for (int i = 0; i < 400; i++)
        {
            samples.Append(i % 2 == 0 ? '\u0000' : 'ÿ');
        }

        string image = ContentSamples.Stream(
            samples.ToString(),
            "/Type /XObject /Subtype /Image /Width 200 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8" + (interpolate ? " /Interpolate true" : string.Empty));
        using Bitmap bitmap = RenderContent("q 100 0 0 100 72 600 cm /Im0 Do Q", "/XObject << /Im0 5 0 R >>", image);

        for (int x = 72; x < 172; x++)
        {
            Assert.InRange(bitmap.At(x, 140).R, 126, 129);
        }
    }

    [Fact]
    public void A_rotated_image_follows_the_unit_square_mapping()
    {
        // A 2 x 1 image, black then white, under a quarter turn: x of the unit square points up, so the first sample (black) lands at
        // the bottom (§8.9.4: image space [1/w 0 0 -1/h 0 1], then the CTM).
        using Bitmap bitmap = RenderContent("q 0 100 -100 0 172 600 cm BI /W 2 /H 1 /CS /G /BPC 8 ID \u0000ÿ EI Q");

        Assert.Equal(Black, bitmap.At(122, 167));
        Assert.Equal(White, bitmap.At(122, 117));
        Assert.Equal(Black, bitmap.At(73, 191));
    }

    [Fact]
    public void The_edges_of_an_image_are_anti_aliased_like_a_filled_square()
    {
        // A black 1 x 1 image from x = 10.5 to 20.5: pixels 10 and 20 are half covered (§10.7.4), 11 to 19 fully; the same square
        // filled as a path below it gets the same pixels.
        using Bitmap bitmap = RenderContent("q 10 0 0 10 10.5 400 cm BI /W 1 /H 1 /CS /G /BPC 8 ID \u0000 EI Q 0 g 10.5 300 10 10 re f");

        Assert.InRange(bitmap.At(10, 387).R, 126, 129);
        Assert.Equal(Black, bitmap.At(15, 387));
        Assert.InRange(bitmap.At(20, 387).R, 126, 129);
        Assert.Equal(White, bitmap.At(21, 387));
        for (int x = 8; x < 23; x++)
        {
            Assert.Equal(bitmap.At(x, 487), bitmap.At(x, 387));
        }
    }

    [Fact]
    public void An_image_with_a_singular_transform_paints_nothing()
    {
        using Bitmap bitmap = RenderContent("q 100 0 0 0 72 600 cm BI /W 1 /H 1 /CS /G /BPC 8 ID \u0000 EI Q");

        for (int y = 0; y < bitmap.Height; y++)
        {
            Assert.True(bitmap.GetRow(y).IndexOfAnyExcept((byte)0xFF) < 0, $"row {y} is not white");
        }
    }

    [Theory]
    [InlineData("dct-baseline.pdf", "Im1", 300, 600, 148, 84)]
    [InlineData("jbig2-generic.pdf", "Im0", 72, 600, 100, 100)]
    [InlineData("ccitt-g4.pdf", "Im0", 72, 600, 100, 100)]
    public void Codec_images_show_their_decoded_samples(string fileName, string name, int x, int y, int width, int height)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        PdfImage image = document.Pages[0].GetImage(name)!;
        using DecodedImage decoded = image.Decode()!;
        ImageDecodeMap map = image.CreateDecodeMap(decoded);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        // Where samples are enlarged (dct-baseline: 4 x 4 pixels per sample), the pixel at a sample's centre shows it; where they are
        // reduced (jbig2-generic 1.5 and ccitt-g4 17 samples per pixel across), the area is not blank.
        int top = 792 - y - height;
        if (decoded.Width <= width)
        {
            Span<byte> row = stackalloc byte[decoded.Width];
            for (int sy = 0; sy < decoded.Height; sy += 3)
            {
                ImageRows.Unpack(decoded.GetRow(sy), decoded.StorageBits, decoded.Width, row);
                for (int sx = 0; sx < decoded.Width; sx += 3)
                {
                    double px = x + ((sx + 0.5) * width / decoded.Width);
                    double py = top + ((sy + 0.5) * height / decoded.Height);
                    if (Math.Abs(px - Math.Floor(px) - 0.5) > 0.3 || Math.Abs(py - Math.Floor(py) - 0.5) > 0.3)
                    {
                        continue;
                    }

                    byte expected = (byte)Math.Round(map.Map(0, row[sx]) * 255);
                    Assert.Equal(new Rgba(expected, expected, expected, 255), bitmap.At((int)px, (int)py));
                }
            }
        }
        else
        {
            int dark = 0;
            for (int py = top; py < top + height; py++)
            {
                for (int px = x; px < x + width; px++)
                {
                    dark += bitmap.At(px, py).R < 250 ? 1 : 0;
                }
            }

            Assert.True(dark > 100, $"{dark} pixels are darker than white");
        }
    }

    private static int DistinctGrays(Bitmap bitmap, int y, int from, int to)
    {
        var grays = new HashSet<byte>();
        for (int x = from; x < to; x++)
        {
            grays.Add(bitmap.At(x, y).R);
        }

        return grays.Count;
    }

    private static Bitmap RenderCorpus(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }

    private static Bitmap RenderContent(string content, string resources = "", params string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), resources, objects));
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0]);
    }
}
