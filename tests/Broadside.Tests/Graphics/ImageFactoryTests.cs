using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Tests.Filters.Dct;

namespace Broadside.Tests.Graphics;

/// <summary>
/// The image factories of issue #153 beyond the PNG vectors: JPEG pass-through and its CMYK convention, raw sample buffers,
/// de-duplication, refusals and the versions the features need. ISO 32000-2 §8.9.5, §8.9.6, §11.6.5.2, §7.4.4, §7.4.8.
/// </summary>
public sealed class ImageFactoryTests
{
    private static readonly double[] NotInverted = [0, 1, 0, 1, 0, 1, 0, 1];
    private static readonly double[] Inverted = [1, 0, 1, 0, 1, 0, 1, 0];

    [Theory]
    [InlineData("rgb", 3, false)]
    [InlineData("testorig", 3, false)]
    [InlineData("progressive", 3, true)]
    [InlineData("gray-2x2", 1, false)]
    [InlineData("cmyk", 4, false)]
    [InlineData("ycck", 4, false)]
    public void A_jpeg_is_placed_byte_for_byte_as_a_DCTDecode_image(string name, int components, bool progressive)
    {
        byte[] jpeg = DctVectors.Jpeg(name);
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImageFromJpeg(jpeg);
        Place(document, image);

        using PdfDocument reopened = PdfDocument.Open(CanvasImageTests.Saved(document, Uncompressed()), new PdfOptions().UseStrict());
        PdfImage placed = CanvasImageTests.PlacedImages(reopened).Single();

        Assert.Equal(jpeg, placed.Stream!.EncodedData.ToArray());
        Assert.Equal(new CosName("DCTDecode"), placed.Dictionary[new CosName("Filter")]);
        Assert.Equal(components, placed.ColorSpace!.ComponentCount);
        using DecodedImage decoded = placed.Decode()!;
        Assert.Equal((placed.Width, placed.Height, components), (decoded.Width, decoded.DecodedRows, decoded.Components));
        Assert.Equal(progressive ? "%PDF-1.3" : "%PDF-1.2", Header(target => target.CreateImageFromJpeg(jpeg)));
        Assert.Empty(reopened.Diagnostics);
    }

    [Theory]
    [InlineData("cmyk")]
    [InlineData("ycck")]
    public void A_cmyk_jpeg_is_placed_as_stored_unless_photoshop_wrote_it_inverted(string name)
    {
        // libjpeg-turbo writes these with an Adobe APP14 segment and the samples as they are; Photoshop writes the same segment
        // with every sample inverted, and always an APP13 "Photoshop 3.0" segment, which is the signal.
        byte[] jpeg = DctVectors.Jpeg(name);
        using PdfDocument document = PdfDocument.Create();

        PdfImage stored = document.CreateImageFromJpeg(jpeg);
        PdfImage photoshop = document.CreateImageFromJpeg(WithPhotoshopSegment(jpeg));

        Assert.Equal(NotInverted, stored.DecodeArray);
        Assert.Equal(Inverted, photoshop.DecodeArray);
        Assert.False(stored.Dictionary.ContainsKey(new CosName("Decode")));
        using DecodedImage a = stored.Decode()!;
        using DecodedImage b = photoshop.Decode()!;
        Assert.Equal(Samples(a), Samples(b));
    }

    [Fact]
    public void A_cmyk_jpeg_from_the_dct_encoder_round_trips_through_CreateImageFromJpeg()
    {
        DctGolden golden = DctVectors.Golden("cmyk");
        byte[] jpeg = DctEncoderTests.Encode(new DctEncoder(90, DctChromaSubsampling.HorizontalAndVertical), golden, null);
        using PdfDocument document = PdfDocument.Create();

        PdfImage image = document.CreateImageFromJpeg(jpeg);

        Assert.Equal(NotInverted, image.DecodeArray);
        using DecodedImage decoded = image.Decode()!;
        byte[] samples = Samples(decoded);
        double error = samples.Zip(golden.Samples, (x, y) => (double)Math.Abs(x - y)).Average();
        Assert.True(error < 2, $"Mean absolute error {error}");
    }

    [Fact]
    public void A_jpeg_whose_frame_dctdecode_does_not_take_is_refused()
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Throws<ArgumentException>(() => document.CreateImageFromJpeg(DctVectors.Jpeg("arithmetic")));
        Assert.Throws<ArgumentException>(() => document.CreateImageFromJpeg(DctVectors.Jpeg("precision12")));
        Assert.Throws<ArgumentException>(() => document.CreateImageFromJpeg("not a jpeg"u8));
        Assert.Throws<ArgumentException>(() => document.CreateImageFromJpeg(DctVectors.Jpeg("rgb").AsSpan(0, 40)));
    }

    [Fact]
    public void A_png_that_does_not_decode_is_refused()
    {
        using PdfDocument document = PdfDocument.Create();
        byte[] png = PngVectors.Png("generated/f-c8");

        Assert.Throws<ArgumentException>(() => document.CreateImageFromPng("not a png"u8));
        Assert.Throws<ArgumentException>(() => document.CreateImageFromPng(png.AsSpan(0, png.Length / 2)));
        byte[] corrupt = (byte[])png.Clone();
        corrupt[20] ^= 0xFF; // inside IHDR: its CRC no longer matches
        Assert.Throws<ArgumentException>(() => document.CreateImageFromPng(corrupt));
    }

    [Fact]
    public void The_same_image_created_twice_is_written_once()
    {
        using PdfDocument document = PdfDocument.Create();
        byte[] samples = Enumerable.Range(0, 4 * 3 * 3).Select(i => (byte)(i * 7)).ToArray();
        var layout = new PdfImageLayout(4, 3, PdfDeviceRgbColorSpace.Instance);

        PdfImage png = document.CreateImageFromPng(PngVectors.Png("generated/f-c8"));
        PdfImage jpeg = document.CreateImageFromJpeg(DctVectors.Jpeg("rgb"));
        PdfImage raw = document.CreateImage(samples, layout, PdfImageCompression.Flate);

        Assert.Equal(png.Reference, document.CreateImageFromPng(PngVectors.Png("generated/f-c8")).Reference);
        Assert.Equal(jpeg.Reference, document.CreateImageFromJpeg(DctVectors.Jpeg("rgb")).Reference);
        Assert.Equal(raw.Reference, document.CreateImage((byte[])samples.Clone(), layout, PdfImageCompression.Flate).Reference);
        Assert.NotEqual(raw.Reference, document.CreateImage(samples, layout, PdfImageCompression.Dct).Reference);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(png, 0, 0, 10, 10);
            canvas.DrawImage(png, 20, 0, 10, 10);
            canvas.DrawImage(jpeg);
        }

        using PdfDocument reopened = PdfDocument.Open(CanvasImageTests.Saved(document));
        List<PdfImage> placed = CanvasImageTests.PlacedImages(reopened);
        Assert.Equal(3, placed.Count);
        Assert.Equal(placed[0].Reference, placed[1].Reference);
        Assert.Equal(2, placed.Select(image => image.Reference).Distinct().Count());
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(3, 8)]
    [InlineData(4, 8)]
    [InlineData(1, 16)]
    [InlineData(3, 16)]
    [InlineData(1, 1)]
    [InlineData(1, 4)]
    public void Raw_samples_round_trip_losslessly_with_flate(int components, int bits)
    {
        PdfColorSpace space = Space(components);
        var layout = new PdfImageLayout(5, 3, space, bits);
        int stride = ((5 * components * bits) + 7) / 8;
        byte[] samples = Enumerable.Range(0, stride * 3).Select(i => (byte)((i * 37) + 11)).ToArray();
        byte[] alpha = Enumerable.Range(0, (((5 * bits) + 7) / 8) * 3).Select(i => (byte)(255 - (i * 13))).ToArray();
        using PdfDocument document = PdfDocument.Create();
        Place(document, document.CreateImage(samples, layout, PdfImageCompression.Flate));
        Place(document, document.CreateImage(samples, alpha, layout, PdfImageCompression.Flate));

        using PdfDocument reopened = PdfDocument.Open(CanvasImageTests.Saved(document), new PdfOptions().UseStrict());
        List<PdfImage> placed = CanvasImageTests.PlacedImages(reopened);

        Assert.Equal(samples, Samples(placed[0]));
        Assert.Equal(PdfImageMaskKind.None, placed[0].MaskKind);
        Assert.Equal(samples, Samples(placed[1]));
        Assert.Equal(PdfImageMaskKind.Soft, placed[1].MaskKind);
        Assert.Equal(alpha, Samples(placed[1].Mask!));
        Assert.Empty(reopened.Diagnostics);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void Raw_samples_compressed_with_dct_come_back_close(int components)
    {
        var layout = new PdfImageLayout(16, 16, Space(components));
        byte[] samples = new byte[16 * 16 * components];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)(64 + ((i / components % 16) * 8) + (i % components * 10));
        }

        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage(samples, layout, PdfImageCompression.Dct);

        Assert.Equal(new CosName("DCTDecode"), image.Dictionary[new CosName("Filter")]);
        byte[] decoded = Samples(image);
        double error = decoded.Zip(samples, (x, y) => (double)Math.Abs(x - y)).Average();
        Assert.True(error < 3, $"Mean absolute error {error}");
    }

    [Fact]
    public void Raw_samples_of_the_wrong_size_or_for_dct_in_a_space_it_cannot_take_are_refused()
    {
        using PdfDocument document = PdfDocument.Create();
        var layout = new PdfImageLayout(2, 2, PdfDeviceRgbColorSpace.Instance);
        PdfIndexedColorSpace indexed = document.CreateIndexedColorSpace(PdfDeviceRgbColorSpace.Instance, new byte[6]);

        Assert.Throws<ArgumentException>(() => document.CreateImage(new byte[11], layout, PdfImageCompression.Flate));
        Assert.Throws<ArgumentException>(() => document.CreateImage(new byte[12], new byte[3], layout, PdfImageCompression.Flate));
        Assert.Throws<ArgumentException>(() => document.CreateImage(new byte[24], layout with { BitsPerComponent = 16 }, PdfImageCompression.Dct));
        Assert.Throws<ArgumentException>(() => document.CreateImage(new byte[4], new PdfImageLayout(2, 2, indexed), PdfImageCompression.Dct));
        Assert.Throws<ArgumentException>(() => document.CreateIndexedColorSpace(PdfDeviceRgbColorSpace.Instance, new byte[7]));
    }

    [Fact]
    public void An_indexed_image_reads_back_through_its_lookup()
    {
        using PdfDocument document = PdfDocument.Create();
        byte[] lookup = [255, 0, 0, 0, 255, 0, 0, 0, 255];
        PdfIndexedColorSpace space = document.CreateIndexedColorSpace(PdfDeviceRgbColorSpace.Instance, lookup);
        byte[] samples = [0, 1, 2, 1];
        Place(document, document.CreateImage(samples, new PdfImageLayout(2, 2, space), PdfImageCompression.Flate));

        using PdfDocument reopened = PdfDocument.Open(CanvasImageTests.Saved(document), new PdfOptions().UseStrict());
        PdfImage placed = CanvasImageTests.PlacedImages(reopened).Single();

        Assert.Equal(samples, Samples(placed));
        Assert.Equal(lookup, ((PdfIndexedColorSpace)placed.ColorSpace!).GetLookup().ToArray());
    }

    [Fact]
    public void Drawing_an_image_of_another_document_or_while_a_path_is_built_is_refused()
    {
        using PdfDocument other = PdfDocument.Create();
        PdfImage foreign = other.CreateImageFromPng(PngVectors.Png("generated/f-c8"));
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImageFromPng(PngVectors.Png("generated/f-c8"));
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();

        Assert.Throws<ArgumentException>(() => canvas.DrawImage(foreign));
        Assert.Throws<ArgumentException>(() => canvas.DrawImage(image, 0, 0, double.NaN, 1));
        canvas.MoveTo(0, 0);
        Assert.Throws<InvalidOperationException>(() => canvas.DrawImage(image));
    }

    [Fact]
    public void Draw_image_paints_the_unit_square_scaled_to_the_rectangle()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImageFromJpeg(DctVectors.Jpeg("rgb"));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(image, 10, 20, 300, 150);
            canvas.DrawImage(image);
        }

        var recorder = new CanvasImageTests.ImageRecorder(document);
        document.Pages[0].ProcessContent(recorder);

        Assert.Equal(new Matrix(300, 0, 0, 150, 10, 20), recorder.Transforms[0]);
        Assert.Equal(Matrix.Identity, recorder.Transforms[1]);
        Assert.All(recorder.Names, name => Assert.StartsWith("Im", name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("generated/f-g16", "1.5")]
    [InlineData("generated/trns-c8", "1.3")]
    [InlineData("generated/f-ga8", "1.4")]
    public void A_png_raises_the_version_its_features_need(string name, string version)
    {
        Assert.Equal("%PDF-" + version, Header(document => document.CreateImageFromPng(PngVectors.Png(name))));
    }

    [Fact]
    public void Strict_mode_refuses_to_save_a_sixteen_bit_image_in_a_pdf_1_4_document()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4"), new PdfOptions().UseStrict());
        Place(document, document.CreateImageFromPng(PngVectors.Png("generated/f-g16")));

        using var output = new MemoryStream();
        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => document.Save(output));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
    }

    [Theory]
    [InlineData("progressive", "1.3")]
    [InlineData("rgb", "1.0")]
    public void A_copied_dct_image_carries_the_version_of_its_jpeg_process(string name, string version)
    {
        using PdfDocument source = PdfDocument.Create();
        PdfImage image = source.CreateImageFromJpeg(DctVectors.Jpeg(name));
        using PdfDocument target = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.0"));

        new PdfObjectCopier(source, target).Copy(image.Reference!);

        Assert.Equal("%PDF-" + version, System.Text.Encoding.ASCII.GetString(CanvasImageTests.Saved(target, Uncompressed()), 0, 8));
    }

    private static void Place(PdfDocument document, PdfImage image)
    {
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        canvas.DrawImage(image, 0, 0, image.Width, image.Height);
    }

    /// <summary>The header a PDF 1.2 document is saved with once the image <paramref name="create"/> makes is placed in it.</summary>
    private static string Header(Func<PdfDocument, PdfImage> create)
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.2"));
        Place(document, create(document));
        return System.Text.Encoding.ASCII.GetString(CanvasImageTests.Saved(document, Uncompressed()), 0, 8);
    }

    private static PdfSaveOptions Uncompressed() =>
        new PdfSaveOptions().WithUpdateModificationDate(false).WithStreamCompression(PdfStreamCompression.None);

    private static PdfColorSpace Space(int components) => components switch
    {
        1 => PdfDeviceGrayColorSpace.Instance,
        3 => PdfDeviceRgbColorSpace.Instance,
        _ => PdfDeviceCmykColorSpace.Instance,
    };

    /// <summary>A JPEG with an APP13 <c>Photoshop 3.0</c> segment (an empty resource block list) after SOI, as Photoshop writes it.</summary>
    private static byte[] WithPhotoshopSegment(byte[] jpeg)
    {
        byte[] body = "Photoshop 3.0\0"u8.ToArray();
        byte[] segment = [0xFF, 0xED, 0, (byte)(body.Length + 2), .. body];
        return [.. jpeg.AsSpan(0, 2), .. segment, .. jpeg.AsSpan(2)];
    }

    private static byte[] Samples(PdfImage image)
    {
        using DecodedImage decoded = image.Decode()!;
        return Samples(decoded);
    }

    private static byte[] Samples(DecodedImage decoded)
    {
        int stride = ((decoded.Width * decoded.Components * decoded.StorageBits) + 7) / 8;
        byte[] samples = new byte[stride * decoded.Height];
        for (int y = 0; y < decoded.Height; y++)
        {
            decoded.GetRow(y)[..stride].CopyTo(samples.AsSpan(y * stride));
        }

        return samples;
    }
}
