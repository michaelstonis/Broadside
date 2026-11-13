using System.Globalization;
using System.Text;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.TestSupport;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Issue #375: the ImageIO decoder behind the platform image decoder seam. It takes 8-bit gray and 4:4:4 JPEG data at full size
/// whose quantizer is fine enough, and stays within the seam's full-size tolerance there (max 24 levels, 50 dB, 1% of pixels over 2
/// levels against the managed decoder, measured with the verification mode); everything else decodes with the managed codec.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ImageIODecoderTests
{
    private static readonly Matrix Device = new(1, 0, 0, -1, 0, 792);

    [Fact]
    public async Task A_full_size_4_4_4_photograph_decodes_with_ImageIO_within_the_tolerance()
    {
        using PdfDocument document = Open(CatalogueSamples.Cover());
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        Diagnostics.Diagnostic verified = Assert.Single(document.Diagnostics);
        Assert.Equal("PlatformImageVerified", verified.Code);
        Assert.Contains("'imageio'", verified.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string> Vectors => [.. Directory.GetFiles(ImageIOTesting.VectorDirectory, "*.jpg").Select(file => Path.GetFileNameWithoutExtension(file)!).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Every_DCT_vector_ImageIO_takes_is_within_the_full_size_tolerance(string name)
    {
        byte[] jpeg = ImageIOTesting.Vector(name);
        if (ImageIOTesting.Request(jpeg, 0) is not { } request || !ImageIODecoder.Shared.CanDecode(request)
            || !(ImageIODecoder.MeanQuantizerStep(jpeg) <= ImageIODecoder.MaxMeanQuantizerStep))
        {
            return;
        }

        using DecodedImage? platform = ImageIODecoder.Decode(jpeg, request, 0);
        using DecodedImage? managed = ImageIOTesting.Managed(jpeg, request);

        AssertWithinTolerance(ImageIOTesting.Compare(platform!, managed!), name);
    }

    [Theory]
    [InlineData(64, 64, DctChromaSubsampling.None)]
    [InlineData(301, 97, DctChromaSubsampling.None)]
    [InlineData(1024, 768, DctChromaSubsampling.None)]
    public void Generated_photographs_at_quality_90_are_within_the_full_size_tolerance(int width, int height, DctChromaSubsampling subsampling)
    {
        byte[] jpeg = CatalogueSamples.Jpeg(width, height, subsampling);
        PlatformImageRequest request = ImageIOTesting.Request(jpeg, 0)!.Value;

        using DecodedImage? platform = ImageIODecoder.Decode(jpeg, request, 0);
        using DecodedImage? managed = ImageIOTesting.Managed(jpeg, request);

        Assert.True(ImageIODecoder.MeanQuantizerStep(jpeg) <= ImageIODecoder.MaxMeanQuantizerStep);
        AssertWithinTolerance(ImageIOTesting.Compare(platform!, managed!), $"{width} x {height}");
    }

    [Theory]
    [InlineData("dct-baseline.pdf")]
    [InlineData("dct-progressive.pdf")]
    [InlineData("dct-cmyk.pdf")]
    public async Task Corpus_DCT_files_never_mismatch_in_verification_mode(string file)
    {
        // Their images are 4:2:0 (never offered), quality 50 (refused, Information), or CMYK (never offered).
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file), Options());
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(document.Diagnostics, d => d.Code == "PlatformImageMismatch");
        Assert.All(document.Diagnostics, d => Assert.Contains("quantizer", d.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cmyk", "/DeviceCMYK")]
    [InlineData("ycck", "/DeviceCMYK")]
    [InlineData("precision12", "/DeviceRGB")]
    [InlineData("precision12-gray", "/DeviceGray")]
    [InlineData("encoded-rgb-q90-420", "/DeviceRGB")]
    [InlineData("encoded-rgb-q90-422", "/DeviceRGB")]
    public async Task Four_component_twelve_bit_and_subsampled_data_decode_with_the_managed_codec(string name, string colorSpace)
    {
        byte[] page = Page(ImageIOTesting.Vector(name), colorSpace, 75, 43);
        using PdfDocument document = Open(page);
        using PdfDocument managed = PdfDocument.Open(page);
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        // Never offered: no diagnostic of the seam, and the replay's samples are the managed decoder's.
        Assert.Empty(document.Diagnostics);
        Assert.Equal(Samples(managed.Pages[0].CreateDisplayList()), Samples(list));
    }

    // Issue #484: ImageIO decodes on the calling thread and returns a completed task; pooled tiles reaching the image at once share it.
    [Fact]
    public async Task Concurrent_prefetches_from_many_threads_decode_the_image_once()
    {
        const int count = 8;
        var decoder = new CountingDecoder(ImageIODecoder.Shared);
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().UsePlatformImageDecoder(decoder).WithDisplayListCacheBudget(0));
        DisplayList list = document.Pages[0].CreateDisplayList();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var start = new Barrier(count);

        Task[] prefetches = [.. Enumerable.Range(0, count).Select(_ => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait(cancellationToken);
                return list.PrefetchImagesAsync(Device, null, 0, cancellationToken).AsTask();
            },
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap())];
        await Task.WhenAll(prefetches);

        Assert.Equal(1, decoder.Calls);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public async Task A_coarse_quantizer_falls_back_to_the_managed_codec_with_one_diagnostic()
    {
        // cjpeg's default quality 75: ImageIO's IDCT is 44 dB from the managed decoder's there.
        byte[] jpeg = ImageIOTesting.Vector("sampling-444");
        using PdfDocument document = Open(Page(jpeg, "/DeviceRGB", 75, 43));
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);
        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        Diagnostics.Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("ImageDecodedManaged", Diagnostics.DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
        Assert.Contains("quantizer", diagnostic.Message, StringComparison.Ordinal);
        Assert.True(ImageIODecoder.MeanQuantizerStep(jpeg) > ImageIODecoder.MaxMeanQuantizerStep);
    }

    [Fact]
    public async Task A_reduced_request_is_never_offered()
    {
        // A quarter-scale view of the cover asks for a reduction: the managed decoder's reduced decode serves it.
        using PdfDocument document = Open(CatalogueSamples.Cover());
        DisplayList list = document.Pages[0].CreateDisplayList();
        var quarter = new Matrix(0.25, 0, 0, -0.25, 0, 198);

        await list.PrefetchImagesAsync(quarter, null, 2, TestContext.Current.CancellationToken);

        Assert.Empty(document.Diagnostics);
        Assert.False(ImageIODecoder.Shared.CanDecode(ImageIOTesting.Request(CatalogueSamples.Jpeg(), 1)!.Value));
    }

    [Fact]
    public void An_Exif_orientation_is_not_applied()
    {
        byte[] jpeg = WithSegment(CatalogueSamples.Jpeg(96, 40), Exif(orientation: 6));
        PlatformImageRequest request = ImageIOTesting.Request(jpeg, 0)!.Value;

        using DecodedImage? platform = ImageIODecoder.Decode(jpeg, request, 0);
        using DecodedImage? managed = ImageIOTesting.Managed(jpeg, request);

        Assert.Equal((96, 40), (platform!.Width, platform.Height));
        AssertWithinTolerance(ImageIOTesting.Compare(platform, managed!), "Exif orientation 6");
    }

    [Fact]
    public void An_embedded_ICC_profile_is_not_converted()
    {
        // ROMM RGB is far from sRGB: a conversion would move the samples by tens of levels.
        byte[] profile = File.ReadAllBytes("/System/Library/ColorSync/Profiles/ROMM RGB.icc");
        byte[] jpeg = WithSegment(CatalogueSamples.Jpeg(96, 40), [.. "ICC_PROFILE\0"u8, 1, 1, .. profile], marker: 0xE2);
        PlatformImageRequest request = ImageIOTesting.Request(jpeg, 0)!.Value;

        using DecodedImage? platform = ImageIODecoder.Decode(jpeg, request, 0);
        using DecodedImage? managed = ImageIOTesting.Managed(jpeg, request);

        AssertWithinTolerance(ImageIOTesting.Compare(platform!, managed!), "ROMM-tagged");
    }

    [Theory]
    [InlineData(75, 43, 1, 37, 21)]
    [InlineData(75, 43, 2, 18, 10)]
    [InlineData(301, 97, 3, 37, 12)]
    public void A_floor_sized_subsampled_decode_reports_its_reduction_and_fills_the_ceiling_grid(int width, int height, int reduction, int floorWidth, int floorHeight)
    {
        // ImageIO's kCGImageSourceSubsampleFactor delivers floor(W / s) samples; the core's grid is ceil(W / s), so the decoder
        // reports the reduction from the size and repeats the last column and row.
        byte[] jpeg = CatalogueSamples.Jpeg(width, height);
        PlatformImageRequest request = ImageIOTesting.Request(jpeg, reduction)!.Value;
        int ceilWidth = (width + (1 << reduction) - 1) >> reduction;
        int ceilHeight = (height + (1 << reduction) - 1) >> reduction;

        using DecodedImage? platform = ImageIODecoder.Decode(jpeg, request, reduction);

        Assert.Equal(reduction, ImageIODecoder.DeliveredReduction(request, floorWidth, floorHeight, reduction));
        Assert.Equal(reduction, platform!.Reduction);
        Assert.Equal((ceilWidth, ceilHeight), (platform.Width, platform.Height));
        if (ceilWidth != floorWidth)
        {
            ReadOnlySpan<byte> row = platform.GetRow(0);
            Assert.Equal(row.Slice((floorWidth - 1) * 3, 3).ToArray(), row.Slice(floorWidth * 3, 3).ToArray());
        }

        if (ceilHeight != floorHeight)
        {
            Assert.Equal(platform.GetRow(floorHeight - 1).ToArray(), platform.GetRow(floorHeight).ToArray());
        }
    }

    [Fact]
    public void A_size_that_is_no_reduction_is_rejected()
    {
        PlatformImageRequest request = ImageIOTesting.Request(CatalogueSamples.Jpeg(75, 43), 2)!.Value;

        Assert.Equal(0, ImageIODecoder.DeliveredReduction(request, 75, 43, 2));
        Assert.Equal(1, ImageIODecoder.DeliveredReduction(request, 38, 22, 2));
        Assert.Equal(-1, ImageIODecoder.DeliveredReduction(request, 40, 40, 2));
        Assert.Equal(-1, ImageIODecoder.DeliveredReduction(request, 9, 5, 2)); // an eighth: coarser than asked
    }

    [Fact]
    public void Data_that_is_not_a_JPEG_returns_nothing()
    {
        PlatformImageRequest request = ImageIOTesting.Request(CatalogueSamples.Jpeg(75, 43), 0)!.Value;
        byte[] png = [0x89, .. "PNG\r\n\u001a\n"u8, 0, 0, 0, 13, .. "IHDR"u8];

        Assert.Null(ImageIODecoder.Decode(png, request, 0));
        Assert.Null(ImageIODecoder.Decode(CatalogueSamples.Jpeg(75, 43).AsMemory(0, 40), request, 0));
        Assert.True(double.IsNaN(ImageIODecoder.MeanQuantizerStep(png)));
    }

    [Fact]
    public void Truncated_and_corrupted_data_returns_samples_or_nothing()
    {
        // The fuzz project is net10.0 and cannot load this Apple-only package: a fixed sweep of damaged inputs stands in for it.
        byte[] jpeg = CatalogueSamples.Jpeg(64, 48);
        PlatformImageRequest request = ImageIOTesting.Request(jpeg, 0)!.Value;
        uint state = 375;
        int Next(int limit)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)limit);
        }

        for (int i = 0; i < 200; i++)
        {
            byte[] damaged = i < 100 ? jpeg[..Next(jpeg.Length)] : [.. jpeg];
            if (i >= 100)
            {
                for (int j = 0; j < 8; j++)
                {
                    damaged[Next(damaged.Length)] = (byte)Next(256);
                }
            }

            _ = ImageIODecoder.MeanQuantizerStep(damaged);
            using DecodedImage? image = ImageIODecoder.Decode(damaged, request with { Reduction = i % 4 }, i % 4);
            Assert.True(image is null || (image.Width, image.Height) == (64 >> image.Reduction, 48 >> image.Reduction));
        }
    }

    [Fact]
    public async Task A_renderer_prefetch_draws_the_page_within_the_page_tolerance_of_a_managed_decode()
    {
        using PdfDocument platform = Open(CatalogueSamples.Cover());
        using PdfDocument managed = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));

        byte[] withImageIO = await RenderAsync(platform, prefetch: true);
        byte[] withManaged = await RenderAsync(managed, prefetch: true);

        Assert.Equal("PlatformImageVerified", Assert.Single(platform.Diagnostics).Code);
        Assert.Empty(managed.Diagnostics);
        (double psnr, _, int max) = ScaledImagePages.Compare(withManaged, withImageIO);
        Assert.True(psnr >= 50 && max <= 24, string.Create(CultureInfo.InvariantCulture, $"PSNR {psnr:0.0} dB, max {max}"));
    }

    [Fact]
    public void Without_a_platform_decoder_a_renderer_prefetch_completes_at_once()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover());
        using var renderer = new CoreGraphicsRenderer { ScaledImageDecode = true };

        ValueTask prefetch = renderer.PrefetchImagesAsync(document.Pages[0].CreateDisplayList(), Device, 612, 792, TestContext.Current.CancellationToken);

        Assert.True(prefetch.IsCompletedSuccessfully);
    }

    [Fact]
    public void The_decoder_declares_what_it_is()
    {
        ImageIODecoder decoder = ImageIODecoder.Shared;

        Assert.Equal("imageio", decoder.Name);
        Assert.Equal(PlatformImageFidelity.Tolerance, decoder.Fidelity);
        Assert.Equal(PlatformImageFeatures.ArithmeticCoding, decoder.Features);
    }

    private static PdfOptions Options() =>
        new PdfOptions().UsePlatformImageDecoder(ImageIODecoder.Shared).WithPlatformImageVerification(true).WithDisplayListCacheBudget(0);

    private static PdfDocument Open(byte[] file) => PdfDocument.Open(file, Options());

    private static byte[] Page(byte[] jpeg, string colorSpace, int width, int height) => ContentSamples.OnePage(
        Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"q {width} 0 0 {height} 0 0 cm /Im0 Do Q")),
        "/XObject << /Im0 5 0 R >>",
        ContentSamples.Stream(
            Encoding.Latin1.GetString(jpeg),
            string.Create(CultureInfo.InvariantCulture, $"/Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace {colorSpace} /BitsPerComponent 8 /Filter /DCTDecode")));

    private static byte[] Samples(DisplayList list)
    {
        var recorder = new ImageRecorder();
        list.Replay(recorder);
        return recorder.Image!.GetSamplesAt(0, TestContext.Current.CancellationToken)!.Value.Samples.ToArray();
    }

    private static void AssertWithinTolerance(Difference difference, string what) =>
        Assert.True(difference.Max <= 24 && difference.Psnr >= 50 && difference.OverTwoLevels <= 0.01, $"{what}: {difference}");

    /// <summary>A JPEG with a segment inserted after SOI.</summary>
    private static byte[] WithSegment(byte[] jpeg, byte[] payload, byte marker = 0xE1) =>
        [0xFF, 0xD8, 0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload, .. jpeg.AsSpan(2)];

    /// <summary>An APP1 Exif payload with one IFD0 entry: Orientation (CIPA DC-008 tag 0x0112).</summary>
    private static byte[] Exif(int orientation) =>
        [.. "Exif\0\0"u8, .. "II"u8, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>Renders a list onto white at 0.5 points per pixel after the renderer's prefetch, as a viewer draws a tile.</summary>
    private static async Task<byte[]> RenderAsync(PdfDocument document, bool prefetch)
    {
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(0.5);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        DisplayList list = page.CreateDisplayList();
        using var renderer = new CoreGraphicsRenderer();
        if (prefetch)
        {
            await renderer.PrefetchImagesAsync(list, device, size.Width, size.Height, TestContext.Current.CancellationToken);
        }

        using CGBitmapContext context = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] pixels, out _);
        using (var white = new CGColor(1, 1, 1, 1))
        {
            context.SetFillColor(white);
        }

        context.FillRect(new CGRect(0, 0, size.Width, size.Height));
        context.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, size.Height));
        renderer.Render(list, context, device);
        context.Flush();
        return pixels;
    }

    /// <summary>Records the one image a replay draws.</summary>
    /// <summary>Counts the decodes it passes on to <paramref name="inner"/>.</summary>
    private sealed class CountingDecoder(IPlatformImageDecoder inner) : IPlatformImageDecoder
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string Name => inner.Name;

        public PlatformImageFidelity Fidelity => inner.Fidelity;

        public PlatformImageFeatures Features => inner.Features;

        public bool CanDecode(in PlatformImageRequest request) => inner.CanDecode(request);

        public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return inner.DecodeAsync(encoded, request, cancellationToken);
        }
    }

    private sealed class ImageRecorder : IRenderingBackend
    {
        public DisplayImage? Image { get; private set; }

        public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) => Image = image;

        public void SaveState()
        {
        }

        public void RestoreState()
        {
        }

        public void SetTransform(in Matrix transform)
        {
        }

        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
        }

        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
        }

        public void IntersectClip(PathView path, FillRule rule)
        {
        }

        public void DrawGlyphs(in GlyphRun run)
        {
        }

        public void DrawShading(in DisplayShading shading)
        {
        }

        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
        {
        }

        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
        }

        public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern)
        {
        }

        public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern)
        {
        }

        public void FillImageMask(DisplayImageMask mask, in DisplayColor color)
        {
        }

        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
        {
        }

        public void SetSoftMask(in DisplaySoftMask mask)
        {
        }

        public void SetDeviceParameters(in DisplayDeviceParameters parameters)
        {
        }

        public void DrawGroup(in DisplayGroup group) => group.Replay(this);

        public void BeginAnnotation(in DisplayAnnotation annotation)
        {
        }

        public void EndAnnotation()
        {
        }
    }
}
