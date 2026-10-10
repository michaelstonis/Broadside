using System.Buffers;
using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Filters.Ccitt;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// CCITTFaxDecode over a synthesized 1728 x 2200 page (A4 at 200 dpi): Group 4, Group 3 2-D (K 4, EOLs) and Group 3 1-D, through
/// the filter contract into a reused buffer; <see cref="CcittMmrBenchmarks"/> runs the Group 4 data through the JBIG2 MMR seam (ISO 32000-2 §7.4.6; ITU-T T.4,
/// T.6, T.88 §6.2.6). A codec hot path: <c>Allocated</c> must read <c>-</c>; <c>Broadside.Tests.Filters.CcittAllocationTests</c>
/// fails the build if it does not.
/// </summary>
[MemoryDiagnoser]
public class CcittBenchmarks
{
    private const int Columns = 1728;
    private const int Rows = 2200;
    private const int RowBytes = Columns / 8;

    private readonly CcittFaxDecodeFilter _filter = new();
    private readonly ArrayBufferWriter<byte> _output = new(RowBytes * Rows);
    private FilterContext _context = new();
    private byte[] _encoded = [];

    /// <summary>The K parameter: -1 (Group 4), 0 (Group 3 1-D) or 4 (Group 3 2-D).</summary>
    [Params(-1, 0, 4)]
    public int K { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        bool[][] page = CcittEncoder.SampleBitmap(Columns, Rows);
        _encoded = CcittEncoder.Encode(page, new CcittEncoding(K: K, EndOfLine: K > 0));
        _context = new FilterContext
        {
            Parameters = new CosDictionary
            {
                [new CosName("K")] = new CosInteger(K),
                [new CosName("Columns")] = new CosInteger(Columns),
                [new CosName("EndOfLine")] = K > 0 ? CosBoolean.True : CosBoolean.False,
            },
        };
        if (Decode() != RowBytes * Rows)
        {
            throw new InvalidOperationException("The page did not decode to 2200 rows.");
        }
    }

    [Benchmark]
    public int Decode()
    {
        _output.ResetWrittenCount();
        _filter.Decode(_encoded, _output, _context);
        return _output.WrittenCount;
    }
}

/// <summary>The Group 4 page of <see cref="CcittBenchmarks"/> through the JBIG2 MMR seam (ITU-T T.88 §6.2.6): no PDF framing.</summary>
[MemoryDiagnoser]
public class CcittMmrBenchmarks
{
    private const int Columns = 1728;
    private const int Rows = 2200;
    private const int RowBytes = Columns / 8;

    private readonly byte[] _bitmap = new byte[RowBytes * Rows];
    private byte[] _encoded = [];

    [GlobalSetup]
    public void Setup() => _encoded = CcittEncoder.Encode(CcittEncoder.SampleBitmap(Columns, Rows), new CcittEncoding(K: -1));

    [Benchmark]
    public int DecodeMmr() => MmrDecoder.Decode(_encoded, Columns, Rows, _bitmap, RowBytes).Rows;
}
