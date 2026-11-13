using BenchmarkDotNet.Attributes;
using Broadside.Filters.Jpx;

namespace Broadside.Benchmarks;

/// <summary>
/// JPEG 2000 tier-1 alone (ITU-T T.800 Annex D, issue #255): every coding pass of 64 x 64 code-blocks, 28 passes from bit-plane 9
/// down, over pseudo-random bytes, so the MQ decisions are close to even and most samples become significant in the first planes, as
/// in the low bit-planes of a lossless image (the heavy case of <c>pdfjs/issue5567.pdf</c>). The mean is per sample (one code-block
/// is 4,096): the tier-1 nanoseconds per sample the issue tracks. <c>Default</c> is the usual code-block style;
/// <c>BypassCausal</c> adds the selective bypass (raw passes from the eleventh, D.6) and vertically causal contexts (D.7). Zero
/// allocation per code-block: the decoder reuses its pooled state (<c>JpxAllocationTests</c> fails the build otherwise).
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases it in [GlobalCleanup].")]
public class JpxTier1Benchmarks
{
    private const int Blocks = 8;
    private const int Side = 64;
    private const int SegmentLength = 12 * 1024;

    private readonly JpxSegment[] _segments = [new(0, SegmentLength, 28)];
    private readonly int[] _output = new int[Side * Side];
    private byte[] _data = [];
    private JpxCodeBlockDecoder? _decoder;
    private JpxBlockCoding _coding;

    [Params("Default", "BypassCausal")]
    public string Style { get; set; } = "Default";

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[Blocks * SegmentLength];
#pragma warning disable CA5394 // benchmark input, not security
        new Random(255).NextBytes(_data);
#pragma warning restore CA5394
        _decoder = new JpxCodeBlockDecoder();
        int style = Style == "Default" ? 0 : JpxComponentStyle.Bypass | JpxComponentStyle.VerticallyCausal;
        _coding = new JpxBlockCoding(Side, Side, 1, style, 9, 0, 0);
    }

    [GlobalCleanup]
    public void Cleanup() => _decoder?.Dispose();

    [Benchmark(OperationsPerInvoke = Blocks * Side * Side)]
    public int Decode()
    {
        int sum = 0;
        for (int block = 0; block < Blocks; block++)
        {
            _decoder!.Decode(_data.AsSpan(block * SegmentLength, SegmentLength), _segments, _coding, _output, Side);
            sum += _output[block];
        }

        return sum;
    }
}
