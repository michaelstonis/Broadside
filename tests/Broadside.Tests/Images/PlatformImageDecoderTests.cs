using System.Globalization;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Rendering;
using Broadside.Tests.Document;
using Broadside.Tests.Filters.Dct;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// Issue #372: the platform image decoder seam's contract, with a fake decoder. A prefetch fills the document's image cache before a
/// replay; the replay never calls the platform; a failure falls back to the managed codec with one diagnostic; an ineligible image is
/// never offered; cancellation keeps nothing; concurrent prefetches decode once; the verification mode measures the difference.
/// </summary>
public sealed class PlatformImageDecoderTests
{
    // The 75 x 43 test JPEG drawn at its size in points, under a device transform of 1: reduction 0 at any samples per pixel.
    private static readonly Matrix Device = Matrix.Identity;

    [Fact]
    public async Task A_prefetch_fills_the_cache_and_the_replay_draws_the_platform_samples_without_calling_it()
    {
        var decoder = new FakeDecoder();
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);
        Assert.Equal(1, decoder.Calls);

        DisplayImageSamples samples = Image(list).GetSamplesAt(0, TestContext.Current.CancellationToken)!.Value;
        Assert.All(samples.Samples.ToArray(), b => Assert.Equal(FakeDecoder.Fill, b));
        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);
        Assert.Equal(1, decoder.Calls);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_replay_without_a_prefetch_decodes_with_the_managed_codec_and_never_calls_the_platform()
    {
        var decoder = new FakeDecoder();
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();

        DisplayImageSamples samples = Image(list).GetSamplesAt(0, TestContext.Current.CancellationToken)!.Value;

        Assert.Equal(0, decoder.Calls);
        Assert.Contains(samples.Samples.ToArray(), b => b != FakeDecoder.Fill);
    }

    [Theory]
    [InlineData(FakeBehavior.ReturnsNull, "could not decode")]
    [InlineData(FakeBehavior.Throws, "the platform is out of decoders")]
    [InlineData(FakeBehavior.WrongSize, "delivered 10 x 10")]
    public async Task A_platform_failure_falls_back_to_the_managed_codec_with_one_diagnostic(FakeBehavior behavior, string reason)
    {
        var decoder = new FakeDecoder { Behavior = behavior };
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);
        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        Assert.Equal(1, decoder.Calls);
        Diagnostics.Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("ImageDecodedManaged", Diagnostics.DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
        Assert.Contains(reason, diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'fake'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(Image(list).GetSamplesAt(0, TestContext.Current.CancellationToken)!.Value.Samples.ToArray(), b => b != FakeDecoder.Fill);
    }

    [Theory]
    [InlineData("cmyk", "/DeviceCMYK", "")]
    [InlineData("encoded-ycck-q90-420", "/DeviceCMYK", "")]
    [InlineData("precision12", "/DeviceRGB", "")]
    [InlineData("dnl", "/DeviceRGB", "")]
    [InlineData("arithmetic", "/DeviceRGB", "")]
    [InlineData("encoded-rgb-q90-420", "/DeviceRGB", "/DecodeParms << /ColorTransform 0 >>")]
    [InlineData("encoded-rgb-untransformed-q90", "/DeviceRGB", "")]
    public async Task An_ineligible_image_is_never_offered(string name, string colorSpace, string entries)
    {
        var decoder = new FakeDecoder();
        using PdfDocument document = Open(DctVectors.Jpeg(name), decoder, colorSpace, entries);
        DisplayList list = document.Pages[0].CreateDisplayList();
        Assert.True(Image(list).MaxReduction > 0, "the image is kept encoded, so only the rule keeps it from the platform");

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        Assert.Equal(0, decoder.Calls);
    }

    [Fact]
    public async Task A_frame_of_another_size_than_the_dictionary_is_never_offered()
    {
        var decoder = new FakeDecoder();
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), decoder, width: 80);
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        Assert.Equal(0, decoder.Calls);
    }

    [Fact]
    public async Task Arithmetic_coding_goes_only_to_a_decoder_that_declares_it()
    {
        var decoder = new FakeDecoder { Features = PlatformImageFeatures.ArithmeticCoding };
        using PdfDocument document = Open(DctVectors.Jpeg("arithmetic"), decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        Assert.Equal(1, decoder.Calls);
        Assert.True(decoder.LastRequest.ArithmeticCoding);
    }

    [Theory]
    [InlineData(PlatformImageFeatures.None, false, false)]
    [InlineData(PlatformImageFeatures.AppliesExifOrientation, true, false)]
    [InlineData(PlatformImageFeatures.ConvertsEmbeddedProfiles, false, true)]
    [InlineData(PlatformImageFeatures.AppliesExifOrientation | PlatformImageFeatures.ConvertsEmbeddedProfiles, true, true)]
    public async Task Exif_and_ICC_payloads_are_zero_filled_only_for_a_decoder_that_would_act_on_them(PlatformImageFeatures features, bool exifCleared, bool iccCleared)
    {
        byte[] jpeg = PlatformImageEligibilityTests.WithSegments(DctVectors.Jpeg("encoded-rgb-q90-420"));
        var decoder = new FakeDecoder { Features = features };
        using PdfDocument document = Open(jpeg, decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        byte[] seen = decoder.LastData!;
        Assert.Equal(jpeg.Length, seen.Length);
        Assert.Equal(exifCleared, PlatformImageEligibilityTests.ExifPayload(seen).All(b => b == 0));
        Assert.Equal(iccCleared, PlatformImageEligibilityTests.IccPayload(seen).All(b => b == 0));
        Assert.True(decoder.LastRequest.HasExif && decoder.LastRequest.HasIccProfile);
    }

    [Fact]
    public async Task The_request_carries_the_reduction_the_backend_will_ask_for_and_a_finer_delivery_serves_it()
    {
        // Drawn at a quarter of its size with 1 sample per pixel: reduction 2 is wanted; the fake delivers reduction 1.
        var decoder = new FakeDecoder { Deliver = 1 };
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();
        var quarter = new Matrix(0.25, 0, 0, 0.25, 0, 0);

        await list.PrefetchImagesAsync(quarter, null, 1, TestContext.Current.CancellationToken);

        Assert.Equal(2, decoder.LastRequest.Reduction);
        DisplayImage image = Image(list);
        Assert.Equal(2, image.GetReduction(new Matrix(75, 0, 0, 43, 0, 0) * quarter, 1, document.GetColorConverter(image.ColorSpace)));
        DisplayImageSamples samples = image.GetSamplesAt(2, TestContext.Current.CancellationToken)!.Value;
        Assert.Equal((1, 38, 22), (samples.Reduction, samples.Width, samples.Height));
        Assert.Equal(FakeDecoder.Fill, samples.Samples[0]);
        Assert.Equal(1, decoder.Calls);
    }

    [Fact]
    public async Task An_image_outside_the_region_is_not_offered()
    {
        var decoder = new FakeDecoder();
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, new PdfRectangle(300, 300, 400, 400), 0, TestContext.Current.CancellationToken);
        Assert.Equal(0, decoder.Calls);

        await list.PrefetchImagesAsync(Device, new PdfRectangle(0, 0, 10, 10), 0, TestContext.Current.CancellationToken);
        Assert.Equal(1, decoder.Calls);
    }

    [Fact]
    public async Task Cancelling_a_prefetch_keeps_nothing_and_the_next_one_decodes_again()
    {
        var decoder = new FakeDecoder { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();
        using var cancellation = new CancellationTokenSource();

        ValueTask prefetch = list.PrefetchImagesAsync(Device, null, 0, cancellation.Token);
        Assert.False(prefetch.IsCompleted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await prefetch);
        decoder.Gate.SetResult();
        await decoder.Finished.Task.WaitAsync(TestContext.Current.CancellationToken);

        decoder.Gate = null;
        Assert.Contains(Image(list).GetSamplesAt(0, TestContext.Current.CancellationToken)!.Value.Samples.ToArray(), b => b != FakeDecoder.Fill);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public async Task Concurrent_prefetches_of_one_image_decode_it_once()
    {
        var decoder = new FakeDecoder { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();

        Task first = list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken).AsTask();
        Task second = list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken).AsTask();
        decoder.Gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, decoder.Calls);
        Assert.Equal(FakeDecoder.Fill, Image(list).GetSamplesAt(0, TestContext.Current.CancellationToken)!.Value.Samples[0]);
    }

    [Fact]
    public async Task The_verification_mode_records_the_mismatch_of_shifted_samples_with_its_PSNR()
    {
        var decoder = new FakeDecoder { Behavior = FakeBehavior.Managed, Shift = 40 };
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-444"), decoder, options: new PdfOptions().WithPlatformImageVerification(true));
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        Diagnostics.Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("PlatformImageMismatch", Diagnostics.DiagnosticSeverity.Warning), (diagnostic.Code, diagnostic.Severity));
        Assert.Contains("max 40", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("dB", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlatformImageFidelity.Exact, 0, 1, "PlatformImageVerified")]
    [InlineData(PlatformImageFidelity.Exact, 1, 4, "PlatformImageMismatch")]
    [InlineData(PlatformImageFidelity.Tolerance, 1, 4, "PlatformImageVerified")] // max 1, about 54 dB
    [InlineData(PlatformImageFidelity.Tolerance, 1, 1, "PlatformImageMismatch")] // every sample 1 level off: 48.1 dB, under 50
    public async Task The_verification_mode_holds_each_decoder_to_its_declared_fidelity(PlatformImageFidelity fidelity, int shift, int every, string code)
    {
        var decoder = new FakeDecoder { Behavior = FakeBehavior.Managed, Shift = shift, ShiftEvery = every, Fidelity = fidelity };
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-444"), decoder, options: new PdfOptions().WithPlatformImageVerification(true));
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

        Assert.Equal(code, Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public async Task Platform_decoding_turns_off_globally_or_per_decoder()
    {
        foreach (PdfOptions options in new[]
        {
            new PdfOptions().WithPlatformImageDecoding(false),
            new PdfOptions().UseManagedImageDecoding(),
            new PdfOptions().WithPlatformImageDecoder("fake", false),
        })
        {
            var decoder = new FakeDecoder();
            using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), decoder, options: options);
            DisplayList list = document.Pages[0].CreateDisplayList();

            ValueTask prefetch = list.PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);

            Assert.True(prefetch.IsCompletedSuccessfully);
            await prefetch;
            Assert.Equal(0, decoder.Calls);
        }

        var again = new FakeDecoder();
        using PdfDocument enabled = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), again, options: new PdfOptions().WithPlatformImageDecoder("fake", false).WithPlatformImageDecoder("fake", true));
        await enabled.Pages[0].CreateDisplayList().PrefetchImagesAsync(Device, null, 0, TestContext.Current.CancellationToken);
        Assert.Equal(1, again.Calls);
    }

    [Fact]
    public void Text_extraction_and_image_decode_never_use_the_platform()
    {
        var decoder = new FakeDecoder();
        using PdfDocument document = Open(DctVectors.Jpeg("encoded-rgb-q90-420"), decoder);

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        Assert.Equal(0, decoder.Calls);
        Assert.Contains(decoded.Samples.ToArray(), b => b != FakeDecoder.Fill);
    }

    [Fact]
    public void Without_a_decoder_a_prefetch_completes_at_once()
    {
        using PdfDocument document = PdfDocument.Open(Page(DctVectors.Jpeg("encoded-rgb-q90-420"), "/DeviceRGB", "", 75));
        ValueTask prefetch = document.Pages[0].CreateDisplayList().PrefetchImagesAsync(Device, null, 2, TestContext.Current.CancellationToken);

        Assert.True(prefetch.IsCompletedSuccessfully);
    }

    private static DisplayImage Image(DisplayList list)
    {
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        return Assert.Single(recorder.Images).Image;
    }

    private static PdfDocument Open(byte[] jpeg, FakeDecoder decoder, string colorSpace = "/DeviceRGB", string entries = "", int width = 75, PdfOptions? options = null) =>
        PdfDocument.Open(Page(jpeg, colorSpace, entries, width), (options ?? new PdfOptions()).UsePlatformImageDecoder(decoder));

    private static byte[] Page(byte[] jpeg, string colorSpace, string entries, int width) => new TestPdf().Build(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>",
        ImageTesting.Stream(string.Empty, "q 75 0 0 43 0 0 cm /Im0 Do Q"),
        ImageTesting.Stream(string.Create(CultureInfo.InvariantCulture, $"/Type /XObject /Subtype /Image /Width {width} /Height 43 /ColorSpace {colorSpace} /BitsPerComponent 8 /Filter /DCTDecode {entries}"), ImageTesting.Bytes(jpeg)));

    public enum FakeBehavior
    {
        Fills,
        ReturnsNull,
        Throws,
        WrongSize,
        Managed,
    }

    /// <summary>A platform decoder whose output the test decides; it counts calls and keeps what it was handed.</summary>
    private sealed class FakeDecoder : IPlatformImageDecoder
    {
        public const byte Fill = 77;

        private int _calls;

        public string Name => "fake";

        public PlatformImageFidelity Fidelity { get; init; } = PlatformImageFidelity.Tolerance;

        public PlatformImageFeatures Features { get; init; }

        public FakeBehavior Behavior { get; init; }

        public int Shift { get; init; }

        public int ShiftEvery { get; init; } = 1;

        public int? Deliver { get; init; }

        public TaskCompletionSource? Gate { get; set; }

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public PlatformImageRequest LastRequest { get; private set; }

        public byte[]? LastData { get; private set; }

        public bool CanDecode(in PlatformImageRequest request) => true;

        public async ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastRequest = request;
            LastData = encoded.ToArray();
            try
            {
                if (Gate is { } gate)
                {
                    await gate.Task.ConfigureAwait(false);
                }

                int reduction = Deliver ?? request.Reduction;
                switch (Behavior)
                {
                    case FakeBehavior.ReturnsNull:
                        return null;
                    case FakeBehavior.Throws:
                        throw new InvalidOperationException("the platform is out of decoders");
                    case FakeBehavior.WrongSize:
                        using (var wrong = new PdfRequestFree(10).Builder())
                        {
                            return wrong.Build();
                        }

                    case FakeBehavior.Managed:
                        using (DecodedImageBuilder builder = request.CreateImage(reduction))
                        {
                            byte[] managed = ManagedSamples(LastData, reduction);
                            for (int i = 0; i < managed.Length; i++)
                            {
                                builder.Samples[i] = (byte)Math.Min(255, managed[i] + (i % ShiftEvery == 0 ? Shift : 0));
                            }

                            return builder.Build();
                        }

                    default:
                        using (DecodedImageBuilder builder = request.CreateImage(reduction))
                        {
                            builder.Samples.Fill(Fill);
                            return builder.Build();
                        }
                }
            }
            finally
            {
                Finished.TrySetResult();
            }
        }

        private static byte[] ManagedSamples(byte[] jpeg, int reduction)
        {
            using PdfDocument document = PdfDocument.Open(Page(jpeg, "/DeviceRGB", string.Empty, 75));
            return Image(document.Pages[0].CreateDisplayList()).GetSamplesAt(reduction, TestContext.Current.CancellationToken)!.Value.Samples.ToArray();
        }
    }

    /// <summary>Makes samples of a size no request asks for, through the public request type.</summary>
    private readonly struct PdfRequestFree(int size)
    {
        public DecodedImageBuilder Builder() => new PlatformImageRequest { Width = size, Height = size, Components = 3 }.CreateImage(0);
    }
}
