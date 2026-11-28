using Broadside.Graphics;
using Broadside.Graphics.Colors;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// <see cref="PdfDocument.CreateImageFromBmp"/> (issue #642) over the BMP files of <c>Images/Import</c>: 1, 4, 8, 16, 24 and 32 bits,
/// RLE4 and RLE8, bit fields with alpha, top-down rows.
/// </summary>
public sealed class BmpImportTests
{
    [Theory]
    [InlineData("bmp-4.bmp", 37, 23, 16, 4, 17)]
    [InlineData("bmp-8.bmp", 40, 30, 256, 8, 1)]
    [InlineData("bmp-8-topdown.bmp", 40, 30, 256, 8, 1)]
    [InlineData("bmp-rle4.bmp", 37, 23, 16, 4, 17)]
    public void Indexed_bitmaps_become_indexed_images(string file, int width, int height, int entries, int bits, int step)
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromBmp(ImportTesting.File(file));

        Assert.IsType<PdfIndexedColorSpace>(image.ColorSpace);
        Assert.Equal(bits, image.BitsPerComponent);
        Assert.Equal(ImportTesting.Expected(width, height, 1, (x, y, _) => ImportSamples.Index(x, y, entries)), ImportTesting.Samples(image));
        byte[] lookup = ImportTesting.Lookup(image);
        (int r, int g, int b) = ImportSamples.PaletteRgb(5 * step);
        Assert.Equal([(byte)r, (byte)g, (byte)b], lookup[15..18]);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Run_length_8_bitmaps_decode()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromBmp(ImportTesting.File("bmp-rle8.bmp"));

        Assert.Equal(ImportTesting.Expected(40, 30, 1, (x, y, _) => ((x / 6) + y) % 256), ImportTesting.Samples(image));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void One_bit_bitmaps_keep_their_table()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromBmp(ImportTesting.File("bmp-1.bmp"));

        Assert.Equal(1, image.BitsPerComponent);
        Assert.Equal(ImportTesting.Expected(45, 21, 1, (x, y, _) => ImportSamples.Black(x, y) ? 1 : 0), ImportTesting.Samples(image));
        Assert.Equal([255, 255, 255, 0, 0, 0], ImportTesting.Lookup(image));
    }

    [Theory]
    [InlineData("bmp-24.bmp")]
    [InlineData("bmp-32.bmp")]
    public void True_color_bitmaps_become_rgb(string file)
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromBmp(ImportTesting.File(file));

        Assert.IsType<PdfDeviceRgbColorSpace>(image.ColorSpace);
        Assert.Equal(PdfImageMaskKind.None, image.MaskKind);
        Assert.Equal(ImportTesting.Rgb8(67, 45), ImportTesting.Samples(image));
    }

    [Fact]
    public void An_alpha_mask_becomes_a_soft_mask()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromBmp(ImportTesting.File("bmp-32-alpha.bmp"));

        Assert.Equal(ImportTesting.Rgb8(67, 45), ImportTesting.Samples(image));
        Assert.Equal(PdfImageMaskKind.Soft, image.MaskKind);
        Assert.Equal(ImportTesting.Expected(67, 45, 1, (x, y, _) => ImportSamples.Alpha8(x, y)), ImportTesting.Samples(image.Mask!));
    }

    [Fact]
    public void Bit_fields_scale_to_8_bits()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromBmp(ImportTesting.File("bmp-16-565.bmp"));

        static int Scale(int value, int bits) => ((value * 255) + (((1 << bits) - 1) / 2)) / ((1 << bits) - 1);
        Assert.Equal(
            ImportTesting.Expected(67, 45, 3, (x, y, c) => ImportSamples.Rgb8(x, y) switch
            {
                var v => c switch { 0 => Scale(v.R >> 3, 5), 1 => Scale(v.G >> 2, 6), _ => Scale(v.B >> 3, 5) },
            }),
            ImportTesting.Samples(image));
    }

    [Fact]
    public void Bitmaps_from_the_sample_writer_round_trip()
    {
        byte[] rgb = ImportSamples.RgbGradient(50, 20);
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromBmp(ImportSamples.Bmp24(50, 20, rgb));

        Assert.Equal(rgb.Select(v => (ushort)v), ImportTesting.Samples(image));
    }

    [Fact]
    public void Malformed_and_unsupported_files_are_refused()
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Throws<ArgumentException>(() => document.CreateImageFromBmp("BM"u8));
        Assert.Throws<ArgumentException>(() => document.CreateImageFromBmp(ImportSamples.Bmp(4, 4, 7, 0, [], new byte[64])));
        Assert.Throws<ArgumentException>(() => document.CreateImageFromBmp(ImportSamples.Bmp(4, 4, 24, 1, [], new byte[64])));
        Assert.Throws<NotSupportedException>(() => document.CreateImageFromBmp(ImportSamples.Bmp(4, 4, 24, 4, [], new byte[64])));
        Assert.Throws<ArgumentException>(() => document.CreateImageFromBmp(ImportSamples.Bmp(4, 4, 8, 0, [], new byte[16])));
    }
}
