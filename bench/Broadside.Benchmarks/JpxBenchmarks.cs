using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// JPXDecode through the image facet (ISO 32000-2 §7.4.9; ITU-T T.800): the J.11 worked example and OpenJPEG-encoded lossless
/// vectors (64 x 48 RGB with four levels and three layers, small code-blocks with precincts, a tiled image with offsets). Allocation
/// is the image's structure (tiles, precincts, code-block records, one tier-1 scratch) and nothing per layer, pass or byte;
/// <c>Broadside.Tests.Filters.JpxAllocationTests</c> fails the build otherwise.
/// </summary>
[MemoryDiagnoser]
public class JpxBenchmarks
{
    private readonly JpxDecodeFilter _filter = new();
    private byte[] _data = [];
    private ImageFilterContext _context = null!;

    [Params("WorkedExample", "Medium", "PrecinctsAndSmallBlocks", "Tiled")]
    public string Vector { get; set; } = "Medium";

    [GlobalSetup]
    public void Setup()
    {
        _data = Vector == "WorkedExample" ? JpxSamples.WorkedExample : JpxSamples.Vector(Vector).Data;
        _context = new ImageFilterContext(new FilterContext());
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
