using Broadside.Filters;
using Broadside.Filters.Jpx;
using Broadside.Images;
using Broadside.Tests.Document;
using Broadside.TestSupport;

#pragma warning disable CA5394 // pseudo-random test input, not security

namespace Broadside.Tests.Filters;

/// <summary>
/// JPEG 2000 decoding allocates for the image's structure (tiles, precincts, code-block records) and nothing in the tier-1 and
/// tier-2 loops: more layers, passes and bytes for the same geometry cost no more (CLAUDE.md, "Code conventions"; ISO 32000-2
/// §7.4.9). <c>JpxBenchmarks</c> in bench/Broadside.Benchmarks is the measuring half.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class JpxAllocationTests
{
    [Fact]
    public void Decoding_allocates_for_the_image_structure_and_not_per_layer_pass_or_byte()
    {
        byte[] oneLayer = JpxSamples.Vector("Rlcp1Layer").Data;
        byte[] threeLayers = JpxSamples.Vector("Rlcp3Layers").Data;
        long forOneLayer = Allocations.Measure(() => Decode(oneLayer));
        long forThreeLayers = Allocations.Measure(() => Decode(threeLayers));

        Assert.Equal(forOneLayer, forThreeLayers);
        Assert.InRange(forOneLayer, 1, 32 * 1024);
    }

    [Theory]
    [InlineData(64, 64, 0)]
    [InlineData(1024, 4, JpxComponentStyle.Bypass | JpxComponentStyle.VerticallyCausal)]
    [InlineData(7, 61, JpxComponentStyle.ResetContexts | JpxComponentStyle.SegmentationSymbols)]
    public void Tier_1_allocates_nothing_per_code_block(int width, int height, int style)
    {
        // Every pass of 3 x 9 + 1 over pseudo-random bytes: decisions close to even, most samples significant early (ITU-T T.800 Annex D).
        byte[] data = new byte[16 * 1024];
        new Random(width).NextBytes(data);
        JpxSegment[] segments = [new(0, 12 * 1024, 28)];
        int[] output = new int[width * height];
        using var decoder = new JpxCodeBlockDecoder();
        var coding = new JpxBlockCoding(width, height, 3, style, 9, 0, 0);

        long allocated = Allocations.Measure(() =>
        {
            for (int block = 0; block < 4; block++)
            {
                decoder.Decode(data.AsSpan(block * 1024), segments, coding, output, width);
            }
        });

        Assert.Equal(0, allocated);
    }

    private static void Decode(byte[] data)
    {
        var filter = new JpxDecodeFilter();
        var context = new ImageFilterContext(new FilterContext());
        using DecodedImage? image = filter.DecodeImage(data, context);
        Assert.NotNull(image);
    }
}
