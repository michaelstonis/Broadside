using BenchmarkDotNet.Attributes;
using Broadside.Graphics.Colors;

namespace Broadside.Benchmarks;

/// <summary>
/// The DeviceCMYK to RGB characterization (ISO 32000-2 §10.4.2.1; issue #476), through the scalar method (<c>Scalar</c>) and the
/// dispatcher (the <c>Vector128</c> path where it is accelerated): a 794-color row (a page width at 96 DPI) of random 8-bit CMYK
/// samples, as a CMYK image's row reaches it. <c>Allocated</c> must read <c>-</c>; the vector path's bit-exactness is
/// <c>Broadside.Tests.Colors.CmykCharacterizationKernelTests</c>.
/// </summary>
[MemoryDiagnoser]
public class CmykCharacterizationBenchmarks
{
    private const int Colors = 794;
    private readonly float[] _cmyk = new float[4 * Colors];
    private readonly float[] _rgb = new float[3 * Colors];

    [GlobalSetup]
    public void Setup()
    {
#pragma warning disable CA5394 // benchmark input, not security
        var random = new Random(476);
        for (int i = 0; i < _cmyk.Length; i++)
        {
            _cmyk[i] = (float)(random.Next(256) / 255.0);
        }
#pragma warning restore CA5394
    }

    [Benchmark]
    public float ToRgbRowScalar()
    {
        CmykCharacterization.ToRgbRowScalar(_cmyk, _rgb);
        return _rgb[^1];
    }

    [Benchmark]
    public float ToRgbRow()
    {
        CmykCharacterization.ToRgbRow(_cmyk, _rgb);
        return _rgb[^1];
    }
}
