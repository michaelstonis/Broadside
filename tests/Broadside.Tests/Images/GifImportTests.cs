using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// <see cref="PdfDocument.CreateImageFromGif"/> (issue #642) over the GIF files of <c>Images/Import</c> and ones built by
/// <see cref="ImportSamples"/>: LZW, global and local color tables, interlacing, the transparent index, damaged data.
/// </summary>
public sealed class GifImportTests
{
    [Theory]
    [InlineData("gif-256.gif", 61, 37, 256, 8)]
    [InlineData("gif-16-interlaced.gif", 33, 27, 16, 4)]
    [InlineData("gif-transparent-local.gif", 20, 10, 8, 4)]
    public void Indices_and_color_table_become_an_indexed_image(string file, int width, int height, int entries, int bits)
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromGif(ImportTesting.File(file));

        Assert.IsType<PdfIndexedColorSpace>(image.ColorSpace);
        Assert.Equal(bits, image.BitsPerComponent);
        Assert.Equal(ImportTesting.Expected(width, height, 1, (x, y, _) => ImportSamples.Index(x, y, entries)), ImportTesting.Samples(image));
        byte[] lookup = ImportTesting.Lookup(image);
        int step = 255 / (entries - 1);
        for (int i = 0; i < entries; i++)
        {
            (int r, int g, int b) = ImportSamples.PaletteRgb(i * step);
            Assert.Equal([(byte)r, (byte)g, (byte)b], lookup[(3 * i)..((3 * i) + 3)]);
        }

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Two_colors_are_one_bit_indices()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromGif(ImportTesting.File("gif-2.gif"));

        Assert.Equal(1, image.BitsPerComponent);
        Assert.Equal(ImportTesting.Expected(45, 21, 1, (x, y, _) => ImportSamples.Black(x, y) ? 1 : 0), ImportTesting.Samples(image));
    }

    [Fact]
    public void The_transparent_index_becomes_a_color_key_mask()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromGif(ImportTesting.File("gif-transparent-local.gif"));

        Assert.Equal(PdfImageMaskKind.ColorKey, image.MaskKind);
        Assert.Equal([3.0, 3.0], image.ColorKey);
    }

    [Fact]
    public void Images_from_the_sample_writer_round_trip()
    {
        int width = 300;
        int height = 50;
        byte[] indices = new byte[width * height];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (byte)((i * 7 / 3) % 256);
        }

        byte[] palette = new byte[768];
        using PdfDocument document = PdfDocument.Create();
        foreach (bool interlaced in new[] { false, true })
        {
            PdfImage image = document.CreateImageFromGif(ImportSamples.Gif(width, height, indices, palette, interlaced));

            Assert.Equal(indices.Select(i => (ushort)i), ImportTesting.Samples(image));
        }
    }

    [Fact]
    public void Data_that_ends_early_keeps_what_decoded_and_is_reported()
    {
        byte[] gif = ImportTesting.File("gif-256.gif");
        byte[] cut = [.. gif[..(gif.Length - 400)], 0, 0x3B];
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromGif(cut);

        Assert.Equal(61, image.Width);
        Assert.Contains(document.Diagnostics, d => d.Code == ImageDiagnostics.ImageImportRepaired.Name);
    }

    [Fact]
    public void Malformed_files_are_refused()
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Throws<ArgumentException>(() => document.CreateImageFromGif("GIF89a"u8));
        Assert.Throws<ArgumentException>(() => document.CreateImageFromGif("PNG"u8));
        Assert.Throws<ArgumentException>(() => document.CreateImageFromGif("GIF89a\x01\0\x01\0\0\0\0\x3B"u8));
        byte[] noTable = [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x4C, 0x01, 0, 0x3B];
        Assert.Throws<ArgumentException>(() => document.CreateImageFromGif(noTable));
    }

    [Fact]
    public void Interlaced_rows_map_to_the_four_passes()
    {
        int[] rows = [.. Enumerable.Range(0, 11).Select(i => Broadside.Images.Import.GifReader.InterlacedRow(i, 11))];

        Assert.Equal([0, 8, 4, 2, 6, 10, 1, 3, 5, 7, 9], rows);
    }
}
