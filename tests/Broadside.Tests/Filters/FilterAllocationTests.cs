using System.Buffers;
using System.Text;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// Filters are a hot path and allocate nothing per byte, code or row (CLAUDE.md, "Code conventions"). This is the build-breaking
/// half of that rule; the <c>*Benchmarks</c> classes in <c>bench/Broadside.Benchmarks</c> are the measuring half. ISO 32000-2 §7.4.
/// </summary>
public class FilterAllocationTests
{
    public static TheoryData<string> AllocationFreeFilters => new() { "ASCIIHexDecode", "ASCII85Decode", "LZWDecode", "RunLengthDecode" };

    [Theory]
    [MemberData(nameof(AllocationFreeFilters))]
    public void Decoding_through_the_contract_allocates_nothing(string name)
    {
        (IStreamFilter filter, byte[] encoded) = Encoded(name, FilterEncoders.SampleData(200_000));
        var context = new FilterContext();
        var output = new ArrayBufferWriter<byte>(400_000);

        // Past tiered compilation's call-count threshold first, so the runtime's own bookkeeping is not counted.
        for (int warmUp = 0; warmUp < 40; warmUp++)
        {
            Decode(filter, encoded, output, context);
        }

        long allocated = Decode(filter, encoded, output, context);

        Assert.True(output.WrittenCount >= 200_000);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Flate_allocates_the_inflater_once_per_call_and_nothing_per_byte()
    {
        var filter = new FlateDecodeFilter();
        var context = new FilterContext();
        var output = new ArrayBufferWriter<byte>(1_000_000);
        byte[] small = FilterEncoders.Zlib(FilterEncoders.SampleData(1_000));
        byte[] large = FilterEncoders.Zlib(FilterEncoders.SampleData(900_000));
        for (int warmUp = 0; warmUp < 40; warmUp++)
        {
            Decode(filter, small, output, context);
            Decode(filter, large, output, context);
        }

        long forSmall = Decode(filter, small, output, context);
        long forLarge = Decode(filter, large, output, context);

        Assert.InRange(forLarge, 0, forSmall + 64);
    }

    [Fact]
    public void Decoding_a_predicted_stream_through_a_document_allocates_per_stream_not_per_row()
    {
        long forSmall = PredictedStreamAllocation(rows: 10);
        long forLarge = PredictedStreamAllocation(rows: 20_000);

        Assert.InRange(forLarge, 0, forSmall + 64);
    }

    private static long PredictedStreamAllocation(int rows)
    {
        byte[] predicted = new byte[rows * 5];
        for (int row = 0; row < rows; row++)
        {
            predicted[row * 5] = (byte)(row % 5);
            predicted[(row * 5) + 1] = (byte)row;
        }

        byte[] file = FilterTesting.FileWithStream("/Filter /FlateDecode /DecodeParms << /Predictor 15 /Columns 4 >>", FilterEncoders.Zlib(predicted));
        using PdfDocument document = PdfDocument.Open(file);
        var stream = (CosStream)document.Resolve(FilterTesting.StreamReference);
        var output = new ArrayBufferWriter<byte>(rows * 4);
        for (int warmUp = 0; warmUp < 40; warmUp++)
        {
            output.ResetWrittenCount();
            document.DecodeStream(stream, output);
        }

        output.ResetWrittenCount();

        long before = GC.GetAllocatedBytesForCurrentThread();
        document.DecodeStream(stream, output);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(rows * 4, output.WrittenCount);
        return allocated;
    }

    private static long Decode(IStreamFilter filter, byte[] encoded, ArrayBufferWriter<byte> output, FilterContext context)
    {
        output.ResetWrittenCount();
        long before = GC.GetAllocatedBytesForCurrentThread();
        filter.Decode(encoded, output, context);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static (IStreamFilter Filter, byte[] Encoded) Encoded(string name, byte[] data) => name switch
    {
        "ASCIIHexDecode" => (new AsciiHexDecodeFilter(), Encoding.ASCII.GetBytes(Convert.ToHexString(data) + ">")),
        "ASCII85Decode" => (new Ascii85DecodeFilter(), FilterEncoders.Ascii85Encode(data)),
        "LZWDecode" => (new LzwDecodeFilter(), FilterEncoders.LzwEncode(data)),
        "RunLengthDecode" => (new RunLengthDecodeFilter(), RunLength(data)),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static byte[] RunLength(byte[] data)
    {
        var output = new List<byte>();
        for (int index = 0; index < data.Length; index += 64)
        {
            int count = Math.Min(64, data.Length - index);
            output.Add((byte)(count - 1));
            output.AddRange(data.AsSpan(index, count));
            output.Add(0xFD);
            output.Add(data[index]);
        }

        output.Add(0x80);
        return [.. output];
    }
}
