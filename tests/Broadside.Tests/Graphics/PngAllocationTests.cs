using Broadside.Images.Png;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Graphics;

/// <summary>
/// The PNG reader allocates per image, not per row (CLAUDE.md, "Code conventions"; issue #153): its row buffers and planes are
/// pooled, so reading a tall image allocates no more than reading a short one, except the pass-through copy of the IDAT data for
/// a non-interlaced image without alpha. <c>PngBenchmarks</c> is the measuring half.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class PngAllocationTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Reading_allocates_the_same_whatever_the_height(bool alpha, bool interlaced)
    {
        byte[] small = PngSamples.Rgb(64, 16, alpha, interlaced);
        byte[] large = PngSamples.Rgb(64, 512, alpha, interlaced);

        long forSmall = Allocations.Measure(() => Read(small));
        long forLarge = Allocations.Measure(() => Read(large));

        Assert.InRange(forLarge, 0, forSmall);
    }

    [Fact]
    public void Reading_an_image_that_passes_through_allocates_only_the_idat_copy_more_per_height()
    {
        byte[] small = PngSamples.Rgb(64, 16, alpha: false, interlaced: false);
        byte[] large = PngSamples.Rgb(64, 512, alpha: false, interlaced: false);

        long forSmall = Allocations.Measure(() => Read(small));
        long forLarge = Allocations.Measure(() => Read(large));

        // The concatenated IDAT data is at most the file; an array header and alignment are the slack.
        Assert.InRange(forLarge - forSmall, 0, large.Length - small.Length + 64);
    }

    private static void Read(byte[] png)
    {
        using Broadside.Images.Png.PngImage image = PngReader.Read(png, long.MaxValue, keepSamples: false, out _) ?? throw new InvalidOperationException("The PNG did not read.");
    }
}
