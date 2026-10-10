using Broadside.Filters;
using Broadside.Images;
using Broadside.Tests.Document;
using Broadside.TestSupport;

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

    private static void Decode(byte[] data)
    {
        var filter = new JpxDecodeFilter();
        var context = new ImageFilterContext(new FilterContext());
        using DecodedImage? image = filter.DecodeImage(data, context);
        Assert.NotNull(image);
    }
}
