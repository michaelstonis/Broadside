using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Issue #482: ImageIO and JPEG 2000. ImageIO decodes JPX as the managed decoder does, to within 2 levels (exactly when the
/// codestream is reversible), full size and at <c>kCGImageSourceSubsampleFactor</c> 2, 4 and 8, which is the codestream's
/// resolution levels dropped. It is also 2.5 to 12 times slower than the managed decoder on every image measured
/// (<c>docs/benchmarks/imageio-image-decoder.md</c>), so the decoder does not take JPX: these tests hold the fidelity that was
/// measured, so that taking it later is a one-line change, and hold that nothing offers it a JPX image now.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ImageIOJpxTests
{
    public static TheoryData<string> Vectors { get; } = [.. JpxSamples.Vectors.Where(vector => vector.Components is 1 or 3 && vector.Depth == 8).Select(vector => vector.Name)];

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Every_OpenJPEG_vector_decodes_within_the_tolerance_at_full_size_and_at_each_resolution_level(string name)
    {
        JpxVector vector = JpxSamples.Vector(name);
        byte[] data = vector.Data;
        PlatformImageRequest request = Request(vector);
        ImageHeader header = Header(data, request);

        for (int reduction = 0; reduction <= Math.Min(3, header.MaxReduction); reduction++)
        {
            using DecodedImage? platform = ImageIODecoder.Decode(data, request with { Reduction = reduction }, reduction);
            using DecodedImage? managed = Managed(data, request, reduction);

            Assert.NotNull(platform);
            Assert.NotNull(managed);
            Assert.Equal((managed.Reduction, managed.Geometry), (platform.Reduction, platform.Geometry));
            Difference difference = ImageIOTesting.Compare(platform, managed);
            if (vector.IsLossless)
            {
                Assert.Equal(0, difference.Max);
            }
            else
            {
                Assert.True(difference.Max <= 24 && difference.Psnr >= 55 && difference.OverTwoLevels <= 0.01, $"{name} at reduction {reduction}: {difference}");
            }
        }
    }

    [Fact]
    public void A_reduction_delivers_the_ceiling_of_the_size_over_two_to_the_levels_dropped()
    {
        JpxVector vector = JpxSamples.Vector("IrreversibleTiled");
        PlatformImageRequest request = Request(vector);

        foreach (int reduction in new[] { 1, 2, 3 })
        {
            using DecodedImage? platform = ImageIODecoder.Decode(vector.Data, request with { Reduction = reduction }, reduction);

            Assert.NotNull(platform);
            Assert.Equal(reduction, platform.Reduction);
            Assert.Equal((Ceiling(vector.Width, reduction), Ceiling(vector.Height, reduction)), (platform.Geometry.Width, platform.Geometry.Height));
        }
    }

    [Fact]
    public async Task DecodeAsync_decodes_a_JPX_request_at_its_reduction()
    {
        JpxVector vector = JpxSamples.Vector("Medium");
        PlatformImageRequest request = Request(vector) with { Reduction = 2 };

        using DecodedImage? decoded = await ImageIODecoder.Shared.DecodeAsync(vector.Data, request, TestContext.Current.CancellationToken);

        Assert.NotNull(decoded);
        Assert.Equal((2, 16, 12, 3), (decoded.Reduction, decoded.Geometry.Width, decoded.Geometry.Height, decoded.Geometry.Components));
    }

    [Fact]
    public void ImageIO_may_parse_only_JPEG_2000_for_a_JPX_decode()
    {
        // The allowable types of a JPX decode are public.jpeg-2000 alone: JPEG data under a JPX request is no image.
        byte[] jpeg = ImageIOTesting.Vector("encoded-rgb-q90-444");
        PlatformImageRequest request = ImageIOTesting.Request(jpeg, 0)!.Value with { Filter = new CosName("JPXDecode") };

        using DecodedImage? decoded = ImageIODecoder.Decode(jpeg, request, 0);

        Assert.Null(decoded);
    }

    [Fact]
    public void The_decoder_does_not_take_a_JPX_image_because_the_managed_decoder_is_faster()
    {
        PlatformImageRequest request = Request(JpxSamples.Vector("Medium"));

        Assert.False(ImageIODecoder.Shared.CanDecode(request));
        Assert.False(ImageIODecoder.Shared.CanDecode(request with { Reduction = 2 }));
    }

    [Theory]
    [InlineData("jpx-lossless.pdf")]
    [InlineData("jpx-subsampled.pdf")]
    [InlineData("jpx-fragmented.pdf")]
    [InlineData("jpx-smask-in-data.pdf")]
    public async Task A_document_with_a_JPX_image_never_offers_it_to_ImageIO_and_records_nothing(string file)
    {
        var decoder = new CountingDecoder(ImageIODecoder.Shared);
        using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Path(file), new PdfOptions().UsePlatformImageDecoder(decoder).WithPlatformImageVerification(true).WithDisplayListCacheBudget(0), TestContext.Current.CancellationToken);
        DisplayList list = document.Pages[0].CreateDisplayList();

        await list.PrefetchImagesAsync(Matrix.Identity, null, 1, TestContext.Current.CancellationToken);
        await list.PrefetchImagesAsync(new Matrix(0.1, 0, 0, 0.1, 0, 0), null, 1, TestContext.Current.CancellationToken);

        Assert.Equal(0, decoder.Calls);
        Assert.Empty(document.Diagnostics);
    }

    // ITU-T T.800 B-15: a resolution level's size is the reference grid's, divided and rounded up.
    private static int Ceiling(int size, int reduction) => (size + (1 << reduction) - 1) >> reduction;

    // The request the core makes for an offered image: the codestream's size and channels, 8 bits, reduction 0.
    private static PlatformImageRequest Request(JpxVector vector) => new()
    {
        Filter = new CosName("JPXDecode"),
        Width = vector.Width,
        Height = vector.Height,
        Components = vector.Components,
        BitsPerComponent = 8,
    };

    private static ImageHeader Header(byte[] data, in PlatformImageRequest request)
    {
        Assert.True(new JpxDecodeFilter().TryReadHeader(data, Context(request, 0), out ImageHeader header));
        return header;
    }

    private static DecodedImage? Managed(byte[] data, in PlatformImageRequest request, int reduction) =>
        new JpxDecodeFilter().DecodeImage(data, Context(request, reduction));

    private static ImageFilterContext Context(in PlatformImageRequest request, int reduction) =>
        new(new FilterContext()) { Geometry = new ImageGeometry(request.Width, request.Height, request.Components, 8), Reduction = reduction };

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
}
