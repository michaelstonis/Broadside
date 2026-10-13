using System.Buffers;
using System.IO.Compression;
using System.Text;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Images in the display list (ISO 32000-2 §8.9.4, §8.9.5, §8.9.7; ADR 0006): image XObjects and inline images reach the backend
/// decoded, with their colour space, Decode mapping and Interpolate flag, under the CTM that maps the unit square; an image XObject
/// painted several times is decoded once.
/// </summary>
public sealed class DisplayListImageTests
{
    [Theory]
    [InlineData("image-1bpc.pdf", "image 10x3 DeviceGray bpc 1 interpolate")]
    [InlineData("image-2bpc.pdf", "image 5x2 DeviceGray bpc 2")]
    [InlineData("image-4bpc-indexed.pdf", "image 3x2 Indexed bpc 4")]
    [InlineData("image-16bpc.pdf", "image 3x2 DeviceRgb bpc 16")]
    [InlineData("image-decode-inverted.pdf", "image 4x1 DeviceGray bpc 8")]
    [InlineData("inline-image.pdf", "image 2x2 DeviceGray bpc 8")]
    [InlineData("inline-image-filters.pdf", "image 4x4 DeviceRgb bpc 8 interpolate")]
    [InlineData("dct-progressive.pdf", "image 51x35 DeviceRgb bpc 8", 204, 140)]
    [InlineData("jpx-lossless.pdf", "image 17x13 DeviceRgb bpc 8")]
    [InlineData("jbig2-generic.pdf", "image 150x48 DeviceGray bpc 1")]
    [InlineData("ccitt-g4.pdf", "image 1728x40 DeviceGray bpc 1")]
    public void An_image_is_drawn_into_the_unit_square_under_the_ctm(string fileName, string expected, double width = 100, double height = 100)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // Every one of these files paints its image with "q w 0 0 h 72 600 cm ... Q": a save, the CTM, the image, a restore.
        Assert.Equal(["save", "transform " + new Matrix(width, 0, 0, height, 72, 600), expected, "restore"], recorder.Operations);
    }

    [Fact]
    public void The_samples_decode_mapping_and_colour_space_reach_the_backend_unconverted()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-4bpc-indexed.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        DisplayImage image = Assert.Single(recorder.Images).Image;
        PdfIndexedColorSpace indexed = Assert.IsType<PdfIndexedColorSpace>(image.ColorSpace);
        Assert.Equal(15, indexed.HighValue);

        // Indices 0 5 15 / 9 1 14 at 4 bits, rows padded to a byte (§8.9.3; the generator sets the padding bits).
        Assert.Equal([0x05, 0xFF, 0x91, 0xEF], image.Samples.ToArray());
        Assert.Equal(2, image.Stride);
        Assert.Equal(4, image.StorageBits);
        Assert.Equal(1, image.Components);
        Assert.Equal(5f, image.DecodeMap.Map(0, 5));
    }

    [Fact]
    public void A_decode_array_travels_with_the_image()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-decode-inverted.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        DisplayImage image = Assert.Single(recorder.Images).Image;
        Assert.Equal([0, 85, 170, 255], image.Samples.ToArray());
        Assert.True(image.DecodeMap.IsInverted);
        Assert.Equal(1f, image.DecodeMap.Map(0, 0));
        Assert.Equal(0f, image.DecodeMap.Map(0, 255));
    }

    [Fact]
    public void The_image_intent_replaces_the_graphics_state_intent_for_that_image()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-1bpc.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // /Intent /Perceptual on the image; the graphics state keeps its initial RelativeColorimetric (§8.6.5.8).
        (DisplayImage image, PdfDefaultColorSpaces defaults, ColorConversion conversion) = Assert.Single(recorder.Images);
        Assert.Equal(RenderingIntent.Perceptual, conversion.Intent);
        Assert.Equal(DeviceColorModel.Rgb, conversion.Target);
        Assert.True(defaults.IsEmpty);
        Assert.True(image.Interpolate);
    }

    [Fact]
    public void An_image_xobject_painted_twice_is_decoded_once_and_shared_by_both_paints()
    {
        var counting = new CountingFlateFilter();
        byte[] file = ContentSamples.OnePage(
            Encoding.ASCII.GetBytes("q 100 0 0 100 72 600 cm /Im0 Do Q q 50 0 0 50 300 600 cm /Im0 Do Q"),
            "/XObject << /Im0 5 0 R >>",
            Checkerboard("/Filter /FlateDecode"));
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseFilter(counting));

        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(1, counting.Calls);
        Assert.Equal(2, recorder.Images.Count);
        Assert.Same(recorder.Images[0].Image, recorder.Images[1].Image);
        Assert.Equal(
            [
                "save", "transform " + new Matrix(100, 0, 0, 100, 72, 600), "image 2x2 DeviceGray bpc 8", "restore",
                "save", "transform " + new Matrix(50, 0, 0, 50, 300, 600), "image 2x2 DeviceGray bpc 8", "restore",
            ],
            recorder.Operations);
    }

    [Fact]
    public void Each_list_decodes_its_images_afresh()
    {
        byte[] file = ContentSamples.OnePage(Encoding.ASCII.GetBytes("/Im0 Do"), "/XObject << /Im0 5 0 R >>", Checkerboard());
        using PdfDocument document = PdfDocument.Open(file);
        var first = new RecordingBackend();
        var second = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(first);
        document.Pages[0].CreateDisplayList().Replay(second);

        Assert.NotSame(first.Images[0].Image, second.Images[0].Image);
        Assert.Equal(first.Images[0].Image.Samples.ToArray(), second.Images[0].Image.Samples.ToArray());
    }

    [Fact]
    public void An_image_that_cannot_be_decoded_paints_nothing_and_says_why()
    {
        // No ColorSpace on an image that is not a mask (§8.9.5.1, Table 87).
        byte[] file = ContentSamples.OnePage(
            Encoding.ASCII.GetBytes("q 100 0 0 100 72 600 cm /Im0 Do Q 0 g 0 0 10 10 re f"),
            "/XObject << /Im0 5 0 R >>",
            ContentSamples.Stream("\0\0\0\0", "/Type /XObject /Subtype /Image /Width 2 /Height 2 /BitsPerComponent 8"));
        using PdfDocument document = PdfDocument.Open(file);
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Empty(recorder.Images);
        Assert.Equal(["fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 0"], recorder.Operations);
        Assert.Contains(document.Diagnostics, static d => d.Code == DiagnosticCodes.ImageColorSpaceMissing);
    }

    [Fact]
    public void Image_masks_are_not_in_the_list_yet()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-stencil-mask.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Empty(recorder.Images);
    }

    /// <summary>A 2 x 2 8-bit DeviceGray checkerboard image XObject (black top left), its data compressed when a filter is named.</summary>
    internal static string Checkerboard(string entries = "")
    {
        byte[] samples = [0, 255, 255, 0];
        if (entries.Contains("FlateDecode", StringComparison.Ordinal))
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal))
            {
                zlib.Write(samples);
            }

            samples = compressed.ToArray();
        }

        return ContentSamples.Stream(
            Encoding.Latin1.GetString(samples),
            "/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8 " + entries);
    }

    /// <summary>FlateDecode that counts how often it runs.</summary>
    private sealed class CountingFlateFilter : IStreamFilter
    {
        private readonly FlateDecodeFilter _inner = new();
        private int _calls;

        public int Calls => _calls;

        public CosName Name => _inner.Name;

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            Interlocked.Increment(ref _calls);
            _inner.Decode(encoded, output, context);
        }
    }
}
