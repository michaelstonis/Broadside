using BenchmarkDotNet.Attributes;
using Broadside.Filters.Codecs;
using Broadside.Filters.Jpx;

namespace Broadside.Benchmarks;

/// <summary>
/// The JPEG 2000 inverse wavelet transform and component transforms (ITU-T T.800 F.3, G.2, G.3; issue #477): a 1,024 x 768
/// tile-component of five levels through the 5/3 (<c>Inverse53</c>) and 9/7 (<c>Inverse97</c>) filters, and the inverse RCT and ICT
/// over 64 Ki samples of three components, each through the scalar method (<c>*Scalar</c>) and the dispatcher (the <c>Vector128</c>
/// path where it is accelerated). Every call first copies its input in (the transforms work in place). <c>Allocated</c> must read
/// <c>-</c>; the vector paths' bit-exactness is <c>Broadside.Tests.Filters.JpxLiftingKernelTests</c>.
/// </summary>
[MemoryDiagnoser]
public class JpxWaveletBenchmarks
{
    private const int Width = 1024;
    private const int Height = 768;
    private const int Levels = 5;
    private const int Samples = 1 << 16;
    private readonly CodecReporter _reporter = new();
    private readonly int[] _integers = new int[Width * Height];
    private readonly float[] _floats = new float[Width * Height];
    private readonly int[] _intBuffer = new int[Width * Height];
    private readonly float[] _floatBuffer = new float[Width * Height];
    private readonly int[] _rctSource = new int[3 * Samples];
    private readonly int[] _rct = new int[3 * Samples];
    private readonly float[] _ictSource = new float[3 * Samples];
    private readonly float[] _ict = new float[3 * Samples];
    private JpxTileComponent _component = null!;

    [GlobalSetup]
    public void Setup()
    {
        var resolutions = new JpxResolution[Levels + 1];
        for (int r = 0; r <= Levels; r++)
        {
            int shift = Levels - r;
            resolutions[r] = new JpxResolution
            {
                Width = (Width + (1 << shift) - 1) >> shift,
                Height = (Height + (1 << shift) - 1) >> shift,
                NextLayer = [],
                Bands = [],
                PrecinctBands = [],
            };
        }

        _component = new JpxTileComponent
        {
            Width = Width,
            Height = Height,
            Style = new JpxComponentStyle { Levels = Levels, Precincts = [] },
            Resolutions = resolutions,
        };
#pragma warning disable CA5394 // benchmark input, not security
        var random = new Random(477);
        for (int i = 0; i < _integers.Length; i++)
        {
            _integers[i] = random.Next(-2048, 2048);
            _floats[i] = (float)((random.NextDouble() - 0.5) * 4096);
        }

        for (int i = 0; i < _rct.Length; i++)
        {
            _rctSource[i] = random.Next(-255, 256);
            _ictSource[i] = random.Next(-255, 256) / 2f;
        }
#pragma warning restore CA5394
    }

    [Benchmark]
    public int Inverse53()
    {
        _integers.AsSpan().CopyTo(_intBuffer);
        JpxWavelet.Inverse53(_component, _intBuffer, _reporter, Levels);
        return _intBuffer[^1];
    }

    [Benchmark]
    public float Inverse97()
    {
        _floats.AsSpan().CopyTo(_floatBuffer);
        JpxWavelet.Inverse97(_component, _floatBuffer, _reporter, Levels);
        return _floatBuffer[^1];
    }

    [Benchmark]
    public int InverseRctScalar()
    {
        _rctSource.AsSpan().CopyTo(_rct);
        JpxLifting.InverseRctScalar(_rct.AsSpan(0, Samples), _rct.AsSpan(Samples, Samples), _rct.AsSpan(2 * Samples, Samples));
        return _rct[^1];
    }

    [Benchmark]
    public int InverseRct()
    {
        _rctSource.AsSpan().CopyTo(_rct);
        JpxLifting.InverseRct(_rct.AsSpan(0, Samples), _rct.AsSpan(Samples, Samples), _rct.AsSpan(2 * Samples, Samples));
        return _rct[^1];
    }

    [Benchmark]
    public float InverseIctScalar()
    {
        _ictSource.AsSpan().CopyTo(_ict);
        JpxLifting.InverseIctScalar(_ict.AsSpan(0, Samples), _ict.AsSpan(Samples, Samples), _ict.AsSpan(2 * Samples, Samples));
        return _ict[^1];
    }

    [Benchmark]
    public float InverseIct()
    {
        _ictSource.AsSpan().CopyTo(_ict);
        JpxLifting.InverseIct(_ict.AsSpan(0, Samples), _ict.AsSpan(Samples, Samples), _ict.AsSpan(2 * Samples, Samples));
        return _ict[^1];
    }
}
