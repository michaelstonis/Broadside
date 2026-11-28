using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Graphics.Colors;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Tests.Graphics;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// <see cref="PdfDocument.CreateImagesFromTiff"/> (issue #642) over libtiff's output (<c>Images/Import</c>, made by
/// <c>make_import_images.py</c> with <c>tiffcp</c>) and files built by <see cref="ImportSamples"/>: CCITT and JPEG pass-through, the
/// decoded compressions, every photometric interpretation and layout, alpha, multipage files, repairs and refusals.
/// </summary>
public sealed class TiffImportTests
{
    [Theory]
    [InlineData("bilevel-g3.tif", 0, false, true)]
    [InlineData("bilevel-g3-2d.tif", 97, false, true)]
    [InlineData("bilevel-g3-2d-fill.tif", 97, false, true)]
    [InlineData("bilevel-g4.tif", -1, false, false)]
    [InlineData("bilevel-g4-fillorder2.tif", -1, false, false)]
    public void Ccitt_data_passes_through_and_decodes_to_the_original_bitmap(string file, int k, bool byteAlign, bool endOfLine)
    {
        byte[] tiff = ImportTesting.File(file);
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = Assert.Single(document.CreateImagesFromTiff(tiff));

        Assert.Equal("CCITTFaxDecode", ImportTesting.Filter(image));
        Assert.Equal(k, ((CosInteger)ImportTesting.Parameter(image, "K")!).Value);
        Assert.Equal(203, ((CosInteger)ImportTesting.Parameter(image, "Columns")!).Value);
        Assert.Equal(97, ((CosInteger)ImportTesting.Parameter(image, "Rows")!).Value);
        Assert.Equal(byteAlign, ImportTesting.Parameter(image, "EncodedByteAlign") is CosBoolean { Value: true });
        Assert.Equal(endOfLine, ImportTesting.Parameter(image, "EndOfLine") is CosBoolean { Value: true });
        Assert.Null(ImportTesting.Parameter(image, "BlackIs1"));
        Assert.Equal(ImportTesting.Bilevel(203, 97), ImportTesting.Samples(image));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Ccitt_data_is_stored_byte_for_byte_and_bit_reversed_for_fill_order_2()
    {
        byte[] normal = ImportTesting.File("bilevel-g4.tif");
        byte[] reversed = ImportTesting.File("bilevel-g4-fillorder2.tif");
        using PdfDocument document = PdfDocument.Create();

        byte[] a = document.CreateImagesFromTiff(normal)[0].Stream!.EncodedData.ToArray();
        byte[] b = document.CreateImagesFromTiff(reversed)[0].Stream!.EncodedData.ToArray();

        Assert.Equal(a, b);
        Assert.True(normal.AsSpan().IndexOf(a) > 0, "The G4 strip is not in the file as it stands.");
    }

    [Fact]
    public void Modified_huffman_strips_are_joined_and_pass_through()
    {
        byte[] data = ImportSamples.Bilevel(203, 97);
        byte[] tiff = ImportSamples.Tiff(new TiffPage(203, 97, 1, 1, 0, data) { Compression = 2, RowsPerStrip = 10 });
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImagesFromTiff(tiff)[0];

        Assert.Equal("CCITTFaxDecode", ImportTesting.Filter(image));
        Assert.Equal(0, ((CosInteger)ImportTesting.Parameter(image, "K")!).Value);
        Assert.True(ImportTesting.Parameter(image, "EncodedByteAlign") is CosBoolean { Value: true });
        Assert.Equal(ImportTesting.Bilevel(203, 97), ImportTesting.Samples(image));
    }

    [Fact]
    public void Black_is_zero_ccitt_data_sets_black_is_1()
    {
        // BlackIsZero: the coded "black" runs are 1 bits, which BlackIsZero shows as white; BlackIs1 maps them the same way.
        byte[] data = ImportSamples.Bilevel(64, 20);
        byte[] tiff = ImportSamples.Tiff(new TiffPage(64, 20, 1, 1, 1, data) { Compression = 4 });
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImagesFromTiff(tiff)[0];

        Assert.True(ImportTesting.Parameter(image, "BlackIs1") is CosBoolean { Value: true });
        Assert.Equal(ImportTesting.Expected(64, 20, 1, (x, y, _) => ImportSamples.Black(x, y) ? 1 : 0), ImportTesting.Samples(image));
    }

    [Theory]
    [InlineData("bilevel-g4-strips.tif")]
    [InlineData("bilevel-packbits.tif")]
    [InlineData("bilevel-tiles.tif")]
    [InlineData("bilevel.tif")]
    public void Bilevel_data_that_cannot_pass_through_is_decoded(string file)
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImagesFromTiff(ImportTesting.File(file))[0];

        Assert.Equal("FlateDecode", ImportTesting.Filter(image));
        Assert.Equal(1, image.BitsPerComponent);
        Assert.Equal(ImportTesting.Bilevel(203, 97), ImportTesting.Samples(image));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("rgb8.tif")]
    [InlineData("rgb8-lzw.tif")]
    [InlineData("rgb8-lzw-predictor.tif")]
    [InlineData("rgb8-deflate-predictor-be.tif")]
    [InlineData("rgb8-packbits.tif")]
    [InlineData("rgb8-tiles-lzw.tif")]
    [InlineData("rgb8-planar-lzw.tif")]
    public void Rgb_files_decode_to_the_original_samples(string file)
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImagesFromTiff(ImportTesting.File(file))[0];

        Assert.Equal("FlateDecode", ImportTesting.Filter(image));
        Assert.IsType<PdfDeviceRgbColorSpace>(image.ColorSpace);
        Assert.Equal(ImportTesting.Rgb8(67, 45), ImportTesting.Samples(image));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("rgb16-lzw-predictor.tif")]
    [InlineData("rgb16-lzw-predictor-be.tif")]
    [InlineData("rgb16.tif")]
    public void Sixteen_bit_samples_keep_their_depth_in_either_byte_order(string file)
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImagesFromTiff(ImportTesting.File(file))[0];

        Assert.Equal(16, image.BitsPerComponent);
        Assert.Equal(ImportTesting.Expected(33, 21, 3, (x, y, c) => ImportTesting.Component(ImportSamples.Rgb16(x, y), c)), ImportTesting.Samples(image));
    }

    [Fact]
    public void Gray_and_cmyk_files_decode_to_the_original_samples()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage gray = document.CreateImagesFromTiff(ImportTesting.File("gray8-lzw-predictor.tif"))[0];
        PdfImage gray4 = document.CreateImagesFromTiff(ImportTesting.File("gray4-packbits.tif"))[0];
        PdfImage cmyk = document.CreateImagesFromTiff(ImportTesting.File("cmyk8-lzw.tif"))[0];

        Assert.IsType<PdfDeviceGrayColorSpace>(gray.ColorSpace);
        Assert.Equal(ImportTesting.Expected(64, 40, 1, (x, y, _) => ImportSamples.Gray8(x, y)), ImportTesting.Samples(gray));
        Assert.Equal(4, gray4.BitsPerComponent);
        Assert.Equal(ImportTesting.Expected(29, 19, 1, (x, y, _) => (x + y) % 16), ImportTesting.Samples(gray4));
        Assert.IsType<PdfDeviceCmykColorSpace>(cmyk.ColorSpace);
        Assert.Equal(
            ImportTesting.Expected(31, 17, 4, (x, y, c) => ImportSamples.Cmyk8(x, y) switch { var v => c switch { 0 => v.C, 1 => v.M, 2 => v.Y, _ => v.K } }),
            ImportTesting.Samples(cmyk));
    }

    [Theory]
    [InlineData("palette8-lzw.tif", 40, 30, 256, 1)]
    [InlineData("palette4.tif", 37, 23, 16, 17)]
    public void Palette_files_become_indexed_images(string file, int width, int height, int entries, int step)
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImagesFromTiff(ImportTesting.File(file))[0];

        Assert.IsType<PdfIndexedColorSpace>(image.ColorSpace);
        Assert.Equal(ImportTesting.Expected(width, height, 1, (x, y, _) => ImportSamples.Index(x, y, entries)), ImportTesting.Samples(image));
        byte[] lookup = ImportTesting.Lookup(image);
        Assert.Equal(3 * entries, lookup.Length);
        for (int i = 0; i < entries; i++)
        {
            (int r, int g, int b) = ImportSamples.PaletteRgb(i * step);
            Assert.Equal([(byte)r, (byte)g, (byte)b], lookup[(3 * i)..((3 * i) + 3)]);
        }
    }

    [Fact]
    public void Single_strip_jpeg_passes_through_with_its_tables_merged()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage ycc = document.CreateImagesFromTiff(ImportTesting.File("smooth8-jpeg.tif"))[0];
        PdfImage rgb = document.CreateImagesFromTiff(ImportTesting.File("smooth8-jpeg-rgb.tif"))[0];

        Assert.Equal("DCTDecode", ImportTesting.Filter(ycc));
        Assert.Null(ImportTesting.Parameter(ycc, "ColorTransform"));
        Assert.Equal("DCTDecode", ImportTesting.Filter(rgb));
        Assert.Equal(0, ((CosInteger)ImportTesting.Parameter(rgb, "ColorTransform")!).Value);
        ImportTesting.Near(ImportTesting.Smooth(67, 45), ImportTesting.Samples(ycc), 16);
        ImportTesting.Near(ImportTesting.Smooth(67, 45), ImportTesting.Samples(rgb), 16);
    }

    [Fact]
    public void Multi_strip_jpeg_is_decoded()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImagesFromTiff(ImportTesting.File("smooth8-jpeg-strips.tif"))[0];

        Assert.Equal("FlateDecode", ImportTesting.Filter(image));
        ImportTesting.Near(ImportTesting.Smooth(67, 45), ImportTesting.Samples(image), 16);
    }

    [Fact]
    public void Unassociated_alpha_becomes_a_soft_mask()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImagesFromTiff(ImportTesting.File("rgba8-deflate.tif"))[0];

        Assert.Equal(PdfImageMaskKind.Soft, image.MaskKind);
        Assert.Equal(ImportTesting.Rgb8(67, 45), ImportTesting.Samples(image));
        Assert.Equal(ImportTesting.Expected(67, 45, 1, (x, y, _) => ImportSamples.Alpha8(x, y)), ImportTesting.Samples(image.Mask!));
        Assert.False(image.Mask!.Stream!.Dictionary.ContainsKey(new CosName("Matte")));
    }

    [Fact]
    public void Associated_alpha_gets_a_matte_and_opaque_alpha_none()
    {
        byte[] rgba = ImportSamples.Pack(8, 4, 8, 4, (x, y, c) => c == 3 ? x * 30 : y * 10);
        byte[] opaque = ImportSamples.Pack(8, 4, 8, 2, (x, _, c) => c == 1 ? 255 : x);
        byte[] tiff = ImportSamples.Tiff(
            new TiffPage(8, 4, 4, 8, 2, rgba) { ExtraSamples = [1], Planar = 2 },
            new TiffPage(8, 4, 2, 8, 1, opaque) { ExtraSamples = [2] });
        using PdfDocument document = PdfDocument.Create();

        IReadOnlyList<PdfImage> images = document.CreateImagesFromTiff(tiff);

        var matte = (CosArray)images[0].Mask!.Stream!.Dictionary[new CosName("Matte")];
        Assert.Equal(3, matte.Count);
        Assert.Equal(ImportTesting.Expected(8, 4, 1, (x, _, _) => x * 30), ImportTesting.Samples(images[0].Mask!));
        Assert.Equal(PdfImageMaskKind.None, images[1].MaskKind);
    }

    [Fact]
    public void Each_page_of_a_multipage_file_becomes_an_image()
    {
        using PdfDocument document = PdfDocument.Create();

        IReadOnlyList<PdfImage> images = document.CreateImagesFromTiff(ImportTesting.File("multipage.tif"));

        Assert.Equal(["CCITTFaxDecode", "FlateDecode", "DCTDecode"], images.Select(ImportTesting.Filter));
        Assert.Equal(ImportTesting.Bilevel(203, 97), ImportTesting.Samples(images[0]));
        Assert.Equal(ImportTesting.Expected(64, 40, 1, (x, y, _) => ImportSamples.Gray8(x, y)), ImportTesting.Samples(images[1]));
        Assert.Same(images[2].Stream, document.CreateImageFromTiff(ImportTesting.File("multipage.tif"), 2).Stream);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.CreateImageFromTiff(ImportTesting.File("multipage.tif"), 3));
    }

    [Fact]
    public void The_same_file_gives_the_same_images()
    {
        byte[] tiff = ImportTesting.File("multipage.tif");
        using PdfDocument document = PdfDocument.Create();

        IReadOnlyList<PdfImage> first = document.CreateImagesFromTiff(tiff);
        IReadOnlyList<PdfImage> second = document.CreateImagesFromTiff(tiff);

        Assert.Equal(first.Select(i => i.Stream), second.Select(i => i.Stream));
    }

    [Fact]
    public void Thumbnails_are_skipped()
    {
        byte[] data = ImportSamples.Pack(4, 4, 8, 1, (x, y, _) => x + y);
        byte[] tiff = ImportSamples.Tiff(
            new TiffPage(4, 4, 1, 8, 1, data),
            new TiffPage(2, 2, 1, 8, 1, new byte[4]) { SubfileType = 1 },
            new TiffPage(4, 4, 1, 8, 1, data) { Compression = 5 });
        using PdfDocument document = PdfDocument.Create();

        Assert.Equal(2, document.CreateImagesFromTiff(tiff).Count);
    }

    [Fact]
    public void Lzw_and_deflate_and_packbits_from_the_sample_writer_round_trip_in_every_layout()
    {
        byte[] data = ImportSamples.Pack(37, 29, 16, 3, (x, y, c) => (x * 1777) + (y * 31) + (c * 9000));
        using PdfDocument document = PdfDocument.Create();
        foreach (int compression in new[] { 1, 5, 8, 32773 })
        {
            foreach (bool big in new[] { false, true })
            {
                foreach (int planar in new[] { 1, 2 })
                {
                    foreach (int tile in new[] { 0, 16 })
                    {
                        byte[] tiff = ImportSamples.Tiff(big, new TiffPage(37, 29, 3, 16, 2, data)
                        {
                            Compression = compression,
                            Planar = planar,
                            Tile = tile,
                            RowsPerStrip = 7,
                            Predictor = compression is 5 or 8 ? 2 : 1,
                        });

                        PdfImage image = document.CreateImagesFromTiff(tiff)[0];

                        Assert.Equal(ImportTesting.Expected(37, 29, 3, (x, y, c) => ((x * 1777) + (y * 31) + (c * 9000)) & 0xFFFF), ImportTesting.Samples(image));
                    }
                }
            }
        }

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Wrong_strip_byte_counts_are_repaired_and_reported()
    {
        byte[] data = ImportSamples.Pack(20, 10, 8, 1, (x, y, _) => (x * 10) + y);
        byte[] zero = ImportSamples.Tiff(new TiffPage(20, 10, 1, 8, 1, data) { Compression = 5, RowsPerStrip = 3, ZeroByteCounts = true });
        byte[] missing = ImportSamples.Tiff(new TiffPage(20, 10, 1, 8, 1, data) { RowsPerStrip = 4, NoByteCounts = true });
        ushort[] expected = ImportTesting.Expected(20, 10, 1, (x, y, _) => (x * 10) + y);
        foreach (byte[] tiff in new[] { zero, missing })
        {
            using PdfDocument document = PdfDocument.Create();

            Assert.Equal(expected, ImportTesting.Samples(document.CreateImagesFromTiff(tiff)[0]));

            Assert.Contains(document.Diagnostics, d => d.Code == ImageDiagnostics.ImageImportRepaired.Name);
        }

        using PdfDocument strict = PdfDocument.Create(new PdfOptions().UseStrict());
        Assert.Throws<DiagnosticException>(() => strict.CreateImagesFromTiff(zero));
    }

    [Fact]
    public void An_icc_profile_becomes_an_icc_based_space()
    {
        byte[] data = ImportSamples.Pack(4, 4, 8, 3, (x, y, c) => x * y * c);
        byte[] tiff = ImportSamples.Tiff(new TiffPage(4, 4, 3, 8, 2, data) { IccProfile = CanvasTests.RgbProfile() });
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImagesFromTiff(tiff)[0];

        Assert.IsType<PdfIccBasedColorSpace>(image.ColorSpace);
    }

    [Fact]
    public void Malformed_and_unsupported_files_are_refused_with_the_reason()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().WithMaxImagePixels(100));
        byte[] gray = ImportSamples.Pack(4, 4, 8, 1, (x, y, _) => x);

        Assert.Throws<ArgumentException>(() => document.CreateImagesFromTiff("not a tiff"u8));
        Assert.Throws<ArgumentException>(() => document.CreateImagesFromTiff("II*\0\0\0\0\0"u8)); // no page
        Assert.Throws<NotSupportedException>(() => document.CreateImagesFromTiff("II+\0\b\0\0\0\0\0\0\0\0\0\0\0"u8));
        Assert.Throws<NotSupportedException>(() => document.CreateImagesFromTiff(ImportSamples.Tiff(new TiffPage(4, 4, 1, 8, 1, gray) { Compression = 6 })));
        Assert.Throws<NotSupportedException>(() => document.CreateImagesFromTiff(ImportSamples.Tiff(new TiffPage(4, 4, 1, 8, 8, gray))));
        ArgumentException large = Assert.Throws<ArgumentException>(() => document.CreateImagesFromTiff(ImportSamples.Tiff(new TiffPage(20, 20, 1, 1, 0, new byte[60]))));
        Assert.Contains("MaxImagePixels", large.Message, StringComparison.Ordinal);
        byte[] truncated = ImportTesting.File("rgb8-lzw.tif")[..200];
        Assert.Throws<ArgumentException>(() => document.CreateImagesFromTiff(truncated));
        Assert.Throws<ArgumentException>(() => document.CreateImagesFromTiff(ImportSamples.Tiff(new TiffPage(4, 4, 1, 8, 3, gray))));
    }
}
