using BenchmarkDotNet.Attributes;
using Broadside.Images;

namespace Broadside.Benchmarks;

/// <summary>
/// The box resampler of the save-time image pass (issue #625; ISO 32000-2 §8.9.5.3): one 2400 × 2400 image (a 600 dpi scan of a
/// 4-inch square) reduced to 150 dpi, as RGB, grey and bitonal samples, and at a ratio that is not a whole number. One call is one
/// image; the column map and accumulators are rented once per image, never per row, so <c>Allocated</c> must read <c>-</c>;
/// <c>Broadside.Tests.Images.ImageResamplerAllocationTests</c> fails the build if it does not.
/// </summary>
[MemoryDiagnoser]
public class ImageResampleBenchmarks
{
    private const int Size = 2400;

    private byte[] _rgb = [];
    private byte[] _gray = [];
    private byte[] _bits = [];
    private byte[] _target = [];

    [GlobalSetup]
    public void Setup()
    {
        _rgb = [.. Enumerable.Range(0, Size * Size * 3).Select(static i => (byte)((i * 31) ^ (i >> 9)))];
        _gray = [.. Enumerable.Range(0, Size * Size).Select(static i => (byte)((i * 17) ^ (i >> 11)))];
        _bits = [.. Enumerable.Range(0, Size / 8 * Size).Select(static i => (byte)((i * 13) ^ (i >> 5)))];
        _target = new byte[Size * Size * 3];
    }

    /// <summary>RGB, 4:1 on each axis.</summary>
    /// <returns>A sample of the result.</returns>
    [Benchmark]
    public byte Rgb()
    {
        ImageResampler.Resample(_rgb, Size, Size, 3, 8, 8, 600, 600, 8, _target);
        return _target[12345];
    }

    /// <summary>Grey, 2400 to 700: a ratio of 3.43, so most source samples are split between two targets.</summary>
    /// <returns>A sample of the result.</returns>
    [Benchmark]
    public byte GrayFractional()
    {
        ImageResampler.Resample(_gray, Size, Size, 1, 8, 8, 700, 700, 8, _target);
        return _target[12345];
    }

    /// <summary>A 1-bit scan, 4:1, thresholded to 1 bit.</summary>
    /// <returns>A sample of the result.</returns>
    [Benchmark]
    public byte Bitonal()
    {
        ImageResampler.Resample(_bits, Size, Size, 1, 1, 1, 600, 600, 1, _target);
        return _target[1234];
    }
}
