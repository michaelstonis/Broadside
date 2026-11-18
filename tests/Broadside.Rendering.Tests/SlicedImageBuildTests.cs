using System.Text;
using Broadside.Content;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #566: a display list built in slices decodes a Flate image (with its predictor) and its soft mask a few chunks or rows at a
/// time before the image's <c>Do</c>, instead of inside it. The list, its samples, its pixels and the document's diagnostics equal
/// those of a list built whole, for well-formed and damaged data alike (ISO 32000-2 §7.4.4, §8.9.5, §11.6.5.2).
/// </summary>
/// <remarks>In the Heavy collection, as <see cref="SlicedPreparationTests"/>: the document's caches use a process-wide epoch.</remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class SlicedImageBuildTests
{
    private const int Width = 320;
    private const int Height = 240;

    public static TheoryData<string> Variants => new(
        "png", "png-no-mask", "tiff", "truncated", "corrupt", "unknown-tag", "partial-row", "raw-deflate", "lzw", "chain", "too-long", "mask-own-size");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Variants))]
    public void A_list_built_in_slices_decodes_its_flate_images_as_a_list_built_whole(string variant)
    {
        byte[] file = Page(variant);
        using PdfDocument sliced = PdfDocument.Open(file);
        using PdfDocument whole = PdfDocument.Open(file);
        using DisplayListBuild build = sliced.Pages[0].BeginDisplayList(DisplayLayers.Content, DisplayIntent.View, new ContentOptions().WithCancellation(Token));
        int slices = 1;
        while (!build.Continue(TimeSpan.Zero))
        {
            slices++;
        }

        DisplayList actual = build.Result;
        DisplayList expected = whole.Pages[0].CreateDisplayList(DisplayLayers.Content, DisplayIntent.View, new ContentOptions().WithCancellation(Token));
        Assert.Equal(expected.OperationCount, actual.OperationCount);
        Assert.Equal(Describe(whole), Describe(sliced));

        DisplayImage[] expectedImages = Images(expected);
        DisplayImage[] actualImages = Images(actual);
        Assert.Equal(expectedImages.Length, actualImages.Length);
        for (int i = 0; i < expectedImages.Length; i++)
        {
            AssertSameSamples(expectedImages[i], actualImages[i], variant);
            Assert.Equal(expectedImages[i].SoftMask is null, actualImages[i].SoftMask is null);
            if (expectedImages[i].SoftMask is { } expectedMask)
            {
                AssertSameSamples(expectedMask, actualImages[i].SoftMask!, variant + " soft mask");
            }
        }

        Assert.True(Draw(expected, whole.Pages[0]).AsSpan().SequenceEqual(Draw(actual, sliced.Pages[0])), $"{variant}: the list built in {slices} slices draws differently.");
        if (variant == "png")
        {
            // Four operators, but the image's inflation and predictor rows pause many times: the decode is no longer one step.
            Assert.True(slices > 10, $"{variant}: {slices} slices");
        }
    }

    [Fact]
    public void A_build_cancelled_while_an_image_decodes_in_slices_stops_and_a_later_build_decodes_it_whole()
    {
        byte[] file = Page("png");
        using PdfDocument document = PdfDocument.Open(file);
        using var cancellation = new CancellationTokenSource();
        DisplayListBuild build = document.Pages[0].BeginDisplayList(DisplayLayers.Content, DisplayIntent.View, new ContentOptions().WithCancellation(cancellation.Token));
        for (int i = 0; i < 4; i++)
        {
            Assert.False(build.Continue(TimeSpan.Zero));
        }

        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => build.Continue(TimeSpan.Zero));
        Assert.Throws<ObjectDisposedException>(() => build.Continue(TimeSpan.Zero));
        build.Dispose();

        using PdfDocument whole = PdfDocument.Open(file);
        DisplayList list = document.Pages[0].CreateDisplayList();
        DisplayList expected = whole.Pages[0].CreateDisplayList();
        AssertSameSamples(Images(expected)[0], Images(list)[0], "after a cancelled build");
    }

    [Fact]
    public void A_list_built_in_slices_reuses_an_image_the_document_already_decoded()
    {
        byte[] file = Page("png");
        using PdfDocument document = PdfDocument.Open(file);
        DisplayImage first = Images(document.Pages[0].CreateDisplayList(DisplayLayers.Content, DisplayIntent.Print, new ContentOptions()))[0];
        using DisplayListBuild build = document.Pages[0].BeginDisplayList(DisplayLayers.Content, DisplayIntent.View, new ContentOptions());
        int slices = 1;
        while (!build.Continue(TimeSpan.Zero))
        {
            slices++;
        }

        Assert.Same(first, Images(build.Result)[0]);
        Assert.True(slices < 4, $"{slices} slices");
    }

    private static void AssertSameSamples(DisplayImage expected, DisplayImage actual, string what)
    {
        Assert.Equal(expected.Geometry, actual.Geometry);
        Assert.True(expected.Samples.SequenceEqual(actual.Samples), $"{what}: the samples differ.");
    }

    private static string[] Describe(PdfDocument document) =>
        [.. document.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Severity} {diagnostic.Message}")];

    private static byte[] Draw(DisplayList list, PdfPage page)
    {
        RenderOptions options = new RenderOptions().WithScale(1);
        PixelRectangle area = options.GetPixelRectangle(page);
        using var rasterizer = new SoftwareRasterizer();
        using var bitmap = new Bitmap(area.Width, area.Height);
        rasterizer.Render(list, bitmap, options.GetDeviceTransform(page));
        return bitmap.Pixels.ToArray();
    }

    private static DisplayImage[] Images(DisplayList list)
    {
        var collector = new RecordingBackend();
        list.Replay(collector);
        return [.. collector.Images.Select(entry => entry.Image).Distinct()];
    }

    /// <summary>A page painting one 320 x 240 RGB image, Flate-encoded with a predictor and a soft mask, damaged as the variant says.</summary>
    private static byte[] Page(string variant)
    {
        byte[] color = Samples(Width, Height, 3, seed: 7);
        byte[] gray = Samples(Width, Height, 1, seed: 11);
        string pngParameters = FormattableString.Invariant($"/DecodeParms << /Predictor 15 /Colors 3 /Columns {Width} >>");
        (byte[] data, string filter) = variant switch
        {
            "png" or "png-no-mask" or "mask-own-size" => (FilterEncoders.Zlib(Png(color, 3)), "/Filter /FlateDecode " + pngParameters),
            "tiff" => (FilterEncoders.Zlib(Tiff(color, 3)), FormattableString.Invariant($"/Filter /FlateDecode /DecodeParms << /Predictor 2 /Colors 3 /Columns {Width} >>")),
            "truncated" => (FilterEncoders.Zlib(Png(color, 3))[..40_000], "/Filter /FlateDecode " + pngParameters),
            "corrupt" => (Corrupt(FilterEncoders.Zlib(Png(color, 3))), "/Filter /FlateDecode " + pngParameters),
            "unknown-tag" => (FilterEncoders.Zlib(Png(color, 3, unknownTagEvery: 9)), "/Filter /FlateDecode " + pngParameters),
            "partial-row" => (FilterEncoders.Zlib(Png(color, 3).AsSpan(0, (Height * ((Width * 3) + 1)) - 500)), "/Filter /FlateDecode " + pngParameters),
            "raw-deflate" => (FilterEncoders.Zlib(Png(color, 3))[2..], "/Filter /FlateDecode " + pngParameters),
            "lzw" => (FilterEncoders.LzwEncode(Png(color, 3)), "/Filter /LZWDecode " + pngParameters),
            "chain" => (Encoding.ASCII.GetBytes(Convert.ToHexString(FilterEncoders.Zlib(Png(color, 3))) + ">"), "/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null << /Predictor 15 /Colors 3 /Columns 320 >>]"),
            "too-long" => (FilterEncoders.Zlib([.. Png(color, 3), .. Png(color, 3)]), "/Filter /FlateDecode " + pngParameters),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
        };

        (int maskWidth, int maskHeight) = variant == "mask-own-size" ? (160, 120) : (Width, Height);
        byte[] maskSamples = variant == "mask-own-size" ? Samples(maskWidth, maskHeight, 1, seed: 11) : gray;
        string mask = ContentSamples.Stream(
            Encoding.Latin1.GetString(FilterEncoders.Zlib(Png(maskSamples, 1, maskWidth))),
            FormattableString.Invariant($"/Type /XObject /Subtype /Image /Width {maskWidth} /Height {maskHeight} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Columns {maskWidth} >>"));
        string smask = variant == "png-no-mask" ? string.Empty : " /SMask 6 0 R";
        string image = ContentSamples.Stream(
            Encoding.Latin1.GetString(data),
            FormattableString.Invariant($"/Type /XObject /Subtype /Image /Width {Width} /Height {Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 {filter}{smask}"));
        return ContentSamples.OnePage(Encoding.ASCII.GetBytes("0 0 1 rg 0 0 612 792 re f q 400 0 0 300 100 200 cm /Im0 Do Q"), "/XObject << /Im0 5 0 R >>", image, mask);
    }

    /// <summary>Smooth gradients with noise, so that every PNG algorithm has work to do.</summary>
    private static byte[] Samples(int width, int height, int components, int seed)
    {
        uint state = (uint)seed;
        byte[] samples = new byte[width * height * components];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < components; c++)
                {
                    samples[(((y * width) + x) * components) + c] = (byte)((x * (c + 1)) + (y * 2) + (int)((state = (state * 1664525) + 1013904223) >> 29));
                }
            }
        }

        return samples;
    }

    /// <summary>PNG-predicts rows (ISO/IEC 15948 §9), the algorithm cycling 0 to 4 row by row; every <paramref name="unknownTagEvery"/>th row is tagged 7.</summary>
    private static byte[] Png(byte[] samples, int components, int width = Width, int unknownTagEvery = 0)
    {
        int stride = width * components;
        int rows = samples.Length / stride;
        byte[] output = new byte[rows * (stride + 1)];
        for (int y = 0; y < rows; y++)
        {
            int tag = y % 5;
            output[y * (stride + 1)] = unknownTagEvery > 0 && y % unknownTagEvery == 0 ? (byte)7 : (byte)tag;
            for (int i = 0; i < stride; i++)
            {
                int raw = samples[(y * stride) + i];
                int left = i >= components ? samples[(y * stride) + i - components] : 0;
                int above = y > 0 ? samples[((y - 1) * stride) + i] : 0;
                int upperLeft = y > 0 && i >= components ? samples[((y - 1) * stride) + i - components] : 0;
                int predicted = tag switch
                {
                    1 => left,
                    2 => above,
                    3 => (left + above) >> 1,
                    4 => Paeth(left, above, upperLeft),
                    _ => 0,
                };
                output[(y * (stride + 1)) + 1 + i] = unknownTagEvery > 0 && y % unknownTagEvery == 0 ? (byte)raw : (byte)(raw - predicted);
            }
        }

        return output;
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int toLeft = Math.Abs(estimate - left);
        int toAbove = Math.Abs(estimate - above);
        int toUpperLeft = Math.Abs(estimate - upperLeft);
        return toLeft <= toAbove && toLeft <= toUpperLeft ? left : toAbove <= toUpperLeft ? above : upperLeft;
    }

    /// <summary>TIFF Predictor 2 (§7.4.4.4): each component minus the same component of the sample to its left.</summary>
    private static byte[] Tiff(byte[] samples, int components)
    {
        int stride = Width * components;
        byte[] output = new byte[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            output[i] = (byte)(samples[i] - ((i % stride) >= components ? samples[i - components] : 0));
        }

        return output;
    }

    /// <summary>Overwrites the middle of the deflate data, so that inflating it fails part way.</summary>
    private static byte[] Corrupt(byte[] zlib)
    {
        byte[] damaged = zlib.ToArray();
        for (int i = damaged.Length / 2; i < (damaged.Length / 2) + 64; i++)
        {
            damaged[i] = (byte)(0xFF - (i & 0x0F));
        }

        return damaged;
    }
}
