using Broadside.Images.Import;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// The TIFF, GIF and BMP readers allocate per image, not per row (CLAUDE.md, "Code conventions"; issue #642): strips, tiles, rows
/// and planes are pooled, so reading a tall image allocates no more than reading a short one. <c>ImageImportBenchmarks</c> is the
/// measuring half.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ImageImportAllocationTests
{
    [Theory]
    [InlineData(1, 1, 3, 8, 2)]
    [InlineData(5, 2, 3, 8, 2)]
    [InlineData(8, 2, 3, 16, 2)]
    [InlineData(32773, 1, 1, 8, 1)]
    [InlineData(5, 1, 4, 8, 2)]
    public void Reading_a_tiff_page_allocates_the_same_whatever_the_height(int compression, int predictor, int samples, int bits, int photometric)
    {
        byte[] small = Tiff(64, compression, predictor, samples, bits, photometric);
        byte[] large = Tiff(1024, compression, predictor, samples, bits, photometric);

        long forSmall = Allocations.Measure(() => ReadTiff(small));
        long forLarge = Allocations.Measure(() => ReadTiff(large));

        Assert.InRange(forLarge, 0, forSmall);
    }

    [Fact]
    public void Reading_ccitt_data_that_passes_through_allocates_only_its_copy_more_per_height()
    {
        byte[] small = Tiff(64, 4, 1, 1, 1, 0);
        byte[] large = Tiff(1024, 4, 1, 1, 1, 0);

        long forSmall = Allocations.Measure(() => ReadTiff(small));
        long forLarge = Allocations.Measure(() => ReadTiff(large));

        Assert.InRange(forLarge - forSmall, 0, large.Length - small.Length + 64);
    }

    [Fact]
    public void Reading_a_gif_allocates_the_same_whatever_the_height()
    {
        byte[] small = Gif(64);
        byte[] large = Gif(1024);

        long forSmall = Allocations.Measure(() => ReadGif(small));
        long forLarge = Allocations.Measure(() => ReadGif(large));

        Assert.InRange(forLarge, 0, forSmall);
    }

    [Fact]
    public void Reading_a_bmp_allocates_the_same_whatever_the_height()
    {
        byte[] small = ImportSamples.Bmp24(128, 64, ImportSamples.RgbGradient(128, 64));
        byte[] large = ImportSamples.Bmp24(128, 1024, ImportSamples.RgbGradient(128, 1024));

        long forSmall = Allocations.Measure(() => ReadBmp(small));
        long forLarge = Allocations.Measure(() => ReadBmp(large));

        Assert.InRange(forLarge, 0, forSmall);
    }

    internal static byte[] Tiff(int height, int compression, int predictor, int samples, int bits, int photometric)
    {
        byte[] data = ImportSamples.Pack(128, height, bits, samples, (x, y, c) => bits == 1 ? (ImportSamples.Black(x, y) ? 1 : 0) : ((x * 3) + (y * 5) + (c * 40)) & ((1 << bits) - 1));
        return ImportSamples.Tiff(new TiffPage(128, height, samples, bits, photometric, data)
        {
            Compression = compression,
            Predictor = predictor,
            ExtraSamples = samples == 4 ? [2] : null,
        });
    }

    internal static byte[] Gif(int height)
    {
        byte[] indices = new byte[128 * height];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (byte)((i / 7) % 16);
        }

        return ImportSamples.Gif(128, height, indices, new byte[48]);
    }

    private static void ReadTiff(byte[] tiff)
    {
        List<long> pages = TiffReader.ListPages(tiff, out bool little);
        using ImportedImage image = TiffReader.ReadPage(tiff, pages[0], little, long.MaxValue);
    }

    private static void ReadGif(byte[] gif)
    {
        using ImportedImage image = GifReader.Read(gif, long.MaxValue);
    }

    private static void ReadBmp(byte[] bmp)
    {
        using ImportedImage image = BmpReader.Read(bmp, long.MaxValue);
    }
}
