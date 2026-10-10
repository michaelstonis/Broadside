using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// JBIG2Decode through the image facet over a synthesized 1728 x 2200 page (A4 at 200 dpi) coded as one generic region: template 0
/// with typical prediction, and MMR (ISO 32000-2 §7.4.7; ITU-T T.88 §6.2.5, §6.2.6). A codec hot path: the decode allocates per
/// stream (segment lists, the reporter, the image object) and nothing per row or pixel; <c>Broadside.Tests.Filters.Jbig2AllocationTests</c>
/// fails the build otherwise.
/// </summary>
[MemoryDiagnoser]
public class Jbig2Benchmarks
{
    private const int Width = 1728;
    private const int Height = 2200;

    private readonly Jbig2DecodeFilter _filter = new();
    private byte[] _arithmetic = [];
    private byte[] _mmr = [];
    private ImageFilterContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        bool[][] page = CcittEncoder.SampleBitmap(Width, Height);
        byte[] info = Jbig2Encoder.Segment(0, 48, 1, Jbig2Encoder.PageInformation(Width, Height));
        _arithmetic = [.. info, .. Jbig2Encoder.Segment(1, 39, 1, Jbig2Encoder.GenericRegion(page, 0, 0, 0, mmr: false, 0, typicalPrediction: true))];
        _mmr = [.. info, .. Jbig2Encoder.Segment(1, 39, 1, Jbig2Encoder.GenericRegion(page, 0, 0, 0, mmr: true))];
        _context = new ImageFilterContext(new FilterContext()) { Width = Width, Height = Height, BitsPerComponent = 1, ColorComponents = 1 };
        if (DecodeGenericTemplate0Tpgd() == 0 || DecodeGenericMmr() == 0)
        {
            throw new InvalidOperationException("The page did not decode.");
        }
    }

    [Benchmark]
    public int DecodeGenericTemplate0Tpgd() => Decode(_arithmetic);

    [Benchmark]
    public int DecodeGenericMmr() => Decode(_mmr);

    private int Decode(byte[] data)
    {
        using DecodedImage? image = _filter.DecodeImage(data, _context);
        return image?.Samples.Length ?? 0;
    }
}
