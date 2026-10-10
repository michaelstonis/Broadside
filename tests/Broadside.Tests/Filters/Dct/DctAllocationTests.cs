using System.Buffers;
using Broadside.Filters;
using Broadside.Images;
using static Broadside.Tests.Filters.Dct.DctVectors;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// The DCT decoder is a hot path and allocates nothing per block, MCU or row (CLAUDE.md, "Code conventions"): once warm, a whole
/// decode through the plain filter path allocates nothing at all (per-image buffers are pooled), and the image path allocates the
/// same few objects whatever the image size. <c>DctBenchmarks</c> is the measuring half.
/// </summary>
[Collection("Heavy")]
public class DctAllocationTests
{
    public static TheoryData<string> Vectors => new() { "testorig", "sampling-411", "gray-2x2", "multiscan", "restart-blocks" };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Decoding_through_the_filter_contract_allocates_nothing_once_warm(string vector)
    {
        byte[] jpeg = Jpeg(vector);
        var filter = new DctDecodeFilter();
        var context = new FilterContext();
        var output = new ArrayBufferWriter<byte>(256 * 1024);

        // Past tiered compilation's call-count threshold first, so the runtime's own bookkeeping is not counted.
        for (int warmUp = 0; warmUp < 40; warmUp++)
        {
            Decode(filter, jpeg, output, context);
        }

        long allocated = Decode(filter, jpeg, output, context);

        Assert.True(output.WrittenCount > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void The_image_path_allocates_per_image_not_per_row()
    {
        var filter = new DctDecodeFilter();
        byte[] small = Jpeg("sampling-420");
        byte[] large = Jpeg("testorig");
        for (int warmUp = 0; warmUp < 40; warmUp++)
        {
            DecodeImage(filter, small);
            DecodeImage(filter, large);
        }

        long forSmall = DecodeImage(filter, small);
        long forLarge = DecodeImage(filter, large);

        Assert.InRange(forLarge, 0, forSmall);
    }

    private static long Decode(DctDecodeFilter filter, byte[] jpeg, ArrayBufferWriter<byte> output, FilterContext context)
    {
        output.ResetWrittenCount();
        long before = GC.GetAllocatedBytesForCurrentThread();
        filter.Decode(jpeg, output, context);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static long DecodeImage(DctDecodeFilter filter, byte[] jpeg)
    {
        var context = new ImageFilterContext(new FilterContext());
        long before = GC.GetAllocatedBytesForCurrentThread();
        using DecodedImage? image = filter.DecodeImage(jpeg, context);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.NotNull(image);
        return allocated;
    }
}
