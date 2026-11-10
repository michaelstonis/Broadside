using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Issue #377: the platform-neutral half of the WIC image decoder, which runs on every host: the reduction a size WIC offers
/// delivers, the YCbCr planes put back together with chroma replicated as the managed decoder replicates it, and WIC's BGR or gray
/// rows repacked, a plane one sample short completed by repeating its last sample.
/// </summary>
public sealed class WicSamplesTests
{
    [Theory]
    [InlineData(2550, 3150, 2, 638, 788, 2)] // ⌈W/4⌉, ⌈H/4⌉: the reduction asked for
    [InlineData(2550, 3150, 2, 637, 787, 2)] // ⌊W/4⌋, ⌊H/4⌋: one sample short, completed
    [InlineData(2550, 3150, 3, 1275, 1575, 1)] // a finer reduction than asked for
    [InlineData(2550, 3150, 3, 2550, 3150, 0)] // the full size
    [InlineData(2550, 3150, 1, 638, 788, -1)] // coarser than allowed
    [InlineData(2550, 3150, 3, 700, 700, -1)] // no power of two
    [InlineData(75, 43, 3, 10, 6, 3)]
    public void The_reduction_delivered_is_read_from_the_size_WIC_offers(int width, int height, int requested, int offeredWidth, int offeredHeight, int expected) =>
        Assert.Equal(expected, WicSamples.DeliveredReduction(width, height, requested, offeredWidth, offeredHeight));

    [Theory]
    [InlineData(75, 75, 0)]
    [InlineData(75, 38, 1)]
    [InlineData(75, 37, 1)]
    [InlineData(75, 19, 2)]
    [InlineData(76, 38, 1)]
    [InlineData(75, 25, -1)]
    [InlineData(75, 0, -1)]
    public void The_chroma_subsampling_is_read_from_the_plane_sizes(int luma, int chroma, int expected) =>
        Assert.Equal(expected, WicSamples.ChromaShift(luma, chroma));

    [Fact]
    public void Planes_are_put_together_with_chroma_replicated_and_the_managed_transform()
    {
        // A 5 x 3 image, 4:2:0: chroma planes of 3 x 2, each chroma sample covering 2 x 2 luma samples (the last ones partly).
        const int width = 5, height = 3;
        byte[] y = new byte[width * height];
        for (int i = 0; i < y.Length; i++)
        {
            y[i] = (byte)(20 + (i * 13));
        }

        byte[] cb = [60, 128, 200, 90, 128, 30];
        byte[] cr = [240, 128, 10, 128, 70, 180];
        using DecodedImageBuilder image = Request(width, height, 3).CreateImage(0);

        Assert.True(WicSamples.ConvertPlanes(image, y, new WicSamples.Plane(width, height, width), cb, cr, new WicSamples.Plane(3, 2, 3), new byte[3 * width]));

        byte[] expected = new byte[3];
        for (int row = 0; row < height; row++)
        {
            for (int x = 0; x < width; x++)
            {
                int c = ((row >> 1) * 3) + (x >> 1);
                YCbCrConversion.ToRgb([y[(row * width) + x]], [cb[c]], [cr[c]], expected);
                Assert.Equal(expected, image.GetRow(row).Slice(x * 3, 3).ToArray());
            }
        }
    }

    [Fact]
    public void Neutral_chroma_gives_gray_and_the_transform_is_libjpegs()
    {
        byte[] rgb = new byte[6];
        YCbCrConversion.ToRgb([77, 200], [128, 85], [128, 255], rgb);

        // Y 200, Cb 85, Cr 255 in libjpeg's 16-bit fixed point (jdcolor.c): R = 200 + 178 clamps to 255; G and B as computed here.
        Assert.Equal<byte>([77, 77, 77], rgb[..3]);
        Assert.Equal(255, rgb[3]);
        Assert.Equal(200 + ((-22554 * (85 - 128)) + 32768 + (-46802 * (255 - 128)) >> 16), rgb[4]);
        Assert.Equal(200 + (((116130 * (85 - 128)) + 32768) >> 16), rgb[5]);
    }

    [Fact]
    public void Planes_one_sample_short_are_completed_with_their_last_sample()
    {
        // WIC rounding a reduced size down: a 3 x 2 luma plane for a 4 x 3 image.
        byte[] y = [10, 20, 30, 40, 50, 60];
        byte[] chroma = [128, 128, 128, 128];
        using DecodedImageBuilder image = Request(4, 3, 3).CreateImage(0);

        Assert.True(WicSamples.ConvertPlanes(image, y, new WicSamples.Plane(3, 2, 3), chroma, chroma, new WicSamples.Plane(2, 1, 2), new byte[12]));

        Assert.Equal<byte>([10, 10, 10, 20, 20, 20, 30, 30, 30, 30, 30, 30], image.GetRow(0)[..12].ToArray());
        Assert.Equal(image.GetRow(1)[..12].ToArray(), image.GetRow(2)[..12].ToArray());
        Assert.Equal(60, image.GetRow(2)[9]);
    }

    [Fact]
    public void Planes_that_do_not_fit_the_image_are_refused()
    {
        using DecodedImageBuilder image = Request(8, 8, 3).CreateImage(0);
        byte[] plane = new byte[64];

        Assert.False(WicSamples.ConvertPlanes(image, plane, new WicSamples.Plane(6, 8, 6), plane, plane, new WicSamples.Plane(3, 4, 3), new byte[24]));
        Assert.False(WicSamples.ConvertPlanes(image, plane, new WicSamples.Plane(8, 8, 8), plane, plane, new WicSamples.Plane(3, 3, 3), new byte[24]));
    }

    [Fact]
    public void BGR_rows_are_repacked_as_RGB_and_completed()
    {
        byte[] bgr = [1, 2, 3, 4, 5, 6, 0, 0, 7, 8, 9, 10, 11, 12, 0, 0]; // 2 x 2 pixels, stride 8
        using DecodedImageBuilder image = Request(3, 3, 3).CreateImage(0);

        Assert.True(WicSamples.CopyPixels(image, bgr, new WicSamples.Plane(2, 2, 8)));

        Assert.Equal<byte>([3, 2, 1, 6, 5, 4, 6, 5, 4], image.GetRow(0)[..9].ToArray());
        Assert.Equal<byte>([9, 8, 7, 12, 11, 10, 12, 11, 10], image.GetRow(1)[..9].ToArray());
        Assert.Equal(image.GetRow(1)[..9].ToArray(), image.GetRow(2)[..9].ToArray());
    }

    [Fact]
    public void Gray_rows_are_copied()
    {
        byte[] gray = [1, 2, 3, 4, 5, 6];
        using DecodedImageBuilder image = Request(3, 2, 1).CreateImage(0);

        Assert.True(WicSamples.CopyPixels(image, gray, new WicSamples.Plane(3, 2, 3)));

        Assert.Equal<byte>([1, 2, 3], image.GetRow(0)[..3].ToArray());
        Assert.Equal<byte>([4, 5, 6], image.GetRow(1)[..3].ToArray());
        Assert.False(WicSamples.CopyPixels(image, gray, new WicSamples.Plane(1, 2, 1)));
    }

    private static PlatformImageRequest Request(int width, int height, int components) => new()
    {
        Filter = new CosName("DCTDecode"),
        Width = width,
        Height = height,
        Components = components,
        BitsPerComponent = 8,
        ColorTransform = components == 3,
    };
}
