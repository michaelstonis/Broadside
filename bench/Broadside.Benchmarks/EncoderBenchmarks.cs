using System.Buffers;
using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The managed default encoders (ISO 32000-2 §7.4.2 to §7.4.5) over 256 KB of sample data into a reused buffer, through the encoder
/// contract (issue #142). <c>Allocated</c> must read <c>-</c> for ASCIIHex, ASCII85 and RunLength, and a constant per call for Flate,
/// whose BCL deflater is created once per call; <c>Broadside.Tests.Filters.EncoderAllocationTests</c> fails the build otherwise.
/// </summary>
[MemoryDiagnoser]
public class StreamEncoderBenchmarks
{
    private const int Length = 256 * 1024;

    private readonly ArrayBufferWriter<byte> _output = new(Length * 3);
    private readonly StreamEncoderContext _context = new();
    private byte[] _data = [];
    private IStreamEncoder _encoder = new FlateEncoder();

    /// <summary>The filter whose encoder runs.</summary>
    [Params("ASCIIHexDecode", "ASCII85Decode", "FlateDecode", "RunLengthDecode")]
    public string Filter { get; set; } = "FlateDecode";

    [GlobalSetup]
    public void Setup()
    {
        _data = FilterEncoders.SampleData(Length);
        _encoder = StreamEncoderRegistry.Default.TryGet(new CosName(Filter), out IStreamEncoder? encoder)
            ? encoder
            : throw new InvalidOperationException($"No encoder for {Filter}.");
    }

    [Benchmark]
    public int Encode()
    {
        _output.ResetWrittenCount();
        _encoder.Encode(_data, _output, _context);
        return _output.WrittenCount;
    }
}

/// <summary>
/// The predictor functions (§7.4.4.4) applied before Flate, over 256 KB of rows of 256 RGB samples: the predictor alone, and the
/// whole FlateDecode-with-predictor stream as a document encodes it. Allocation is per stream (two pooled buffers and the deflater),
/// never per row.
/// </summary>
[MemoryDiagnoser]
public class PredictorEncodeBenchmarks
{
    private const int Columns = 256;
    private const int Rows = 341;

    private readonly ArrayBufferWriter<byte> _output = new(((Columns * 3) + 1) * (Rows + 1));
    private readonly StreamEncoder _pipeline = new(StreamEncoderRegistry.Default, static value => value ?? CosNull.Instance);
    private CosDictionary _parameters = new();
    private CosDictionary _stream = new();
    private byte[] _samples = [];

    /// <summary>The Predictor value: 2 (TIFF) or 15 (PNG optimum, every algorithm tried on every row).</summary>
    [Params(2, 15)]
    public int Predictor { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _parameters = new CosDictionary
        {
            [new CosName("Predictor")] = new CosInteger(Predictor),
            [new CosName("Colors")] = new CosInteger(3),
            [new CosName("Columns")] = new CosInteger(Columns),
        };
        _stream = new CosDictionary { [new CosName("Filter")] = new CosName("FlateDecode"), [new CosName("DecodeParms")] = _parameters };
        _samples = FilterEncoders.SampleData(Columns * 3 * Rows, alphabet: 200);
    }

    [Benchmark]
    public int Predict()
    {
        _output.ResetWrittenCount();
        PredictorEncoder.Encode(_samples, _output, _parameters, static value => value ?? CosNull.Instance);
        return _output.WrittenCount;
    }

    [Benchmark]
    public int FlateWithPredictor()
    {
        _output.ResetWrittenCount();
        _pipeline.Encode(_samples, _stream, _output, CompressionLevel.Optimal);
        return _output.WrittenCount;
    }
}
