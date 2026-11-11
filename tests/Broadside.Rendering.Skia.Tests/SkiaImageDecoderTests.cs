using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Issue #374: SkiaSharp's JPEG decoder behind the platform image decoder seam, measured against the managed decoder through the
/// verification mode over the DCT vectors and the generated photographs, at full size and each reduction. Gray and 4:4:4 images are
/// bit-identical at full size; subsampled chroma (libjpeg's interpolating upsampling) and reduced decodes stay within the
/// tolerance; what the core's rule leaves out (CMYK, YCCK, 12-bit) never reaches Skia, and data Skia rejects decodes managed with
/// <c>ImageDecodedManaged</c>; an Exif-6 image is not rotated and an ICC-tagged one is not converted.
/// </summary>
/// <remarks>In the non-parallel collection: the document caches' epoch is process-wide (see <c>PlatformImageViewerTests</c>).</remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class SkiaImageDecoderTests
{
    private static readonly string Vectors = Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Filters", "Dct");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, int> EligibleVectors()
    {
        var data = new TheoryData<string, int>();
        foreach (string name in (string[])[
            "testorig", "testimgint", "testimgari", "sampling-444", "sampling-422", "sampling-440", "sampling-420", "sampling-411",
            "gray-2x2", "multiscan", "restart-blocks", "sof1", "no-dht", "progressive", "progressive-gray-2x2", "progressive-restart",
            "progressive-scans", "arithmetic", "arithmetic-progressive", "arithmetic-progressive-scans", "encoded-gray-q90",
            "encoded-rgb-q10-420", "encoded-rgb-q75-420", "encoded-rgb-q90-420", "encoded-rgb-q90-422", "encoded-rgb-q90-444",
            "encoded-rgb-q100-444"])
        {
            for (int reduction = 0; reduction <= 3; reduction++)
            {
                data.Add(name, reduction);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EligibleVectors))]
    public async Task Every_vector_Skia_takes_decodes_bit_identically_and_it_takes_all_but_interpolated_chroma(string name, int reduction)
    {
        byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(Vectors, name + ".jpg"), Token);

        Diagnostic? verified = await VerifyAsync(jpeg, reduction);

        // libjpeg-turbo interpolates 2:1 chroma (4:2:0 at full size; 4:2:2 and 4:4:0 down to 1/4), which the managed decoder
        // replicates: those are left to it. Everything else is libjpeg-turbo's integer IDCT and color transform, the managed ones.
        bool interpolated = name switch
        {
            "sampling-422" or "sampling-440" or "encoded-rgb-q90-422" => reduction < 3,
            _ when Frame(jpeg).Components == 3 && Subsampled(jpeg) && name != "sampling-411" => reduction == 0,
            _ => false,
        };
        Assert.Equal(!interpolated, verified is not null);
        if (verified is not null)
        {
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"at reduction {reduction}: max 0,"), verified.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(DctChromaSubsampling.None, 0)]
    [InlineData(DctChromaSubsampling.None, 1)]
    [InlineData(DctChromaSubsampling.None, 2)]
    [InlineData(DctChromaSubsampling.None, 3)]
    [InlineData(DctChromaSubsampling.HorizontalAndVertical, 0)]
    [InlineData(DctChromaSubsampling.HorizontalAndVertical, 1)]
    [InlineData(DctChromaSubsampling.HorizontalAndVertical, 2)]
    [InlineData(DctChromaSubsampling.HorizontalAndVertical, 3)]
    [InlineData(DctChromaSubsampling.Horizontal, 0)]
    [InlineData(DctChromaSubsampling.Horizontal, 2)]
    [InlineData(DctChromaSubsampling.Horizontal, 3)]
    public async Task The_photograph_decodes_with_Skia_bit_identically_wherever_Skia_takes_it(DctChromaSubsampling subsampling, int reduction)
    {
        Diagnostic? verified = await VerifyAsync(CatalogueSamples.Jpeg(1320, 948, subsampling), reduction);

        bool interpolated = subsampling switch
        {
            DctChromaSubsampling.HorizontalAndVertical => reduction == 0, // measured max 30, 51.9 dB, 1.075% over 2 levels if taken
            DctChromaSubsampling.Horizontal => reduction < 3,
            _ => false,
        };
        Assert.Equal(!interpolated, verified is not null);
        if (verified is not null)
        {
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"at reduction {reduction}: max 0,"), verified.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("cmyk", "/DeviceCMYK")]
    [InlineData("ycck", "/DeviceCMYK")]
    [InlineData("encoded-cmyk-q90", "/DeviceCMYK")]
    [InlineData("precision12", "/DeviceRGB")]
    [InlineData("precision12-gray", "/DeviceGray")]
    [InlineData("precision12-arithmetic", "/DeviceRGB")]
    public async Task CMYK_YCCK_and_12_bit_images_decode_with_the_managed_codec(string name, string colorSpace)
    {
        byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(Vectors, name + ".jpg"), Token);
        (int width, int height, _, int precision) = Frame(jpeg);
        using PdfDocument document = Open(jpeg, width, height, colorSpace);
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device(0), null, 1, Token);
        DisplayImageSamples? samples = Image(list).GetSamplesAt(0, Token);

        Assert.NotNull(samples);
        Assert.DoesNotContain(document.Diagnostics, d => d.Code.StartsWith("PlatformImage", StringComparison.Ordinal));
        Assert.True(precision == 12 || Frame(jpeg).Components == 4);
        Assert.False(SkiaImageDecoder.Shared.CanDecode(new PlatformImageRequest { Filter = new Objects.CosName("DCTDecode"), BitsPerComponent = 12, Components = 3 }));
    }

    [Fact]
    public async Task Data_Skia_rejects_decodes_with_the_managed_codec_and_one_diagnostic()
    {
        // The frame header is intact, so the core offers it; the scan is cut off, so Skia stops short.
        byte[] whole = CatalogueSamples.Jpeg(320, 240, DctChromaSubsampling.None);
        byte[] jpeg = whole[..(whole.Length / 3)];
        using PdfDocument document = Open(jpeg, 320, 240, "/DeviceRGB");
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device(0), null, 1, Token);

        Diagnostic diagnostic = Assert.Single(document.Diagnostics, d => d.Code == "ImageDecodedManaged");
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Contains("'skia'", diagnostic.Message, StringComparison.Ordinal);
        Assert.NotNull(Image(list).GetSamplesAt(0, Token));
    }

    [Fact]
    public async Task An_Exif_6_image_keeps_its_own_orientation_and_an_ICC_tagged_one_its_own_samples()
    {
        // A gamma 1.8 profile: converted to sRGB it would move every mid tone by far more than the tolerance.
        byte[] profile = Broadside.Rendering.Tests.Icc.IccProfiles.Rgb();
        byte[] jpeg = WithExifAndIcc(CatalogueSamples.Jpeg(640, 400, DctChromaSubsampling.None), profile);
        using (SKData data = SKData.CreateCopy(jpeg))
        using (SKCodec codec = SKCodec.Create(data))
        {
            Assert.Equal(SKEncodedOrigin.RightTop, codec.EncodedOrigin);
            Assert.NotNull(codec.Info.ColorSpace);
            Assert.False(codec.Info.ColorSpace.IsSrgb);
        }

        Diagnostic? verified = await VerifyAsync(jpeg, 0);

        Assert.NotNull(verified);
        Assert.Contains("640 x 400", verified.Message, StringComparison.Ordinal);
        Assert.Contains("max 0,", verified.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_image_decodes_with_Skia_only_when_registered_and_not_turned_off()
    {
        byte[] jpeg = CatalogueSamples.Jpeg(320, 240, DctChromaSubsampling.None);
        foreach ((PdfOptions options, bool used) in ((PdfOptions, bool)[])[
            (new PdfOptions(), false),
            (new PdfOptions().UseSkiaImageDecoder(), true),
            (new PdfOptions().UseSkiaImageDecoder().WithPlatformImageDecoder(SkiaImageDecoder.DecoderName, false), false),
            (new PdfOptions().UseSkiaImageDecoder().UseManagedImageDecoding(), false)])
        {
            using PdfDocument document = PdfDocument.Open(Page(jpeg, 320, 240, "/DeviceRGB"), options.WithPlatformImageVerification(true));
            DisplayList list = document.Pages[0].CreateDisplayList();

            await list.PrefetchImagesAsync(Device(0), null, 1, Token);

            Assert.Equal(used, document.Diagnostics.Any(d => d.Code == "PlatformImageVerified"));
        }
    }

    [Theory]
    [InlineData("dct-baseline.pdf", 1)]
    [InlineData("dct-baseline.pdf", 0.25)]
    [InlineData("dct-progressive.pdf", 1)]
    [InlineData("dct-progressive.pdf", 0.25)]
    [InlineData("dct-cmyk.pdf", 0.25)]
    [InlineData("cover-444", 0.25)]
    [InlineData("cover-420", 0.25)]
    [InlineData("cover-420", 1)]
    public async Task Skia_renders_with_the_decoder_on_equal_those_with_it_off(string file, double scale)
    {
        byte[] bytes = file switch
        {
            "cover-444" => CatalogueSamples.Cover(1024, 1024, DctChromaSubsampling.None),
            "cover-420" => CatalogueSamples.Cover(1024, 1024, DctChromaSubsampling.HorizontalAndVertical),
            _ => await File.ReadAllBytesAsync(Corpus.Path(file), Token),
        };

        byte[] off = await RenderAsync(new PdfOptions());
        byte[] on = await RenderAsync(new PdfOptions().UseSkiaImageDecoder());

        Assert.Equal(off, on);

        async Task<byte[]> RenderAsync(PdfOptions options)
        {
            using PdfDocument document = PdfDocument.Open(bytes, options.WithDisplayListCacheBudget(0));
            PdfPage page = document.Pages[0];
            RenderOptions render = new RenderOptions().WithScale(scale);
            PixelSize size = render.GetPixelSize(page);
            Matrix device = render.GetDeviceTransform(page);
            using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            using var backend = new SkiaBackend { ScaledImageDecode = true };
            DisplayList list = page.CreateDisplayList();
            await list.PrefetchImagesAsync(device, null, backend.ImageSamplesPerPixel, Token);
            backend.Render(list, canvas, device);
            return bitmap.Bytes;
        }
    }

    [Theory]
    [InlineData(DctChromaSubsampling.None, 0)]
    [InlineData(DctChromaSubsampling.HorizontalAndVertical, 1)]
    public void A_decode_allocates_per_image_not_per_row(DctChromaSubsampling subsampling, int reduction)
    {
        byte[] shortJpeg = CatalogueSamples.Jpeg(640, 64, subsampling);
        byte[] tall = CatalogueSamples.Jpeg(640, 1024, subsampling);
        PlatformImageRequest shortRequest = Request(64);
        PlatformImageRequest tallRequest = Request(1024);
        Action decodeShort = () => SkiaImageDecoder.Decode(shortJpeg, shortRequest, CancellationToken.None)!.Dispose();
        Action decodeTall = () => SkiaImageDecoder.Decode(tall, tallRequest, CancellationToken.None)!.Dispose();

        long perShort = Allocations.Measure(decodeShort);
        long perTall = Allocations.Measure(decodeTall);

        // The band and the samples are pooled; what is left is the codec's wrappers and the image object, once per image.
        Assert.Equal(perShort, perTall);

        PlatformImageRequest Request(int height) => new()
        {
            Filter = new Objects.CosName("DCTDecode"),
            Width = 640,
            Height = height,
            Components = 3,
            BitsPerComponent = 8,
            ColorTransform = true,
            ChromaSubsampled = subsampling != DctChromaSubsampling.None,
            ChromaHorizontalSubsampling = subsampling == DctChromaSubsampling.None ? 1 : 2,
            ChromaVerticalSubsampling = subsampling == DctChromaSubsampling.None ? 1 : 2,
            Reduction = reduction,
        };
    }

    [Fact]
    public async Task Tiles_that_reach_the_image_at_once_share_one_decode()
    {
        byte[] jpeg = CatalogueSamples.Jpeg(1320, 948, DctChromaSubsampling.None);
        var counting = new CountingDecoder();
        using PdfDocument document = PdfDocument.Open(Page(jpeg, 1320, 948, "/DeviceRGB"), new PdfOptions().UsePlatformImageDecoder(counting));
        DisplayList list = document.Pages[0].CreateDisplayList();
        using var start = new ManualResetEventSlim();

        // Eight tiles prefetch at once, as the viewer's pooled tiles do: Skia decodes synchronously, and the others join that decode.
        Task[] tiles = [.. Enumerable.Range(0, 8).Select(_ => Task.Run(
            async () =>
            {
                start.Wait(Token);
                await list.PrefetchImagesAsync(Device(1), null, 1, Token);
            },
            Token))];
        start.Set();
        await Task.WhenAll(tiles);

        Assert.Equal(1, counting.Calls);
    }

    /// <summary>Skia's decoder, counting its decodes.</summary>
    private sealed class CountingDecoder : IPlatformImageDecoder
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string Name => "counting";

        public PlatformImageFidelity Fidelity => SkiaImageDecoder.Shared.Fidelity;

        public PlatformImageFeatures Features => SkiaImageDecoder.Shared.Features;

        public bool CanDecode(in PlatformImageRequest request) => SkiaImageDecoder.Shared.CanDecode(request);

        public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return SkiaImageDecoder.Shared.DecodeAsync(encoded, request, cancellationToken);
        }
    }

    [Fact]
    public void Packing_RGBA_rows_keeps_every_pixel()
    {
        for (int width = 1; width <= 40; width++)
        {
            byte[] rgbx = [.. Enumerable.Range(0, width * 4).Select(i => (byte)(i % 4 == 3 ? 0xEE : i * 7))];
            byte[] rgb = new byte[(width * 3) + 5];
            rgb.AsSpan(width * 3).Fill(0x55);

            SkiaImageDecoder.PackRgb(rgbx, rgb.AsSpan(0, width * 3));

            for (int x = 0; x < width; x++)
            {
                Assert.Equal(rgbx.AsSpan(x * 4, 3).ToArray(), rgb.AsSpan(x * 3, 3).ToArray());
            }

            Assert.All(rgb.AsSpan(width * 3).ToArray(), b => Assert.Equal(0x55, b));
        }
    }

    /// <summary>
    /// Prefetches the image with Skia at <paramref name="reduction"/> under verification and returns the <c>PlatformImageVerified</c>
    /// diagnostic, or <see langword="null"/> when Skia did not take the image (the test fails on <c>PlatformImageMismatch</c> or a
    /// fallback).
    /// </summary>
    private static async Task<Diagnostic?> VerifyAsync(byte[] jpeg, int reduction)
    {
        (int width, int height, int components, _) = Frame(jpeg);
        using PdfDocument document = Open(jpeg, width, height, components == 1 ? "/DeviceGray" : "/DeviceRGB");
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device(reduction), null, 1, Token);

        Diagnostic? diagnostic = document.Diagnostics.SingleOrDefault(d => d.Code is "PlatformImageVerified" or "PlatformImageMismatch" or "ImageDecodedManaged");
        if (diagnostic is null)
        {
            return null;
        }

        TestContext.Current.TestOutputHelper?.WriteLine(diagnostic.Message);
        Assert.True(diagnostic.Code == "PlatformImageVerified", diagnostic.Message);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"at reduction {Math.Min(reduction, Image(list).MaxReduction)}:"), diagnostic.Message, StringComparison.Ordinal);
        return diagnostic;
    }

    // The image fills W x H points; drawn at 1/2^r of that, with one sample per pixel, the backend asks for reduction r.
    private static Matrix Device(int reduction) => Matrix.CreateScale(1.0 / (1 << reduction), 1.0 / (1 << reduction));

    private static PdfDocument Open(byte[] jpeg, int width, int height, string colorSpace) =>
        PdfDocument.Open(Page(jpeg, width, height, colorSpace), new PdfOptions().UseSkiaImageDecoder().WithPlatformImageVerification(true));

    private static byte[] Page(byte[] jpeg, int width, int height, string colorSpace) => ContentSamples.OnePage(
        Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"q {width} 0 0 {height} 0 0 cm /Im0 Do Q")),
        "/XObject << /Im0 5 0 R >>",
        ContentSamples.Stream(
            Encoding.Latin1.GetString(jpeg),
            string.Create(CultureInfo.InvariantCulture, $"/Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace {colorSpace} /BitsPerComponent 8 /Filter /DCTDecode")));

    private static DisplayImage Image(DisplayList list)
    {
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        return Assert.Single(recorder.Images).Image;
    }

    /// <summary>The frame header's size, component count and precision (ITU-T T.81 B.2.2).</summary>
    private static (int Width, int Height, int Components, int Precision) Frame(byte[] jpeg)
    {
        for (int i = 2; i + 9 < jpeg.Length;)
        {
            if (jpeg[i] != 0xFF)
            {
                i++;
                continue;
            }

            byte marker = jpeg[i + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return ((jpeg[i + 7] << 8) | jpeg[i + 8], (jpeg[i + 5] << 8) | jpeg[i + 6], jpeg[i + 9], jpeg[i + 4]);
            }

            i += 2 + ((jpeg[i + 2] << 8) | jpeg[i + 3]);
        }

        throw new InvalidDataException("No frame header.");
    }

    /// <summary>Whether a component is sampled at another rate than the first (4:2:0, 4:2:2, ...).</summary>
    private static bool Subsampled(byte[] jpeg)
    {
        for (int i = 2; i + 9 < jpeg.Length;)
        {
            byte marker = jpeg[i + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                int components = jpeg[i + 9];
                for (int c = 1; c < components; c++)
                {
                    if (jpeg[i + 11 + (3 * c)] != jpeg[i + 11])
                    {
                        return true;
                    }
                }

                return false;
            }

            i += 2 + ((jpeg[i + 2] << 8) | jpeg[i + 3]);
        }

        return false;
    }

    /// <summary>Inserts an APP1 Exif segment (orientation 6) and an APP2 segment with <paramref name="profile"/> after the SOI marker.</summary>
    private static byte[] WithExifAndIcc(byte[] jpeg, byte[] profile)
    {
        byte[] exif = [.. "Exif\0\0MM\0*\0\0\0\b\0\u0001\u0001\u0012\0\u0003\0\0\0\u0001\0\u0006\0\0\0\0\0\0"u8];
        byte[] icc = [.. "ICC_PROFILE\0\u0001\u0001"u8, .. profile];
        return [0xFF, 0xD8, .. Segment(0xE1, exif), .. Segment(0xE2, icc), .. jpeg.AsSpan(2)];

        static byte[] Segment(byte marker, byte[] payload) =>
            [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];
    }
}
