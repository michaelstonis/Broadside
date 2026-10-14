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
    public void With_the_image_and_display_list_caches_off_each_list_decodes_its_images_afresh()
    {
        byte[] file = ContentSamples.OnePage(Encoding.ASCII.GetBytes("/Im0 Do"), "/XObject << /Im0 5 0 R >>", Checkerboard());
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().WithImageCacheBudget(0).WithDisplayListCacheBudget(0));
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
    public void A_stencil_mask_is_filled_with_the_nonstroking_colour()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-stencil-mask.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // "1 0 0 rg q 100 0 0 100 72 600 cm /Im0 Do Q": an 8 x 8 image mask with Decode [1 0] (§8.9.6.2), painted in red.
        Assert.Empty(recorder.Images);
        Assert.Equal(
            ["save", "transform " + new Matrix(100, 0, 0, 100, 72, 600), "imagemask 8x8 inverted | DeviceRgb 1 0 0", "restore"],
            recorder.Operations);
        DisplayImageMask mask = Assert.Single(recorder.ImageMasks).Mask;
        Assert.Equal(1, mask.Stride);

        // The arrow's third row, 1 1 1 1 1 1 0 0, as packed by the generator.
        Assert.Equal(0xFC, mask.GetRow(2)[0]);
    }

    [Fact]
    public void Stencil_masks_with_either_polarity_paint_the_same_samples()
    {
        // ccitt-blackis1-mask.pdf: the same bitmap with BlackIs1 true and Decode [1 0], and with the defaults (§7.4.6, §8.9.6.2).
        using PdfDocument document = PdfDocument.Open(Corpus.Path("ccitt-blackis1-mask.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(2, recorder.ImageMasks.Count);
        Assert.Equal(["DeviceRgb 1 0 0", "DeviceRgb 0 0 1"], recorder.ImageMasks.Select(static m => m.ColorSpace.Family + " " + string.Join(' ', m.Components)));
        byte[] painted = Painted(recorder.ImageMasks[0].Mask);
        Assert.Equal(painted, Painted(recorder.ImageMasks[1].Mask));
        Assert.Contains((byte)255, painted);
        Assert.Contains((byte)0, painted);
    }

    [Fact]
    public void A_stencil_mask_in_a_pattern_colour_is_not_in_the_list_yet()
    {
        byte[] file = ContentSamples.OnePage(
            Encoding.Latin1.GetBytes("/Pattern cs /P0 scn q 100 0 0 100 72 600 cm BI /W 1 /H 1 /IM true ID \u0000 EI Q"),
            "/Pattern << /P0 << /PatternType 2 /Shading << /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 1 0] "
            + "/Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >> >> >>");
        using PdfDocument document = PdfDocument.Open(file);
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Empty(recorder.ImageMasks);
    }

    [Theory]
    [InlineData("image-explicit-mask.pdf", "image 4x4 DeviceRgb bpc 8 mask 16x16")]
    [InlineData("image-smask.pdf", "image 4x4 DeviceRgb bpc 8 smask 2x8 DeviceGray bpc 8")]
    [InlineData("image-smask-matte.pdf", "image 2x2 DeviceRgb bpc 8 smask 2x2 DeviceGray bpc 8 matte 1 1 1")]
    [InlineData("image-color-key-mask.pdf", "image 4x4 DeviceRgb bpc 8 colorkey 250 255 0 5 0 5")]
    [InlineData("jpx-smask-in-data.pdf", "image 15x11 DeviceRgb bpc 8 smask 15x11 DeviceGray bpc 8")]
    public void A_masked_image_carries_its_mask_at_the_mask_s_own_resolution(string fileName, string expected)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // §8.9.6.3 and §11.6.5.2: a mask and its image both map onto the unit square, so the mask keeps its own size.
        Assert.Contains(expected, recorder.Operations);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_explicit_mask_keeps_its_own_samples()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-explicit-mask.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // The 16 x 16 disc of radius 7 about (7.5, 7.5), sample 0 inside (§8.9.6.3: 0 shows the image with the default Decode).
        DisplayImageMask mask = Assert.Single(recorder.Images).Image.Mask!;
        Assert.False(mask.IsInverted);
        byte[] painted = Painted(mask);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                bool inside = ((x - 7.5) * (x - 7.5)) + ((y - 7.5) * (y - 7.5)) <= 49;
                Assert.Equal(inside ? 255 : 0, painted[(y * 16) + x]);
            }
        }
    }

    [Fact]
    public void Opacity_in_JPEG_2000_data_is_the_image_s_soft_mask()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("jpx-smask-in-data.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // SMaskInData 1 (§8.9.5.1 Table 87, §7.4.9): the codestream's opacity channel, not premultiplied, so no Matte.
        DisplayImage image = Assert.Single(recorder.Images).Image;
        DisplayImage alpha = image.SoftMask!;
        Assert.True(image.Matte.IsEmpty);
        Assert.Equal(
            Enumerable.Range(0, 11).SelectMany(y => Enumerable.Range(0, 15).Select(x => (byte)JpxSamples.Sample(x, y, 3, 8))),
            alpha.Samples.ToArray());
    }

    /// <summary>Returns 255 for each sample of the mask that paints and 0 for each that does not.</summary>
    private static byte[] Painted(DisplayImageMask mask)
    {
        byte[] painted = new byte[mask.Width * mask.Height];
        for (int y = 0; y < mask.Height; y++)
        {
            Broadside.Images.ImageRows.Stencil(mask.GetRow(y), mask.Width, mask.IsInverted, painted.AsSpan(y * mask.Width, mask.Width));
        }

        return painted;
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
