using System.Buffers;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// Encoders are a hot path and allocate per stream, never per byte or row (CLAUDE.md, "Code conventions"; issue #142). The
/// build-breaking half of that rule; <c>StreamEncoderBenchmarks</c> and <c>PredictorEncodeBenchmarks</c> in
/// <c>bench/Broadside.Benchmarks</c> are the measuring half. ISO 32000-2 §7.4.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class EncoderAllocationTests
{
    public static TheoryData<string> AllocationFreeEncoders => new() { "ASCIIHexDecode", "ASCII85Decode", "RunLengthDecode" };

    [Theory]
    [MemberData(nameof(AllocationFreeEncoders))]
    public void Encoding_through_the_contract_allocates_nothing(string name)
    {
        IStreamEncoder encoder = StreamEncoderTests.Pair(name).Encoder;
        byte[] data = FilterEncoders.SampleData(200_000);
        var context = new StreamEncoderContext();
        var output = new ArrayBufferWriter<byte>(600_000);

        long allocated = Allocations.Measure(() =>
        {
            output.ResetWrittenCount();
            encoder.Encode(data, output, context);
        });

        Assert.True(output.WrittenCount > 1_000);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Flate_allocates_the_deflater_once_per_call_and_nothing_per_byte()
    {
        var encoder = new FlateEncoder();
        var context = new StreamEncoderContext();
        var output = new ArrayBufferWriter<byte>(1_000_000);
        byte[] small = FilterEncoders.SampleData(1_000);
        byte[] large = FilterEncoders.SampleData(900_000);

        long forSmall = Allocations.Measure(() =>
        {
            output.ResetWrittenCount();
            encoder.Encode(small, output, context);
        });
        long forLarge = Allocations.Measure(() =>
        {
            output.ResetWrittenCount();
            encoder.Encode(large, output, context);
        });

        Assert.InRange(forLarge, 0, forSmall + 64);
    }

    [Fact]
    public void Encoding_a_predicted_stream_through_a_document_allocates_per_stream_not_per_row()
    {
        // The same 48,000 bytes as 12 rows of 4,000 and as 4,800 rows of 10: an allocation per row would cost the second
        // thousands of times what it costs the first. Zeros, so that both compress to a few bytes and the encoded data the
        // document keeps costs the same.
        long forFewRows = PredictedStreamAllocation(rows: 12, columns: 4_000);
        long forManyRows = PredictedStreamAllocation(rows: 4_800, columns: 10);

        Assert.InRange(forManyRows, 0, forFewRows + 1024);
    }

    private static long PredictedStreamAllocation(int rows, int columns)
    {
        using PdfDocument document = PdfDocument.Create();
        byte[] data = new byte[rows * columns];
        var stream = new CosStream(
            new CosDictionary
            {
                [new CosName("Filter")] = new CosName("FlateDecode"),
                [new CosName("DecodeParms")] = new CosDictionary { [new CosName("Predictor")] = new CosInteger(15), [new CosName("Columns")] = new CosInteger(columns) },
            },
            ReadOnlyMemory<byte>.Empty);

        long allocated = Allocations.Measure(() => document.EncodeStream(stream, data));

        Assert.Equal(data, document.DecodeStream(stream).ToArray());
        return allocated;
    }
}
