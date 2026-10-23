using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Issue #362: JPXDecode with 0 to 3 resolution levels dropped (ITU-T T.800 B.5) on the OpenJPEG vectors with three levels or more:
/// <c>Medium</c> (64 x 48 RGB, 5/3, LRCP with three layers: the dropped packets are interleaved and read through) and
/// <c>IrreversibleTiled</c> (128 x 96 RGB, 9/7, four tiles). Tier-1 of the dropped sub-bands and their inverse DWT are skipped,
/// so time falls with each level; allocation is the image's structure, as in <see cref="JpxBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
public class JpxScaledBenchmarks
{
    private readonly JpxDecodeFilter _filter = new();
    private byte[] _data = [];
    private ImageFilterContext _context = null!;

    /// <summary>The vector.</summary>
    [Params("Medium", "IrreversibleTiled")]
    public string Vector { get; set; } = "Medium";

    /// <summary>The resolution levels dropped.</summary>
    [Params(0, 1, 2, 3)]
    public int Levels { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = JpxSamples.Vector(Vector).Data;
        _context = new ImageFilterContext(new FilterContext()) { Reduction = Levels };
        if (Decode() == 0)
        {
            throw new InvalidOperationException($"{Vector} did not decode.");
        }
    }

    [Benchmark]
    public int Decode()
    {
        using DecodedImage? image = _filter.DecodeImage(_data, _context);
        return image?.Samples.Length ?? 0;
    }
}
