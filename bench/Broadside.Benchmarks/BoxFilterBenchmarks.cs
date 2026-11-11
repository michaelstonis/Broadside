using BenchmarkDotNet.Attributes;
using Broadside.Rendering;

namespace Broadside.Benchmarks;

/// <summary>
/// The image sampler's box filter (ISO 32000-2 §8.9.5.3, §10.7.4; issue #475), through the scalar method (<c>Scalar</c>) and the
/// dispatcher (the <c>Vector128</c> path where it is accelerated): a 794-pixel row (a page width at 96 DPI) of a reduced image, each
/// device pixel averaging a 4 × 4 footprint of random samples with fractional weights at its edges, as an image drawn at a quarter of
/// its size takes. <c>Allocated</c> must read <c>-</c>; the vector path's bit-exactness is
/// <c>Broadside.Rendering.Tests.BoxFilterKernelTests</c>. <see cref="ScaledImageBenchmarks"/>' <c>Replay</c> (full decode) is the
/// whole-page measure.
/// </summary>
[MemoryDiagnoser]
public class BoxFilterBenchmarks
{
    private const int Pixels = 794;
    private const int Taps = 4;
    private const int Width = Pixels * Taps;
    private readonly uint[] _image = new uint[Width * Taps];
    private readonly int[] _rows = [0, Width, 2 * Width, 3 * Width];
    private readonly double[] _rowWeights = [0.4, 1, 1, 0.6];
    private readonly double[] _columnWeights = [0.7, 1, 1, 0.3];

    [GlobalSetup]
    public void Setup()
    {
#pragma warning disable CA5394 // benchmark input, not security
        var random = new Random(475);
        for (int i = 0; i < _image.Length; i++)
        {
            _image[i] = (uint)random.Next() ^ ((uint)random.Next(2) << 31);
        }
#pragma warning restore CA5394
    }

    [Benchmark]
    public uint AverageScalar()
    {
        uint sum = 0;
        for (int x = 0; x < Pixels; x++)
        {
            ReadOnlySpan<uint> image = _image.AsSpan(x * Taps);
            BoxFilter.AverageScalar(image, _rows, _rowWeights, Columns, _columnWeights, out uint result);
            sum += result;
        }

        return sum;
    }

    [Benchmark]
    public uint Average()
    {
        uint sum = 0;
        for (int x = 0; x < Pixels; x++)
        {
            ReadOnlySpan<uint> image = _image.AsSpan(x * Taps);
            BoxFilter.Average(image, _rows, _rowWeights, Columns, _columnWeights, out uint result);
            sum += result;
        }

        return sum;
    }

    private static ReadOnlySpan<int> Columns => [0, 1, 2, 3];
}
