using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// JBIG2Decode through the image facet over a synthesized 1728 x 2200 page (A4 at 200 dpi) coded as one generic region (template 0
/// with typical prediction, and MMR), as an arithmetic symbol dictionary of 40 symbols with a text region of 4,000 instances (every
/// tenth refined), and as a 16-pattern dictionary with a halftone region of 432 x 550 cells (ISO 32000-2 §7.4.7; ITU-T T.88 §6.2 to
/// §6.7). A codec hot path: the decode allocates per stream (segment lists, the reporter, the image object), per dictionary and per
/// symbol, and nothing per row, pixel, symbol instance or grid cell; <c>Broadside.Tests.Filters.Jbig2AllocationTests</c> fails the
/// build otherwise.
/// </summary>
[MemoryDiagnoser]
public class Jbig2Benchmarks
{
    private const int Width = 1728;
    private const int Height = 2200;

    private readonly Jbig2DecodeFilter _filter = new();
    private byte[] _arithmetic = [];
    private byte[] _mmr = [];
    private byte[] _text = [];
    private byte[] _halftone = [];
    private ImageFilterContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        bool[][] page = CcittEncoder.SampleBitmap(Width, Height);
        byte[] info = Jbig2Encoder.Segment(0, 48, 1, Jbig2Encoder.PageInformation(Width, Height));
        _arithmetic = [.. info, .. Jbig2Encoder.Segment(1, 39, 1, Jbig2Encoder.GenericRegion(page, 0, 0, 0, mmr: false, 0, typicalPrediction: true))];
        _mmr = [.. info, .. Jbig2Encoder.Segment(1, 39, 1, Jbig2Encoder.GenericRegion(page, 0, 0, 0, mmr: true))];
        bool[][][] symbols = [.. Enumerable.Range(0, 40).Select(i => CcittEncoder.SampleBitmap(8 + (i % 9), 10 + (i % 7), seed: 100 + i))];
        byte[] dictionary = Jbig2Encoder.SymbolDictionary(symbols, 0, out int[] order);
        bool[][][] ordered = [.. order.Select(i => symbols[i])];
        Jbig2Instance[] instances = [.. Enumerable.Range(0, 4000).Select(i => new Jbig2Instance(
            (i * 7) % 40, 20 + ((i % 80) * 21), 20 + ((i / 80) * 42), Refined: i % 10 == 9 ? CcittEncoder.SampleBitmap(14, 16, seed: i) : null))];
        _text = [
            .. info,
            .. Jbig2Encoder.Segment(1, 0, 1, [], dictionary),
            .. Jbig2Encoder.Segment(2, 6, 1, [1], Jbig2Encoder.TextRegion(Width, Height, 0, 0, 0, ordered, instances, new Jbig2TextOptions { Refine = true, LogStrips = 2 })),
        ];
        bool[][][] patterns = [.. Enumerable.Range(0, 16).Select(i => CcittEncoder.SampleBitmap(4, 4, seed: 200 + i))];
        int[][] gray = [.. Enumerable.Range(0, Height / 4).Select(m => Enumerable.Range(0, Width / 4).Select(n => ((m / 8) + (n / 8)) % 16).ToArray())];
        _halftone = [
            .. info,
            .. Jbig2Encoder.Segment(1, 16, 1, Jbig2Encoder.PatternDictionary(patterns, 4, 0, mmr: false)),
            .. Jbig2Encoder.Segment(2, 22, 1, [1], Jbig2Encoder.HalftoneRegion(Width, Height, 0, gray, 16, 4, 0, 0, 0, mmr: false)),
        ];
        _context = new ImageFilterContext(new FilterContext()) { Width = Width, Height = Height, BitsPerComponent = 1, ColorComponents = 1 };
        if (DecodeGenericTemplate0Tpgd() == 0 || DecodeGenericMmr() == 0 || DecodeTextRegion() == 0 || DecodeHalftone() == 0)
        {
            throw new InvalidOperationException("The page did not decode.");
        }
    }

    [Benchmark]
    public int DecodeGenericTemplate0Tpgd() => Decode(_arithmetic);

    [Benchmark]
    public int DecodeGenericMmr() => Decode(_mmr);

    [Benchmark]
    public int DecodeTextRegion() => Decode(_text);

    [Benchmark]
    public int DecodeHalftone() => Decode(_halftone);

    private int Decode(byte[] data)
    {
        using DecodedImage? image = _filter.DecodeImage(data, _context);
        return image?.Samples.Length ?? 0;
    }
}
