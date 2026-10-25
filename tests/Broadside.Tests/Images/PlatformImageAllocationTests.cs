using Broadside.Graphics;
using Broadside.Images;
using Broadside.Rendering;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// Issue #372: the prefetch a viewer awaits before every tile allocates nothing on a text-only page and nothing for an image the
/// platform already decoded (the build-time half of <c>ImagePrefetchBenchmarks</c>).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class PlatformImageAllocationTests
{
    [Theory]
    [InlineData("text-standard14.pdf")]
    [InlineData("dct-baseline.pdf")]
    public async Task A_prefetch_with_nothing_left_to_decode_allocates_nothing(string file)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(file), new PdfOptions().UsePlatformImageDecoder(new GrayDecoder()));
        DisplayList list = document.Pages[0].CreateDisplayList();
        await list.PrefetchImagesAsync(Matrix.Identity, null, 2, TestContext.Current.CancellationToken);

        long allocated = Allocations.Measure(() => Assert.True(Done(list.PrefetchImagesAsync(Matrix.Identity, null, 2, CancellationToken.None))));

        Assert.Equal(0, allocated);
    }

    private static bool Done(ValueTask prefetch) => prefetch.IsCompletedSuccessfully;

    private sealed class GrayDecoder : IPlatformImageDecoder
    {
        public string Name => "gray";

        public PlatformImageFidelity Fidelity => PlatformImageFidelity.Tolerance;

        public PlatformImageFeatures Features => PlatformImageFeatures.None;

        public bool CanDecode(in PlatformImageRequest request) => true;

        public ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
        {
            using DecodedImageBuilder builder = request.CreateImage(request.Reduction);
            builder.Samples.Fill(128);
            return ValueTask.FromResult<DecodedImage?>(builder.Build());
        }
    }
}
