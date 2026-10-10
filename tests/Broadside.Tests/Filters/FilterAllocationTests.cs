using System.Buffers;
using System.Text;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// Filters are a hot path and allocate nothing per byte, code or row (CLAUDE.md, "Code conventions"). This is the build-breaking
/// half of that rule; the <c>*Benchmarks</c> classes in <c>bench/Broadside.Benchmarks</c> are the measuring half. ISO 32000-2 §7.4.
/// </summary>
[Collection(HeavyTestCollection.Name)]
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

        long allocated = Allocations.Measure(() => Decode(filter, encoded, output, context));

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

        long forSmall = Allocations.Measure(() => Decode(filter, small, output, context));
        long forLarge = Allocations.Measure(() => Decode(filter, large, output, context));

        Assert.InRange(forLarge, 0, forSmall + 64);
    }

    [Fact]
    public void Decoding_a_predicted_stream_through_a_document_allocates_per_stream_not_per_row()
    {
        long forSmall = PredictedStreamAllocation(rows: 10);
        long forLarge = PredictedStreamAllocation(rows: 20_000);

        Assert.InRange(forLarge, 0, forSmall + 64);
    }

    // Found by libFuzzer (issue #48): Columns sized the row buffers and the zero padding of the last row, so a few bytes of data
    // with a huge Columns allocated and wrote rows of up to the decoded-length limit (1 GiB) and ran out of memory. Data that
    // cannot hold one whole row is now passed through, so the output is never more than twice the input.
    [Theory]
    [InlineData(12)]
    [InlineData(2)]
    public void A_row_longer_than_the_whole_data_allocates_nothing_in_proportion_to_the_row(int predictor)
    {
        byte[] file = FilterTesting.FileWithStream(
            $"/Filter /FlateDecode /DecodeParms << /Predictor {predictor} /Colors 4 /BitsPerComponent 16 /Columns 100000000 >>",
            FilterEncoders.Zlib([2, 10, 20, 30]));
        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);
        long allocated = Allocations.Measure(() => FilterTesting.DecodeWithCodes(file), warmUpCalls: 1);

        Assert.Equal([2, 10, 20, 30], decoded);
        Assert.Equal(["PredictorInvalid"], codes);
        Assert.True(allocated < 1 << 20, $"Decoding 4 bytes allocated {allocated} bytes.");
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

        long allocated = Allocations.Measure(() =>
        {
            output.ResetWrittenCount();
            document.DecodeStream(stream, output);
        });
        Assert.Equal(rows * 4, output.WrittenCount);
        return allocated;
    }

    private static void Decode(IStreamFilter filter, byte[] encoded, ArrayBufferWriter<byte> output, FilterContext context)
    {
        output.ResetWrittenCount();
        filter.Decode(encoded, output, context);
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
