using System.Text;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// Issue #566: a stream decoded in slices (<see cref="StreamDecoder.ChainJob"/>, Flate a chunk at a time and predictor rows a few at a
/// time) gives the bytes, the outcome and the diagnostics of the one-step decode, for well-formed and damaged data alike. ISO 32000-2
/// §7.3.8.2 and §7.4.4.1 to §7.4.4.4.
/// </summary>
public sealed class SlicedChainDecodeTests
{
    private const int Columns = 300;

    public static TheoryData<string> Cases => new(
        "flate", "png", "png-unknown-tag", "png-partial-row", "tiff-16", "truncated", "corrupt", "raw-deflate", "lzw-png",
        "hex-flate-png", "unknown-predictor", "shorter-than-a-row", "over-the-limit", "no-filter", "empty");

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_stream_decoded_in_slices_equals_the_one_step_decode(string name)
    {
        (CosStream stream, long limit) = Stream(name);
        (byte[] whole, bool wholeComplete, string[] wholeDiagnostics, _) = Decode(stream, limit, sliced: false);
        (byte[] sliced, bool slicedComplete, string[] slicedDiagnostics, int slices) = Decode(stream, limit, sliced: true);

        Assert.Equal(whole, sliced);
        Assert.Equal(wholeComplete, slicedComplete);
        Assert.Equal(wholeDiagnostics, slicedDiagnostics);
        if (name is "png" or "flate")
        {
            // Over 200 KB inflate 16 KB per slice and 16 rows per slice: the decode is not one step.
            Assert.True(slices > 10, $"{name}: {slices} slices");
        }
    }

    [Fact]
    public void A_cancelled_slice_throws_and_returns_what_the_job_holds()
    {
        (CosStream stream, long limit) = Stream("png");
        var decoder = new StreamDecoder(FilterRegistry.Create([]), limit, new DiagnosticSink(strict: false), static value => value ?? CosNull.Instance);
        using var output = new PooledBufferWriter(256, limit);
        using var cancellation = new CancellationTokenSource();
        using StreamDecoder.ChainJob job = decoder.BeginDecode(stream, output, stopBeforeImageFilter: false, cancellation.Token);
        Assert.False(job.Continue(deadline: 0));
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => job.Continue(deadline: 0));
        Assert.True(job.Continue(deadline: 0));
        Assert.Equal(0, output.WrittenSpan.Length);
    }

    private static (byte[] Bytes, bool Complete, string[] Diagnostics, int Slices) Decode(CosStream stream, long limit, bool sliced)
    {
        var sink = new DiagnosticSink(strict: false);
        var decoder = new StreamDecoder(FilterRegistry.Create([]), limit, sink, static value => value ?? CosNull.Instance);
        using var output = new PooledBufferWriter(256, limit);
        bool complete;
        int slices = 1;
        if (sliced)
        {
            using StreamDecoder.ChainJob job = decoder.BeginDecode(stream, output, stopBeforeImageFilter: false, TestContext.Current.CancellationToken);
            while (!job.Continue(deadline: 0))
            {
                slices++;
            }

            complete = job.Outcome.Complete;
        }
        else
        {
            decoder.Decode(stream, output, 0, stopBeforeImageFilter: false, out StreamDecoder.ChainOutcome outcome, TestContext.Current.CancellationToken);
            complete = outcome.Complete;
        }

        return (output.WrittenSpan.ToArray(), complete, [.. sink.Snapshot().Select(static d => $"{d.Code} {d.Severity} {d.Message}")], slices);
    }

    private static (CosStream Stream, long Limit) Stream(string name)
    {
        byte[] samples = FilterEncoders.SampleData(Columns * 3 * 240, alphabet: 40);
        string png = $"<< /Predictor 15 /Colors 3 /Columns {Columns} >>";
        (string entries, byte[] data, long limit) = name switch
        {
            "flate" => ("/Filter /FlateDecode", FilterEncoders.Zlib(samples), 1L << 24),
            "png" => ($"/Filter /FlateDecode /DecodeParms {png}", FilterEncoders.Zlib(Png(samples, tagEvery: 0)), 1L << 24),
            "png-unknown-tag" => ($"/Filter /FlateDecode /DecodeParms {png}", FilterEncoders.Zlib(Png(samples, tagEvery: 7)), 1L << 24),
            "png-partial-row" => ($"/Filter /FlateDecode /DecodeParms {png}", FilterEncoders.Zlib(Png(samples, tagEvery: 0).AsSpan(0, 100_000)), 1L << 24),
            "tiff-16" => ($"/Filter /FlateDecode /DecodeParms << /Predictor 2 /Colors 3 /BitsPerComponent 16 /Columns {Columns / 2} >>", FilterEncoders.Zlib(samples), 1L << 24),
            "truncated" => ($"/Filter /FlateDecode /DecodeParms {png}", FilterEncoders.Zlib(Png(samples, tagEvery: 0)).AsSpan(0, 30_000).ToArray(), 1L << 24),
            "corrupt" => ($"/Filter /FlateDecode /DecodeParms {png}", Corrupt(FilterEncoders.Zlib(Png(samples, tagEvery: 0))), 1L << 24),
            "raw-deflate" => ($"/Filter /FlateDecode /DecodeParms {png}", FilterEncoders.Zlib(Png(samples, tagEvery: 0)).AsSpan(2).ToArray(), 1L << 24),
            "lzw-png" => ($"/Filter /LZWDecode /DecodeParms {png}", FilterEncoders.LzwEncode(Png(samples, tagEvery: 0)), 1L << 24),
            "hex-flate-png" => ($"/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null {png}]", Encoding.ASCII.GetBytes(Convert.ToHexString(FilterEncoders.Zlib(Png(samples, tagEvery: 0))) + ">"), 1L << 24),
            "unknown-predictor" => ("/Filter /FlateDecode /DecodeParms << /Predictor 7 >>", FilterEncoders.Zlib(samples), 1L << 24),
            "shorter-than-a-row" => ("/Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 100000 >>", FilterEncoders.Zlib(samples.AsSpan(0, 500)), 1L << 24),
            "over-the-limit" => ($"/Filter /FlateDecode /DecodeParms {png}", FilterEncoders.Zlib(Png(samples, tagEvery: 0)), 100_000),
            "no-filter" => (string.Empty, samples, 1L << 24),
            "empty" => ("/Filter /FlateDecode", [], 1L << 24),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };

        var dictionary = (CosDictionary)CosObject.Parse(Encoding.Latin1.GetBytes($"<< {entries} >>"));
        return (new CosStream(dictionary, data), limit);
    }

    /// <summary>PNG rows of three-byte pixels, the algorithm cycling 0 to 4 (the samples are not predicted: only the tags matter here).</summary>
    private static byte[] Png(byte[] samples, int tagEvery)
    {
        int stride = Columns * 3;
        int rows = samples.Length / stride;
        byte[] output = new byte[rows * (stride + 1)];
        for (int y = 0; y < rows; y++)
        {
            output[y * (stride + 1)] = tagEvery > 0 && y % tagEvery == 0 ? (byte)9 : (byte)(y % 5);
            samples.AsSpan(y * stride, stride).CopyTo(output.AsSpan((y * (stride + 1)) + 1));
        }

        return output;
    }

    private static byte[] Corrupt(byte[] zlib)
    {
        byte[] damaged = zlib.ToArray();
        damaged.AsSpan(damaged.Length / 2, 64).Fill(0xFF);
        return damaged;
    }
}
