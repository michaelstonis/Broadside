using System.Runtime.InteropServices;
using Broadside.Filters.Codecs;
using Broadside.Filters.Jpx;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// The <c>Vector128</c> paths of the JPEG 2000 lifting steps and component transforms (issue #477) against the scalar ones, the
/// dispatchers included, and the restructured inverse wavelet transform (lifting on the sub-bands, then the interleave) against the
/// one it replaced (<see cref="JpxWaveletReference"/>) over every small geometry, both parities, several levels and reduced decodes.
/// In the <see cref="SimdKernelAssert.Category"/> category, which CI reruns with hardware intrinsics off.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class JpxLiftingKernelTests
{
    private delegate void IntStep(Span<int> target, ReadOnlySpan<int> left, ReadOnlySpan<int> right);

    private delegate void FloatStep(Span<float> target, ReadOnlySpan<float> left, ReadOnlySpan<float> right, float factor);

    private delegate void Zipper(ReadOnlySpan<int> even, ReadOnlySpan<int> odd, Span<int> target);

    private delegate void FloatScale(Span<float> target, float factor);

    private delegate void IntTransform(Span<int> c0, Span<int> c1, Span<int> c2);

    private delegate void FloatTransform(Span<float> c0, Span<float> c1, Span<float> c2);

    /// <summary>Table F.4's lifting coefficients and the scaling factors K and 1/K.</summary>
    private static readonly float[] Factors = [-1.586134342059924f, -0.052980118572961f, 0.882911075530934f, 0.443506852043971f, 1.230174104914001f, (float)(1 / 1.230174104914001)];

    [Fact]
    public void The_5_3_steps_match_the_scalar_steps_on_small_sums_and_on_the_whole_32_bit_range()
    {
        int[] items = IntTriples();
        Check(JpxLifting.Lift53LowScalar, JpxLifting.Lift53LowVector, JpxLifting.Lift53Low, items);
        Check(JpxLifting.Lift53HighScalar, JpxLifting.Lift53HighVector, JpxLifting.Lift53High, items);
    }

    [Fact]
    public void The_9_7_steps_and_scaling_match_the_scalar_bits_for_every_coefficient()
    {
        int[] items = FloatTriples();
        foreach (float factor in Factors)
        {
            SimdKernelAssert.Equal(Float(JpxLifting.Lift97Scalar, factor), Float(JpxLifting.Lift97Vector, factor), items, 3);
            SimdKernelAssert.Equal(Float(JpxLifting.Lift97Scalar, factor), Float(JpxLifting.Lift97, factor), items, 3);
            SimdKernelAssert.Equal(Scale(JpxLifting.Scale97Scalar, factor), Scale(JpxLifting.Scale97Vector, factor), items);
            SimdKernelAssert.Equal(Scale(JpxLifting.Scale97Scalar, factor), Scale(JpxLifting.Scale97, factor), items);
        }
    }

    [Fact]
    public void The_interleave_matches_the_scalar_interleave_with_and_without_a_last_even_sample()
    {
        int[] items = MemoryMarshal.Cast<byte, int>(SimdKernelAssert.RandomBytes(8 * 100_000, seed: 477)).ToArray();
        foreach (bool longer in new[] { false, true })
        {
            SimdKernelAssert.Equal(Zip(JpxLifting.InterleaveScalar, longer), Zip(JpxLifting.InterleaveVector, longer), items, 2, 2);
            SimdKernelAssert.Equal(Zip(JpxLifting.InterleaveScalar, longer), Zip(JpxLifting.Interleave, longer), items, 2, 2);
        }
    }

    [Fact]
    public void The_inverse_RCT_matches_the_scalar_transform()
    {
        int[] items = IntTriples();
        SimdKernelAssert.Equal(Transform(JpxLifting.InverseRctScalar), Transform(JpxLifting.InverseRctVector), items, 3, 3);
        SimdKernelAssert.Equal(Transform(JpxLifting.InverseRctScalar), Transform(JpxLifting.InverseRct), items, 3, 3);
    }

    [Fact]
    public void The_inverse_ICT_matches_the_scalar_bits()
    {
        int[] items = FloatTriples();
        SimdKernelAssert.Equal(Transform(JpxLifting.InverseIctScalar), Transform(JpxLifting.InverseIctVector), items, 3, 3);
        SimdKernelAssert.Equal(Transform(JpxLifting.InverseIctScalar), Transform(JpxLifting.InverseIct), items, 3, 3);
    }

    [Fact]
    public void The_inverse_5_3_transform_matches_the_interleave_first_transform_on_every_small_geometry()
    {
        var random = new Random(477);
        foreach ((JpxTileComponent component, int top) in Geometries())
        {
            int area = component.Width * component.Height;
            int[] expected = new int[area];
            for (int i = 0; i < area; i++)
            {
#pragma warning disable CA5394 // test input, not security
                expected[i] = (i & 7) == 0 ? random.Next() - (int.MaxValue / 2) : random.Next(-4096, 4096);
#pragma warning restore CA5394
            }

            int[] actual = (int[])expected.Clone();
            JpxWaveletReference.Inverse53(component, expected, new CodecReporter(), top);
            JpxWavelet.Inverse53(component, actual, new CodecReporter(), top);
            Assert.True(expected.AsSpan().SequenceEqual(actual), Describe(component, top));
        }
    }

    [Fact]
    public void The_inverse_9_7_transform_matches_the_interleave_first_transform_bit_for_bit_on_every_small_geometry()
    {
        var random = new Random(477);
        foreach ((JpxTileComponent component, int top) in Geometries())
        {
            int area = component.Width * component.Height;
            float[] expected = new float[area];
            for (int i = 0; i < area; i++)
            {
#pragma warning disable CA5394 // test input, not security
                expected[i] = (float)((random.NextDouble() - 0.5) * 512);
#pragma warning restore CA5394
            }

            float[] actual = (float[])expected.Clone();
            JpxWaveletReference.Inverse97(component, expected, new CodecReporter(), top);
            JpxWavelet.Inverse97(component, actual, new CodecReporter(), top);
            Assert.True(
                MemoryMarshal.AsBytes(expected.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(actual.AsSpan())),
                Describe(component, top));
        }
    }

    private static void Check(IntStep scalar, IntStep vector, IntStep dispatcher, int[] items)
    {
        SimdKernelAssert.Equal(Int(scalar), Int(vector), items, 3);
        SimdKernelAssert.Equal(Int(scalar), Int(dispatcher), items, 3);
    }

    /// <summary>
    /// (target, left, right) triples: every pair of neighbours from -128 to 127 beside a sweep of targets, the 32-bit extremes
    /// against each other, and a million random 32-bit triples (whose sums wrap).
    /// </summary>
    private static int[] IntTriples()
    {
        int[] extremes = [int.MinValue, int.MinValue + 1, -65536, -3, -2, -1, 0, 1, 2, 3, 65535, int.MaxValue - 1, int.MaxValue];
        byte[] random = SimdKernelAssert.RandomBytes(12 * 1_000_000, seed: 477);
        var items = new List<int>();
        for (int left = -128; left < 128; left++)
        {
            for (int right = -128; right < 128; right++)
            {
                items.AddRange([((left * 31) + right) * 17, left, right]);
            }
        }

        foreach (int target in extremes)
        {
            foreach (int left in extremes)
            {
                foreach (int right in extremes)
                {
                    items.AddRange([target, left, right]);
                }
            }
        }

        items.AddRange(MemoryMarshal.Cast<byte, int>(random).ToArray());
        return [.. items];
    }

    /// <summary>
    /// (target, left, right) triples of single-precision bit patterns: coefficients as the dequantization gives them (integers and
    /// fractions up to 2^16), any 32-bit pattern (infinities, NaNs and denormals among them), and the extremes against each other.
    /// </summary>
    private static int[] FloatTriples()
    {
        byte[] random = SimdKernelAssert.RandomBytes(8 * 1_000_000, seed: 477);
        float[] extremes = [0f, -0f, 1f, -1f, float.Epsilon, -float.Epsilon, float.MaxValue, float.MinValue, float.PositiveInfinity, float.NegativeInfinity, 1e-30f, 65535.5f];
        var items = new List<int>();
        var values = new Random(477);
        for (int i = 0; i < 1_000_000; i++)
        {
#pragma warning disable CA5394 // test input, not security
            items.Add(BitConverter.SingleToInt32Bits((float)((values.NextDouble() - 0.5) * (1 << (i % 17)))));
#pragma warning restore CA5394
        }

        items.AddRange(MemoryMarshal.Cast<byte, int>(random).ToArray());
        foreach (float target in extremes)
        {
            foreach (float left in extremes)
            {
                foreach (float right in extremes)
                {
                    items.AddRange([BitConverter.SingleToInt32Bits(target), BitConverter.SingleToInt32Bits(left), BitConverter.SingleToInt32Bits(right)]);
                }
            }
        }

        return [.. items];
    }

    private static SpanKernel<int, int> Int(IntStep step) => (source, destination) =>
    {
        (int[] target, int[] left, int[] right) = Split(source, destination.Length);
        step(target, left, right);
        target.CopyTo(destination);
    };

    private static SpanKernel<int, int> Float(FloatStep step, float factor) => (source, destination) =>
    {
        (int[] target, int[] left, int[] right) = Split(source, destination.Length);
        step(Floats(target), Floats(left), Floats(right), factor);
        Canonical(target).CopyTo(destination);
    };

    private static SpanKernel<int, int> Scale(FloatScale scale, float factor) => (source, destination) =>
    {
        int[] target = source.ToArray();
        scale(Floats(target), factor);
        Canonical(target).CopyTo(destination);
    };

    /// <summary>
    /// Pairs (even, odd) interleaved; <paramref name="longer"/> drops the last odd sample, so the even run is one longer and the
    /// output one shorter (its last element left 0).
    /// </summary>
    private static SpanKernel<int, int> Zip(Zipper zip, bool longer) => (source, destination) =>
    {
        int n = destination.Length / 2;
        int[] even = new int[n];
        int[] odd = new int[n];
        for (int i = 0; i < n; i++)
        {
            even[i] = source[2 * i];
            odd[i] = source[(2 * i) + 1];
        }

        int odds = longer && n > 0 ? n - 1 : n;
        destination.Clear();
        zip(even, odd.AsSpan(0, odds), destination[..(n + odds)]);
    };

    private static SpanKernel<int, int> Transform(IntTransform transform) => (source, destination) =>
    {
        (int[] c0, int[] c1, int[] c2) = Split(source, source.Length / 3);
        transform(c0, c1, c2);
        Join(c0, c1, c2, destination);
    };

    private static SpanKernel<int, int> Transform(FloatTransform transform) => (source, destination) =>
    {
        (int[] c0, int[] c1, int[] c2) = Split(source, source.Length / 3);
        transform(Floats(c0), Floats(c1), Floats(c2));
        Join(Canonical(c0), Canonical(c1), Canonical(c2), destination);
    };

    private static Span<float> Floats(int[] bits) => MemoryMarshal.Cast<int, float>(bits.AsSpan());

    /// <summary>
    /// The bits with every NaN made one NaN. Which input's payload a sum of two NaNs carries depends on the operand order the
    /// compiler picks for a commutative add (the JIT's scalar and vector code differ on Arm64), and IEEE 754 leaves it open;
    /// the decoder never sees it, rounding every NaN to the integer 0 (<c>JpxDecoder.RoundToIntegers</c>). Every other bit is compared.
    /// </summary>
    private static int[] Canonical(int[] bits)
    {
        for (int i = 0; i < bits.Length; i++)
        {
            if (float.IsNaN(BitConverter.Int32BitsToSingle(bits[i])))
            {
                bits[i] = BitConverter.SingleToInt32Bits(float.NaN);
            }
        }

        return bits;
    }

    private static (int[] A, int[] B, int[] C) Split(ReadOnlySpan<int> source, int count)
    {
        int[] a = new int[count];
        int[] b = new int[count];
        int[] c = new int[count];
        for (int i = 0; i < count; i++)
        {
            a[i] = source[3 * i];
            b[i] = source[(3 * i) + 1];
            c[i] = source[(3 * i) + 2];
        }

        return (a, b, c);
    }

    private static void Join(int[] a, int[] b, int[] c, Span<int> destination)
    {
        for (int i = 0; i < a.Length; i++)
        {
            destination[3 * i] = a[i];
            destination[(3 * i) + 1] = b[i];
            destination[(3 * i) + 2] = c[i];
        }
    }

    /// <summary>
    /// Tile-components of every size up to 21 x 21 at both parities of each origin coordinate with one to three levels, wider and
    /// taller ones that span several column strips, each reconstructed to its last resolution and, with two or more levels, to one below.
    /// </summary>
    private static IEnumerable<(JpxTileComponent Component, int Top)> Geometries()
    {
        List<(int Width, int Height)> sizes = [];
        for (int width = 1; width <= 21; width++)
        {
            for (int height = 1; height <= 21; height++)
            {
                sizes.Add((width, height));
            }
        }

        sizes.AddRange([(63, 5), (64, 9), (65, 3), (130, 17), (257, 33), (7, 300), (200, 150)]);
        foreach ((int width, int height) in sizes)
        {
            foreach ((long x0, long y0) in new[] { (0L, 0L), (1L, 0L), (0L, 1L), (3L, 5L), (6L, 2L) })
            {
                for (int levels = 1; levels <= 3; levels++)
                {
                    JpxTileComponent component = Component(x0, y0, width, height, levels);
                    yield return (component, levels);
                    if (levels > 1)
                    {
                        yield return (component, levels - 1);
                    }
                }
            }
        }
    }

    /// <summary>A tile-component's resolutions by B-14 and B-15: each level halves the origin and the far edge, rounding up.</summary>
    private static JpxTileComponent Component(long x0, long y0, int width, int height, int levels)
    {
        var resolutions = new JpxResolution[levels + 1];
        for (int r = 0; r <= levels; r++)
        {
            int shift = levels - r;
            long rx0 = Ceiling(x0, shift);
            long ry0 = Ceiling(y0, shift);
            resolutions[r] = new JpxResolution
            {
                X0 = rx0,
                Y0 = ry0,
                Width = (int)(Ceiling(x0 + width, shift) - rx0),
                Height = (int)(Ceiling(y0 + height, shift) - ry0),
                NextLayer = [],
                Bands = [],
                PrecinctBands = [],
            };
        }

        return new JpxTileComponent
        {
            X0 = x0,
            Y0 = y0,
            Width = width,
            Height = height,
            Style = new JpxComponentStyle { Levels = levels, Precincts = [] },
            Resolutions = resolutions,
        };

        static long Ceiling(long value, int shift) => (value + (1L << shift) - 1) >> shift;
    }

    private static string Describe(JpxTileComponent component, int top) =>
        $"{component.Width} x {component.Height} at ({component.X0}, {component.Y0}), {component.Resolutions.Length - 1} levels, top {top}";
}
