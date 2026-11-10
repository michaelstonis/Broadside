using System.Diagnostics;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Issue #377, step 2: <see cref="WicImageDecoder"/> through the platform decoder seam, on Windows. Every eligible DCT vector and
/// photograph decodes bit-identical to the managed decoder at full size and at each reduction (WIC's pixels without subsampling, its
/// YCbCr planes with the managed chroma replication and transform with it), arithmetic coding included; the verification mode agrees;
/// four-component and 12-bit data is never offered, and a subsampling WIC has no planes for decodes managed with a diagnostic; a
/// render whose images were prefetched through WIC equals one without it; and WIC is faster than the managed decoder on the
/// 8-megapixel photograph.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class WicImageDecoderTests
{
    private static readonly string Vectors = Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Filters", "Dct");

    [Fact]
    public void The_decoder_is_exact_and_takes_arithmetic_coding()
    {
        Assert.Equal(PlatformImageFidelity.Exact, WicImageDecoder.Shared.Fidelity);
        Assert.Equal(PlatformImageFeatures.ArithmeticCoding, WicImageDecoder.Shared.Features);
        Assert.Equal("wic", WicImageDecoder.Shared.Name);
        using PdfDocument document = PdfDocument.Open(JpegPage(DctVectors("sampling-444")), new PdfOptions().UseWicImageDecoder());
        Assert.True(document.UsesPlatformImageDecoder(WicImageDecoder.DecoderName));
    }

    [Fact]
    public void Off_Windows_nothing_is_offered()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows WIC is available.");
        Assert.False(WicImageDecoder.IsSupported);
        Assert.False(WicImageDecoder.Shared.CanDecode(Request(DctVectors("sampling-444"), new ImageHeader(75, 43, 3, 8), 0)));
    }

    [Fact]
    public async Task Every_eligible_image_decodes_bit_identical_to_the_managed_decoder_at_every_reduction()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "WIC is Windows-only.");
        Assert.True(WicImageDecoder.IsSupported);
        int compared = 0;
        foreach ((string name, byte[] jpeg) in WicMeasurementTests.Images())
        {
            if (!TryHeader(jpeg, out ImageHeader header))
            {
                continue;
            }

            for (int reduction = 0; reduction <= 3; reduction++)
            {
                PlatformImageRequest request = Request(jpeg, header, reduction);
                Assert.True(WicImageDecoder.Shared.CanDecode(request), name);
                if (name == "sampling-411")
                {
                    // 4:1:1: WIC has no planes for it and interpolates its pixels, so the image is left to the managed decoder.
                    await Assert.ThrowsAsync<InvalidOperationException>(async () => await WicImageDecoder.Shared.DecodeAsync(jpeg, request, TestContext.Current.CancellationToken));
                    continue;
                }

                using DecodedImage? wic = await WicImageDecoder.Shared.DecodeAsync(jpeg, request, TestContext.Current.CancellationToken);
                Assert.NotNull(wic);
                Assert.Equal(reduction, wic.Reduction);
                using DecodedImage? managed = WicMeasurementTests.Managed(jpeg, header, reduction);
                Assert.NotNull(managed);
                (int max, double psnr, double over) = WicMeasurementTests.Compare(wic, managed);
                Assert.True(max == 0, $"{name} at reduction {reduction}: max {max}, {psnr:0.0} dB, {over:P3} over 2 levels");
                compared++;
            }
        }

        Assert.True(compared >= 100, $"Only {compared} decodes were compared.");
    }

    [Theory]
    [InlineData(DctChromaSubsampling.None, 1.0)]
    [InlineData(DctChromaSubsampling.HorizontalAndVertical, 1.0)]
    [InlineData(DctChromaSubsampling.HorizontalAndVertical, 0.2)]
    public void A_prefetch_through_WIC_is_verified_bit_identical_and_the_render_equals_the_managed_one(DctChromaSubsampling subsampling, double scale)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "WIC is Windows-only.");
        byte[] file = JpegPage(CatalogueSamples.Jpeg(1321, 949, subsampling));
        using PdfDocument managedDocument = PdfDocument.Open(file, new PdfOptions().WithDisplayListCacheBudget(0));
        using PdfDocument wicDocument = PdfDocument.Open(file, new PdfOptions().UseWicImageDecoder().WithPlatformImageVerification(true).WithDisplayListCacheBudget(0));

        using Bitmap expected = Render(managedDocument, scale, prefetch: false);
        using Bitmap actual = Render(wicDocument, scale, prefetch: true);

        Diagnostic verified = Assert.Single(wicDocument.Diagnostics, Platform);
        Assert.Equal("PlatformImageVerified", verified.Code);
        Assert.Contains("'wic' (Exact)", verified.Message, StringComparison.Ordinal);
        Assert.Contains("max 0,", verified.Message, StringComparison.Ordinal);
        // Drawn at a fifth with two samples per pixel, the backend asks for a half (2.5 samples a pixel), and WIC delivers it.
        Assert.Contains(scale < 1 ? "at reduction 1:" : "at reduction 0:", verified.Message, StringComparison.Ordinal);
        Assert.True(expected.Pixels.SequenceEqual(actual.Pixels), "The render through WIC differs from the managed one.");
    }

    [Theory]
    [InlineData("encoded-gray-q90")]
    [InlineData("arithmetic")]
    [InlineData("arithmetic-progressive")]
    [InlineData("progressive")]
    public void Gray_progressive_and_arithmetic_coded_images_are_verified_bit_identical(string vector)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "WIC is Windows-only.");
        using PdfDocument document = PdfDocument.Open(JpegPage(DctVectors(vector)), new PdfOptions().UseWicImageDecoder().WithPlatformImageVerification(true));

        using Bitmap rendered = Render(document, 1, prefetch: true);

        Diagnostic verified = Assert.Single(document.Diagnostics, Platform);
        Assert.Equal("PlatformImageVerified", verified.Code);
        Assert.Contains("max 0,", verified.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cmyk")]
    [InlineData("ycck")]
    [InlineData("precision12")]
    [InlineData("precision12-gray")]
    public void Four_component_and_twelve_bit_data_is_never_offered_and_decodes_with_the_managed_codec(string vector)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "WIC is Windows-only.");
        byte[] file = JpegPage(DctVectors(vector));
        using PdfDocument managedDocument = PdfDocument.Open(file);
        using PdfDocument wicDocument = PdfDocument.Open(file, new PdfOptions().UseWicImageDecoder().WithPlatformImageVerification(true));

        using Bitmap expected = Render(managedDocument, 1, prefetch: false);
        using Bitmap actual = Render(wicDocument, 1, prefetch: true);

        Assert.DoesNotContain(wicDocument.Diagnostics, Platform);
        Assert.True(expected.Pixels.SequenceEqual(actual.Pixels), "The render differs from the managed one.");
    }

    [Fact]
    public void A_subsampling_WIC_has_no_planes_for_decodes_managed_with_a_diagnostic()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "WIC is Windows-only.");
        byte[] file = JpegPage(DctVectors("sampling-411"));
        using PdfDocument managedDocument = PdfDocument.Open(file);
        using PdfDocument wicDocument = PdfDocument.Open(file, new PdfOptions().UseWicImageDecoder());

        using Bitmap expected = Render(managedDocument, 1, prefetch: false);
        using Bitmap actual = Render(wicDocument, 1, prefetch: true);

        Diagnostic diagnostic = Assert.Single(wicDocument.Diagnostics, Platform);
        Assert.Equal(("ImageDecodedManaged", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
        Assert.Contains("no Y, Cb, Cr planes", diagnostic.Message, StringComparison.Ordinal);
        Assert.True(expected.Pixels.SequenceEqual(actual.Pixels), "The render differs from the managed one.");
    }

    [Theory]
    [InlineData(DctChromaSubsampling.None)]
    [InlineData(DctChromaSubsampling.HorizontalAndVertical)]
    public async Task WIC_decodes_the_8_megapixel_photograph_faster_than_the_managed_decoder(DctChromaSubsampling subsampling)
    {
        // The issue's gate: the managed-to-WIC ratio on the cover-class photograph, full size (measured 1.5x to 2.2x on the runner).
        Assert.SkipUnless(OperatingSystem.IsWindows(), "WIC is Windows-only.");
        byte[] jpeg = CatalogueSamples.Jpeg(2550, 3150, subsampling);
        Assert.True(TryHeader(jpeg, out ImageHeader header));
        PlatformImageRequest request = Request(jpeg, header, 0);
        double managed = double.MaxValue, wic = double.MaxValue;
        for (int run = 0; run < 3; run++)
        {
            long start = Stopwatch.GetTimestamp();
            using (WicMeasurementTests.Managed(jpeg, header, 0))
            {
                managed = Math.Min(managed, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            start = Stopwatch.GetTimestamp();
            using (await WicImageDecoder.Shared.DecodeAsync(jpeg, request, TestContext.Current.CancellationToken))
            {
                wic = Math.Min(wic, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{subsampling}: managed {managed:0.0} ms, WIC {wic:0.0} ms ({managed / wic:0.00}x)");
        Assert.True(wic < managed, $"WIC took {wic:0.0} ms, the managed decoder {managed:0.0} ms.");
    }

    private static bool Platform(Diagnostic d) => d.Code.StartsWith("Platform", StringComparison.Ordinal) || d.Code == "ImageDecodedManaged";

    private static byte[] DctVectors(string name) => File.ReadAllBytes(Path.Combine(Vectors, name + ".jpg"));

    private static Bitmap Render(PdfDocument document, double scale, bool prefetch)
    {
        PdfPage page = document.Pages[0];
        int width = (int)Math.Ceiling(page.MediaBox.Width * scale);
        int height = (int)Math.Ceiling(page.MediaBox.Height * scale);
        var device = new Matrix(scale, 0, 0, -scale, -page.MediaBox.Left * scale, page.MediaBox.Top * scale);
        DisplayList list = page.CreateDisplayList();
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp) { ScaledImageDecode = true };
        if (prefetch)
        {
            renderer.PrefetchImagesAsync(list, device, width, height, TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult();
        }

        var bitmap = new Bitmap(width, height);
        bitmap.Pixels.Fill(0xFF);
        renderer.Render(list, bitmap, device);
        return bitmap;
    }

    /// <summary>A page of the JPEG's size, the JPEG placed byte for byte over it.</summary>
    private static byte[] JpegPage(byte[] jpeg)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImageFromJpeg(jpeg);
        PdfPage page = document.Pages.Count > 0 ? document.Pages[0] : document.Pages.Add(new PdfRectangle(0, 0, image.Width, image.Height));
        page.MediaBox = new PdfRectangle(0, 0, image.Width, image.Height);
        using (PdfCanvas canvas = page.CreateCanvas())
        {
            canvas.DrawImage(image, 0, 0, image.Width, image.Height);
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static bool TryHeader(byte[] jpeg, out ImageHeader header) =>
        new DctDecodeFilter().TryReadHeader(jpeg, new ImageFilterContext(new FilterContext()), out header)
        && header.BitsPerComponent == 8 && header.Components is 1 or 3 && header.Height > 0;

    private static PlatformImageRequest Request(byte[] jpeg, ImageHeader header, int reduction) => new()
    {
        Filter = new CosName("DCTDecode"),
        Width = header.Width,
        Height = header.Height,
        Components = header.Components,
        BitsPerComponent = 8,
        ColorTransform = header.Components == 3,
        ChromaSubsampled = Subsampled(jpeg),
        ArithmeticCoding = Marker(jpeg) is 0xC9 or 0xCA,
        Progressive = Marker(jpeg) is 0xC2 or 0xCA,
        Reduction = reduction,
    };

    /// <summary>The frame's SOF marker code (ITU-T T.81 B.1.1.3), or 0.</summary>
    private static int Marker(byte[] jpeg) => FindFrame(jpeg) is int at and >= 0 ? jpeg[at + 1] : 0;

    /// <summary>Whether a component of the frame is sampled at another rate than the first (ITU-T T.81 B.2.2).</summary>
    private static bool Subsampled(byte[] jpeg)
    {
        int at = FindFrame(jpeg);
        if (at < 0)
        {
            return false;
        }

        int components = jpeg[at + 9];
        byte first = jpeg[at + 11];
        for (int c = 1; c < components; c++)
        {
            if (jpeg[at + 11 + (3 * c)] != first)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The offset of the frame header's marker (FF Cn, not DHT, JPG or DAC), or -1.</summary>
    private static int FindFrame(byte[] jpeg)
    {
        for (int i = 2; i + 3 < jpeg.Length;)
        {
            if (jpeg[i] != 0xFF)
            {
                return -1;
            }

            int marker = jpeg[i + 1];
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                return i;
            }

            i += 2 + ((jpeg[i + 2] << 8) | jpeg[i + 3]);
        }

        return -1;
    }
}
