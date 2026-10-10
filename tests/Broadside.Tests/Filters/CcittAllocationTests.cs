using System.Buffers;
using Broadside.Filters;
using Broadside.Filters.Ccitt;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// CCITT fax decoding is a codec hot path: nothing is allocated per row or code, through the filter contract, the image facet or
/// the MMR seam (CLAUDE.md, "Code conventions"). <c>CcittBenchmarks</c> in <c>bench/Broadside.Benchmarks</c> is the measuring half.
/// ISO 32000-2 §7.4.6.
/// </summary>
public class CcittAllocationTests
{
    public static TheoryData<int> Modes => new() { -1, 0, 4 };

    [Theory]
    [MemberData(nameof(Modes))]
    public void Decoding_a_page_through_the_filter_contract_allocates_nothing(int k)
    {
        bool[][] page = CcittEncoder.SampleBitmap(1728, 400);
        byte[] encoded = CcittEncoder.Encode(page, new CcittEncoding(K: k, EndOfLine: k > 0));
        var filter = new CcittFaxDecodeFilter();
        var context = new FilterContext { Parameters = Parameters(k) };
        var output = new ArrayBufferWriter<byte>(216 * 400);

        // Past tiered compilation's call-count threshold first, so the runtime's own bookkeeping is not counted.
        for (int warmUp = 0; warmUp < 40; warmUp++)
        {
            output.ResetWrittenCount();
            filter.Decode(encoded, output, context);
        }

        output.ResetWrittenCount();
        long before = GC.GetAllocatedBytesForCurrentThread();
        filter.Decode(encoded, output, context);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(216 * 400, output.WrittenCount);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void The_image_facet_allocates_per_image_not_per_row()
    {
        long forSmall = ImageAllocation(rows: 8);
        long forLarge = ImageAllocation(rows: 2_000);

        Assert.InRange(forLarge, 0, forSmall + 64);
    }

    [Fact]
    public void MMR_decoding_allocates_nothing()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(500, 300);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1));
        byte[] destination = new byte[63 * 300];
        for (int warmUp = 0; warmUp < 40; warmUp++)
        {
            MmrDecoder.Decode(encoded, 500, 300, destination, 63);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        MmrResult result = MmrDecoder.Decode(encoded, 500, 300, destination, 63);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(MmrStatus.Ok, result.Status);
        Assert.Equal(0, allocated);
    }

    private static long ImageAllocation(int rows)
    {
        byte[] encoded = CcittEncoder.Encode(CcittEncoder.SampleBitmap(300, rows), new CcittEncoding(K: -1));
        var filter = new CcittFaxDecodeFilter();
        var context = new ImageFilterContext(new FilterContext { Parameters = Parameters(-1, 300) }) { Width = 300, Height = rows, BitsPerComponent = 1, ColorComponents = 1 };
        for (int warmUp = 0; warmUp < 40; warmUp++)
        {
            filter.DecodeImage(encoded, context)!.Dispose();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        filter.DecodeImage(encoded, context)!.Dispose();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static CosDictionary Parameters(int k, int columns = 1728) => new()
    {
        [new CosName("K")] = new CosInteger(k),
        [new CosName("Columns")] = new CosInteger(columns),
        [new CosName("EndOfLine")] = k > 0 ? CosBoolean.True : CosBoolean.False,
    };
}
