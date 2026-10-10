using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// JPEG 2000 decoding allocates for the image's structure (tiles, precincts, code-block records) and nothing in the tier-1 and
/// tier-2 loops: more layers, passes and bytes for the same geometry cost no more (CLAUDE.md, "Code conventions"; ISO 32000-2
/// §7.4.9). <c>JpxBenchmarks</c> in bench/Broadside.Benchmarks is the measuring half.
/// </summary>
public class JpxAllocationTests
{
    [Fact]
    public void Decoding_allocates_for_the_image_structure_and_not_per_layer_pass_or_byte()
    {
        byte[] oneLayer = JpxSamples.Vector("Rlcp1Layer").Data;
        byte[] threeLayers = JpxSamples.Vector("Rlcp3Layers").Data;
        for (int warmUp = 0; warmUp < 40; warmUp++)
        {
            Decode(oneLayer);
            Decode(threeLayers);
        }

        long forOneLayer = Decode(oneLayer);
        long forThreeLayers = Decode(threeLayers);

        Assert.Equal(forOneLayer, forThreeLayers);
        Assert.InRange(forOneLayer, 1, 32 * 1024);
    }

    private static long Decode(byte[] data)
    {
        var filter = new JpxDecodeFilter();
        var context = new ImageFilterContext(new FilterContext());
        long before = GC.GetAllocatedBytesForCurrentThread();
        using (DecodedImage? image = filter.DecodeImage(data, context))
        {
            Assert.NotNull(image);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
