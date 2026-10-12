using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Shared shape of the filter benchmarks: decode 256 KB of sample data, encoded in setup, through the filter contract into a
/// reused buffer (ISO 32000-2 §7.4). Filters are a hot path: <c>Allocated</c> must read <c>-</c> for every filter except Flate,
/// whose BCL inflater costs a constant per call; <c>Broadside.Tests.Filters.FilterAllocationTests</c> fails the build otherwise.
/// </summary>
public abstract class FilterBenchmark
{
    /// <summary>The size of the decoded data.</summary>
    protected const int DecodedLength = 256 * 1024;

    private readonly ArrayBufferWriter<byte> _output = new(DecodedLength * 2);
    private readonly FilterContext _context;
    private byte[] _encoded = [];

    /// <summary>Initializes a new instance of the <see cref="FilterBenchmark"/> class.</summary>
    /// <param name="parameters">The filter's DecodeParms, or <see langword="null"/>.</param>
    protected FilterBenchmark(CosDictionary? parameters = null) => _context = new FilterContext { Parameters = parameters };

    /// <summary>Gets the filter under test.</summary>
    protected abstract IStreamFilter Filter { get; }

    [GlobalSetup]
    public void Setup()
    {
        _encoded = Encode(FilterEncoders.SampleData(DecodedLength));
        Decode();
        if (_output.WrittenCount != DecodedLength)
        {
            throw new InvalidOperationException($"{Filter.Name.Value} decoded {_output.WrittenCount} bytes, not {DecodedLength}.");
        }
    }

    [Benchmark]
    public int Decode()
    {
        _output.ResetWrittenCount();
        Filter.Decode(_encoded, _output, _context);
        return _output.WrittenCount;
    }

    /// <summary>Encodes the sample data the way the filter expects it.</summary>
    /// <param name="data">The data.</param>
    /// <returns>The encoded data.</returns>
    protected abstract byte[] Encode(byte[] data);
}

/// <summary>ASCIIHexDecode (§7.4.2).</summary>
[MemoryDiagnoser]
public class AsciiHexBenchmarks : FilterBenchmark
{
    protected override IStreamFilter Filter { get; } = new AsciiHexDecodeFilter();

    protected override byte[] Encode(byte[] data) => Encoding.ASCII.GetBytes(Convert.ToHexString(data) + ">");
}

/// <summary>ASCII85Decode (§7.4.3).</summary>
[MemoryDiagnoser]
public class Ascii85Benchmarks : FilterBenchmark
{
    protected override IStreamFilter Filter { get; } = new Ascii85DecodeFilter();

    protected override byte[] Encode(byte[] data) => FilterEncoders.Ascii85Encode(data);
}

/// <summary>LZWDecode with EarlyChange 1 (§7.4.4.2).</summary>
[MemoryDiagnoser]
public class LzwBenchmarks : FilterBenchmark
{
    protected override IStreamFilter Filter { get; } = new LzwDecodeFilter();

    protected override byte[] Encode(byte[] data) => FilterEncoders.LzwEncode(data);
}

/// <summary>FlateDecode through the BCL (§7.4.4.1): allocation is the inflater, once per call.</summary>
[MemoryDiagnoser]
public class FlateBenchmarks : FilterBenchmark
{
    protected override IStreamFilter Filter { get; } = new FlateDecodeFilter();

    protected override byte[] Encode(byte[] data) => FilterEncoders.Zlib(data);
}

/// <summary>RunLengthDecode (§7.4.5).</summary>
[MemoryDiagnoser]
public class RunLengthBenchmarks : FilterBenchmark
{
    protected override IStreamFilter Filter { get; } = new RunLengthDecodeFilter();

    protected override byte[] Encode(byte[] data) => FilterEncoders.RunLengthEncode(data);
}

/// <summary>
/// The PNG and TIFF predictors (§7.4.4.4) over 256 KB of rows of 256 RGB samples, undone as the pipeline does after LZW or Flate.
/// </summary>
[MemoryDiagnoser]
public class PredictorBenchmarks
{
    private const int Columns = 256;
    private const int Rows = 341;

    private readonly ArrayBufferWriter<byte> _output = new(Columns * 3 * (Rows + 1));
    private FilterContext _context = new();
    private byte[] _predicted = [];

    /// <summary>The Predictor value: 2 (TIFF) or 15 (PNG, rows cycling through the five algorithms).</summary>
    [Params(2, 15)]
    public int Predictor { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _context = new FilterContext
        {
            Parameters = new CosDictionary
            {
                [new CosName("Predictor")] = new CosInteger(Predictor),
                [new CosName("Colors")] = new CosInteger(3),
                [new CosName("Columns")] = new CosInteger(Columns),
            },
        };
        int row = Columns * 3;
        byte[] samples = FilterEncoders.SampleData(row * Rows, alphabet: 200);
        if (Predictor == 2)
        {
            _predicted = samples;
            return;
        }

        _predicted = new byte[(row + 1) * Rows];
        for (int index = 0; index < Rows; index++)
        {
            _predicted[index * (row + 1)] = (byte)(index % 5);
            samples.AsSpan(index * row, row).CopyTo(_predicted.AsSpan((index * (row + 1)) + 1));
        }
    }

    [Benchmark]
    public int Decode()
    {
        _output.ResetWrittenCount();
        Filters.Predictor.Decode(_predicted, _output, _context);
        return _output.WrittenCount;
    }
}
