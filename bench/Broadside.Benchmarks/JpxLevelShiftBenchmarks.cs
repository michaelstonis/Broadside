using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Broadside.Filters.Jpx;

namespace Broadside.Benchmarks;

/// <summary>
/// The JPEG 2000 inverse DC level shift and clipping of a row (ITU-T T.800 G.1.2; issue #503) over 64 Ki samples of an 8-bit
/// component: the 9/7 path rounding single-precision values first (<c>RoundShift</c>), the 5/3 path's integers (<c>Shift</c>), each
/// through the scalar method (<c>*Scalar</c>) and the dispatcher (the <c>Vector128</c> path where it is accelerated).
/// <c>RoundThenShift</c> is the two passes the fused method replaced (rounding the whole buffer to integers in place, then shifting
/// and clipping in 64-bit arithmetic with a column check per sample), kept as the baseline; it copies its input in first, as the
/// rounding works in place, and so does every other case for the same cost. <c>Allocated</c> must read <c>-</c>; the vector paths'
/// bit-exactness is <c>Broadside.Tests.Filters.JpxLevelShiftKernelTests</c>.
/// </summary>
[MemoryDiagnoser]
public class JpxLevelShiftBenchmarks
{
    private const int Samples = 1 << 16;
    private const int Shift8 = 128;
    private const int Max8 = 255;
    private readonly float[] _floats = new float[Samples];
    private readonly int[] _integers = new int[Samples];
    private readonly int[] _buffer = new int[Samples];
    private readonly ushort[] _target = new ushort[Samples];

    [GlobalSetup]
    public void Setup()
    {
#pragma warning disable CA5394 // benchmark input, not security
        var random = new Random(503);
        for (int i = 0; i < Samples; i++)
        {
            _floats[i] = (float)((random.NextDouble() * 280) - 140);
            _integers[i] = random.Next(-140, 140);
        }
#pragma warning restore CA5394
    }

    [Benchmark(Baseline = true)]
    public ushort RoundThenShift()
    {
        MemoryMarshal.Cast<float, int>(_floats).CopyTo(_buffer);
        Span<float> values = MemoryMarshal.Cast<int, float>(_buffer.AsSpan());
        for (int i = 0; i < _buffer.Length; i++)
        {
            float value = values[i];
            _buffer[i] = value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : float.IsNaN(value) ? 0 : (int)MathF.Round(value);
        }

        for (int x = 0; x < _buffer.Length; x++)
        {
            if ((uint)x >= (uint)_target.Length)
            {
                continue;
            }

            _target[x] = (ushort)Math.Clamp(_buffer[x] + (long)Shift8, 0, Max8);
        }

        return _target[^1];
    }

    [Benchmark]
    public ushort RoundShiftScalar()
    {
        MemoryMarshal.Cast<float, int>(_floats).CopyTo(_buffer);
        JpxLevelShift.RoundShiftScalar(MemoryMarshal.Cast<int, float>(_buffer.AsSpan()), _target, Shift8, Max8);
        return _target[^1];
    }

    [Benchmark]
    public ushort RoundShift()
    {
        MemoryMarshal.Cast<float, int>(_floats).CopyTo(_buffer);
        JpxLevelShift.RoundShift(MemoryMarshal.Cast<int, float>(_buffer.AsSpan()), _target, Shift8, Max8);
        return _target[^1];
    }

    [Benchmark]
    public ushort ShiftScalar()
    {
        _integers.AsSpan().CopyTo(_buffer);
        JpxLevelShift.ShiftScalar(_buffer, _target, Shift8, Max8);
        return _target[^1];
    }

    [Benchmark]
    public ushort Shift()
    {
        _integers.AsSpan().CopyTo(_buffer);
        JpxLevelShift.Shift(_buffer, _target, Shift8, Max8);
        return _target[^1];
    }
}
