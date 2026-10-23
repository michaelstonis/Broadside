using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Issue #362: <c>DCTDecode</c>'s image facet at 1/1, 1/2, 1/4 and 1/8 (ITU-T T.81 §A.3.3; the reduced IDCTs ported from
/// libjpeg-turbo's <c>jidctred.c</c>) on the catalogue-class photograph (<see cref="CatalogueSamples.Jpeg"/>, 2550 x 3150, baseline,
/// 4:4:4 and 4:2:0). Entropy decoding is the floor no scale goes below; the reduced IDCT, colour transform and output shrink with
/// the scale. Per block and row the decoder allocates nothing (<c>Broadside.Tests.Filters.Dct.DctAllocationTests</c>); what
/// <c>Allocated</c> shows is the image object per call, its samples pooled.
/// </summary>
[MemoryDiagnoser]
public class DctScaledBenchmarks
{
    private readonly FilterContext _context = new();
    private readonly DctDecodeFilter _filter = new();
    private byte[] _jpeg = [];

    /// <summary>The chroma subsampling of the photograph: 4:4:4 (the catalogue cover's) or 4:2:0.</summary>
    [Params("444", "420")]
    public string Sampling { get; set; } = "444";

    /// <summary>The denominator of the scale: 1, 2, 4 or 8.</summary>
    [Params(1, 2, 4, 8)]
    public int Scale { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        _jpeg = CatalogueSamples.Jpeg(subsampling: Sampling == "444" ? DctChromaSubsampling.None : DctChromaSubsampling.HorizontalAndVertical);
        if (Decode() == 0)
        {
            throw new InvalidOperationException("The photograph decoded to nothing.");
        }
    }

    [Benchmark]
    public int Decode()
    {
        using DecodedImage image = _filter.DecodeImage(_jpeg, new ImageFilterContext(_context) { Reduction = System.Numerics.BitOperations.Log2((uint)Scale) })!;
        return image.DecodedRows;
    }
}
