using System.Runtime.InteropServices;
using Broadside.Filters.Jpx;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// The <c>Vector128</c> paths of the JPEG 2000 DC level shift (issue #503) against the scalar ones, the dispatchers included, and the
/// scalar ones against the code they replaced: the 9/7 values rounded to saturated integers first (ties to even, NaN to 0), then
/// shifted and clipped in 64-bit arithmetic. Every depth from 1 to 16, over the special values (NaNs of several payloads, the
/// infinities, signed zeros, denormals, the 32-bit and float extremes, every half-integer around each clipping bound and its
/// neighbors one unit in the last place away) and millions of random floats and integers. In the <see cref="SimdKernelAssert.Category"/>
/// category, which CI reruns with hardware intrinsics off.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class JpxLevelShiftKernelTests
{
    private delegate void FloatRow(ReadOnlySpan<float> source, Span<ushort> target, int shift, int max);

    private delegate void IntRow(ReadOnlySpan<int> source, Span<ushort> target, int shift, int max);

    [Fact]
    public void The_9_7_rounding_and_shift_match_the_scalar_method_and_the_round_then_shift_code_at_every_depth()
    {
        float[] random = Random(4_000_000);
        for (int depth = 1; depth <= 16; depth++)
        {
            (int shift, int max) = Range(depth);
            float[] special = Special(shift, max);
            foreach (float[] items in new[] { special, depth is 1 or 8 or 12 or 16 ? random : random.AsSpan(0, 250_000).ToArray() })
            {
                SimdKernelAssert.Equal(Float(JpxLevelShift.RoundShiftScalar, shift, max), Float(JpxLevelShift.RoundShiftVector, shift, max), items);
                SimdKernelAssert.Equal(Float(JpxLevelShift.RoundShiftScalar, shift, max), Float(JpxLevelShift.RoundShift, shift, max), items);
                SimdKernelAssert.Equal(Float(RoundThenShift, shift, max), Float(JpxLevelShift.RoundShiftScalar, shift, max), items);
            }
        }
    }

    [Fact]
    public void The_5_3_shift_matches_the_scalar_method_and_the_64_bit_code_at_every_depth()
    {
        int[] items = Integers();
        for (int depth = 1; depth <= 16; depth++)
        {
            (int shift, int max) = Range(depth);
            SimdKernelAssert.Equal(Int(JpxLevelShift.ShiftScalar, shift, max), Int(JpxLevelShift.ShiftVector, shift, max), items);
            SimdKernelAssert.Equal(Int(JpxLevelShift.ShiftScalar, shift, max), Int(JpxLevelShift.Shift, shift, max), items);
            SimdKernelAssert.Equal(Int(ShiftLong, shift, max), Int(JpxLevelShift.ShiftScalar, shift, max), items);
        }
    }

    [Fact]
    public void Rounding_to_integers_saturates_and_maps_NaN_to_zero()
    {
        int[] values = [.. new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 2.5f, -2.5f, 3.5f, 0.49999997f, -0f, 2147483648f, -2147483648f, 1e20f }.Select(BitConverter.SingleToInt32Bits)];
        JpxLevelShift.RoundToIntegers(values);
        Assert.Equal([0, int.MaxValue, int.MinValue, 2, -2, 4, 0, 0, int.MaxValue, int.MinValue, int.MaxValue], values);
    }

    /// <summary>The code the fused method replaced: <c>JpxDecoder.RoundToIntegers</c>, then <c>JpxPlane.Write</c>'s shift and clip.</summary>
    private static void RoundThenShift(ReadOnlySpan<float> source, Span<ushort> target, int shift, int max)
    {
        for (int i = 0; i < source.Length; i++)
        {
            float value = source[i];
            int rounded = value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : float.IsNaN(value) ? 0 : (int)MathF.Round(value);
            target[i] = (ushort)Math.Clamp(rounded + (long)shift, 0, max);
        }
    }

    /// <summary>The 5/3 shift and clip <c>JpxPlane.Write</c> did, in 64-bit arithmetic.</summary>
    private static void ShiftLong(ReadOnlySpan<int> source, Span<ushort> target, int shift, int max)
    {
        for (int i = 0; i < source.Length; i++)
        {
            target[i] = (ushort)Math.Clamp(source[i] + (long)shift, 0, max);
        }
    }

    private static (int Shift, int Max) Range(int depth) => (1 << (depth - 1), (1 << depth) - 1);

    private static SpanKernel<float, ushort> Float(FloatRow row, int shift, int max) => (source, target) => row(source, target, shift, max);

    private static SpanKernel<int, ushort> Int(IntRow row, int shift, int max) => (source, target) => row(source, target, shift, max);

    /// <summary>
    /// The special values, then every half-integer from 3 below the lower clipping bound to 3 above the upper one (all of them up to
    /// 12 bits, a stride of 61 beyond) with its neighbors one unit in the last place away, interleaved so each lands in every lane.
    /// </summary>
    private static float[] Special(int shift, int max)
    {
        List<float> values =
        [
            float.NaN, -float.NaN, BitConverter.Int32BitsToSingle(0x7F800001), BitConverter.Int32BitsToSingle(0x7FFFFFFF),
            BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00001)), float.PositiveInfinity, float.NegativeInfinity, 0f, -0f,
            float.Epsilon, -float.Epsilon, 1.17549421e-38f, -1.17549421e-38f, float.MaxValue, float.MinValue, int.MaxValue, int.MinValue,
            2147483520f, -2147483520f, 4194304.5f, 8388607.5f, 8388608f, 16777216f, -8388608.5f, 0.49999997f, -0.49999997f, 0.5f, -0.5f,
            1.5f, -1.5f, 2.5f, -2.5f,
        ];
        int stride = max < 4096 ? 1 : 61;
        for (int k = -shift - 3; k <= max - shift + 3; k += stride)
        {
            foreach (float value in new[] { k, k + 0.5f, -(k + 0.5f) })
            {
                values.Add(value);
                values.Add(MathF.BitIncrement(value));
                values.Add(MathF.BitDecrement(value));
            }
        }

        foreach (int bound in new[] { -shift, max - shift })
        {
            for (int k = bound - 3; k <= bound + 3; k++)
            {
                values.Add(k + 0.5f);
                values.Add(MathF.BitIncrement(k + 0.5f));
                values.Add(MathF.BitDecrement(k + 0.5f));
            }
        }

        return [.. values];
    }

    /// <summary>Half arbitrary bit patterns (NaNs and infinities included), half values in the 16-bit sample range with fractions.</summary>
    private static float[] Random(int count)
    {
        float[] values = MemoryMarshal.Cast<byte, float>(SimdKernelAssert.RandomBytes(4 * count, seed: 503)).ToArray();
#pragma warning disable CA5394 // test input, not security
        var random = new Random(503);
        for (int i = 1; i < values.Length; i += 2)
        {
            values[i] = (float)((random.NextDouble() * 70_000) - 35_000);
            if ((i & 7) == 1)
            {
                values[i] = MathF.Floor(values[i]) + 0.5f;
            }
        }
#pragma warning restore CA5394

        return values;
    }

    /// <summary>Every integer from -70,000 to 70,000, the 32-bit extremes and a million random integers.</summary>
    private static int[] Integers()
    {
        List<int> values = [int.MinValue, int.MaxValue, int.MinValue + 1, int.MaxValue - 1, int.MaxValue - 32767, int.MinValue + 32768];
        for (int value = -70_000; value <= 70_000; value++)
        {
            values.Add(value);
        }

        values.AddRange(MemoryMarshal.Cast<byte, int>(SimdKernelAssert.RandomBytes(4 * 1_000_000, seed: 5031)).ToArray());
        return [.. values];
    }
}
