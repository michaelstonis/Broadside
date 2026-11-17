using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// Issue #482: the platform image decoder seam's rule for JPXDecode (ISO 32000-2 §7.4.9; ITU-T T.800 Annexes A, B.5 and I): which
/// codestreams are offered, what the request says (the codestream's size, its channels, the resolution levels dropped as the
/// reduction), and the images that stay with the managed decoder.
/// </summary>
public sealed class PlatformImageJpxEligibilityTests
{
    private const string Entries = "/Width 64 /Height 48 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /JPXDecode";

    [Fact]
    public void An_8_bit_RGB_codestream_is_offered_with_its_own_size_and_channels()
    {
        byte[] data = Vector("Medium");

        Assert.True(PlatformImageEligibility.TryCreateJpxRequest(data, Header(data), indexed: false, out PlatformImageRequest request, out string? reason), reason);

        Assert.Equal("JPXDecode", request.Filter.Value);
        Assert.Equal((64, 48, 3, 8, 0), (request.Width, request.Height, request.Components, request.BitsPerComponent, request.Reduction));
        Assert.False(request.ColorTransform);
        Assert.False(request.Progressive);
        Assert.False(request.ArithmeticCoding);
        Assert.False(request.ChromaSubsampled);
        Assert.False(request.HasExif);
        Assert.False(request.HasIccProfile);
    }

    [Fact]
    public void A_gray_codestream_and_a_JP2_file_with_sRGB_or_gray_colors_are_offered()
    {
        byte[] gray = Vector("Rpcl");
        Assert.True(PlatformImageEligibility.TryCreateJpxRequest(gray, Header(gray), indexed: false, out PlatformImageRequest grayRequest, out string? reason), reason);
        Assert.Equal(1, grayRequest.Components);

        byte[] rgbFile = JpxEditing.Jp2(Vector("Medium"), JpxEditing.Color(16));
        Assert.True(PlatformImageEligibility.TryCreateJpxRequest(rgbFile, Header(rgbFile), indexed: false, out PlatformImageRequest rgbRequest, out reason), reason);
        Assert.Equal(3, rgbRequest.Components);

        byte[] grayFile = JpxEditing.Jp2(gray, JpxEditing.Color(17));
        Assert.True(PlatformImageEligibility.TryCreateJpxRequest(grayFile, Header(grayFile), indexed: false, out _, out reason), reason);
    }

    // Each case: the data and a fragment of the reason the rule gives. Built once, so the theory data is only the names.
    private static readonly Dictionary<string, (byte[] Data, string Expected)> Cases = Refusals();

    public static TheoryData<string> RefusalNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void Each_reading_a_platform_could_differ_on_keeps_the_image_managed(string name)
    {
        (byte[] data, string expected) = Cases[name];

        Assert.False(PlatformImageEligibility.TryCreateJpxRequest(data, Header(data), indexed: false, out _, out string? reason));
        Assert.Contains(expected, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Indexed_color_space_or_an_opacity_channel_keeps_the_image_managed()
    {
        byte[] data = Vector("Medium");
        ImageHeader header = Header(data);

        Assert.False(PlatformImageEligibility.TryCreateJpxRequest(data, header, indexed: true, out _, out string? reason));
        Assert.Contains("Indexed", reason, StringComparison.Ordinal);
        Assert.False(PlatformImageEligibility.TryCreateJpxRequest(data, header with { HasAlpha = true }, indexed: false, out _, out reason));
        Assert.Contains("opacity", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF, 0x4F, 0xFF, 0x51, 0x00 })]
    [InlineData(new byte[] { 0, 0, 0, 12, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A })]
    public void Malformed_data_is_never_eligible_and_never_throws(byte[] data)
    {
        var header = new ImageHeader(new ImageGeometry(4, 4, 3, 8)) { MaxReduction = 1 };

        Assert.False(PlatformImageEligibility.TryCreateJpxRequest(data, header, indexed: false, out _, out string? reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public async Task A_prefetch_offers_the_codestream_at_the_resolution_levels_the_backend_will_draw()
    {
        var decoder = new RecordingDecoder();
        byte[] data = MediumFile();
        using PdfDocument document = Open(data, decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();

        // The image is drawn 10 x 10 points for 64 x 48 samples: a reduction of 2 still leaves a sample per device pixel.
        await list.PrefetchImagesAsync(Matrix.Identity, null, 1, TestContext.Current.CancellationToken);

        Assert.Equal(1, decoder.Calls);
        Assert.Equal(("JPXDecode", 64, 48, 3, 2), (decoder.Last.Filter.Value, decoder.Last.Width, decoder.Last.Height, decoder.Last.Components, decoder.Last.Reduction));
        Assert.Equal((16, 12), (decoder.Last.ReducedWidth, decoder.Last.ReducedHeight));
        Assert.Equal(data, decoder.Data);
        DisplayImageSamples samples = Image(list).GetSamplesAt(2, TestContext.Current.CancellationToken)!.Value;
        Assert.Equal((16, 12, 2), (samples.Width, samples.Height, samples.Reduction));
        Assert.All(samples.Samples.ToArray(), b => Assert.Equal(RecordingDecoder.Fill, b));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("jpx-lossless.pdf", true)]
    [InlineData("jpx-subsampled.pdf", false)]
    [InlineData("jpx-fragmented.pdf", false)]
    [InlineData("jpx-smask-in-data.pdf", false)]
    public void The_JPX_corpus_files_are_offered_only_where_the_rule_allows(string file, bool offered)
    {
        using PdfDocument document = PdfDocument.Open(Path.Combine(Corpus.Directory, file));

        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(offered, recorder.Images.Single().Image.TryGetPlatformData(PlatformImageFeatures.None, out PlatformImageRequest request, out _));
        if (offered)
        {
            Assert.Equal("JPXDecode", request.Filter.Value);
        }
    }

    [Fact]
    public async Task A_decoder_that_takes_only_JPEG_data_is_offered_no_JPX_image_to_decode()
    {
        var decoder = new RecordingDecoder { TakesJpx = false };
        using PdfDocument document = Open(MediumFile(), decoder);
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Matrix.Identity, null, 1, TestContext.Current.CancellationToken);

        Assert.Equal(0, decoder.Calls);
        Assert.Empty(document.Diagnostics);
    }

    private static byte[] Vector(string name) => JpxSamples.Vector(name).Data;

    // A JP2 file of the 64 x 48 RGB vector (a bare codestream is read with an Information diagnostic of its own).
    private static byte[] MediumFile() => JpxEditing.Jp2(Vector("Medium"), JpxEditing.Color(16));

    private static ImageHeader Header(byte[] data)
    {
        Assert.True(new JpxDecodeFilter().TryReadHeader(data, new ImageFilterContext(new FilterContext()), out ImageHeader header));
        return header;
    }

    private static Dictionary<string, (byte[] Data, string Expected)> Refusals()
    {
        byte[] medium = Vector("Medium");
        byte[] gray = Vector("Rpcl");
        return new()
        {
            ["16-bit samples"] = (Vector("Gray16"), "16 bits"),
            ["signed 12-bit samples"] = (Vector("Signed12"), "12 bits"),
            ["4-bit samples"] = (Vector("Gray4"), "4 bits"),
            ["four components"] = (Vector("Rgba"), "4 channels"),
            ["sYCC"] = (JpxEditing.Jp2(medium, JpxEditing.Color(18)), "enumerated space 18"),
            ["CIELab"] = (JpxEditing.Jp2(medium, JpxEditing.Color(14)), "enumerated space 14"),
            ["an ICC profile"] = (JpxEditing.Jp2(medium, JpxEditing.Color(JpxEditing.IccHeader("RGB "))), "ICC profile"),
            ["one unsupported specification of two"] = (JpxEditing.Jp2(medium, JpxEditing.Color(16, precedence: 1), JpxEditing.Color(18)), "enumerated space 18"),
            ["channel definitions"] = (JpxEditing.Jp2(medium, JpxEditing.Color(16), JpxEditing.Definitions((0, 0, 1), (1, 0, 2), (2, 0, 3))), "channel definition"),
            ["a palette"] = (
                JpxEditing.Jp2(gray, JpxEditing.Color(16), JpxEditing.Palette([.. Enumerable.Range(0, 256).Select(i => new[] { i, 255 - i, i / 2 })]), JpxEditing.Mapping((0, 1, 0), (0, 1, 1), (0, 1, 2))),
                "palette"),
            ["fragments"] = (JpxEditing.Fragmented(medium, [100], [0, 1]), "fragments"),
        };
    }

    private static PdfDocument Open(byte[] data, RecordingDecoder decoder) =>
        PdfDocument.Open(ImageTesting.OneImage(Entries, ImageTesting.Bytes(data)), new PdfOptions().UsePlatformImageDecoder(decoder));

    private static DisplayImage Image(DisplayList list)
    {
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        return Assert.Single(recorder.Images).Image;
    }

    /// <summary>A platform decoder that fills what it is asked for and keeps the request and the data it was handed.</summary>
    private sealed class RecordingDecoder : IPlatformImageDecoder
    {
        public const byte Fill = 77;

        private int _calls;

        public string Name => "recording";

        public PlatformImageFidelity Fidelity => PlatformImageFidelity.Tolerance;

        public PlatformImageFeatures Features => PlatformImageFeatures.None;

        public bool TakesJpx { get; init; } = true;

        public int Calls => Volatile.Read(ref _calls);

        public PlatformImageRequest Last { get; private set; }

        public byte[] Data { get; private set; } = [];

        public bool CanDecode(in PlatformImageRequest request) => TakesJpx || request.Filter.Value == "DCTDecode";

        public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Last = request;
            Data = encoded.ToArray();
            using DecodedImageBuilder image = request.CreateImage(request.Reduction);
            for (int y = 0; y < image.Geometry.Height; y++)
            {
                image.GetRow(y).Fill(Fill);
            }

            return ValueTask.FromResult<DecodedImage?>(image.Build());
        }
    }
}
