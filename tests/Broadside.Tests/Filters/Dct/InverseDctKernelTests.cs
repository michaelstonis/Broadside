using System.Runtime.InteropServices;
using Broadside.Filters.Dct;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// The <c>Vector128</c> path of the 8 x 8 inverse DCT (issue #474) against the scalar one it replaces, the dispatcher included:
/// every single-coefficient block of every 12-bit value, random sparse blocks shaped like real scans, and random blocks over the whole
/// 16-bit coefficient and quantization range, where the 32-bit arithmetic wraps and libjpeg's shortcuts differ from the full
/// arithmetic. In the <see cref="SimdKernelAssert.Category"/> category, which CI reruns with hardware intrinsics off.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class InverseDctKernelTests
{
    private delegate void Idct(ReadOnlySpan<short> coefficients, ReadOnlySpan<int> quantization, Span<byte> output, int stride);

    /// <summary>ITU-T T.81 Table K.1, the luminance quantization table, in natural order.</summary>
    private static readonly int[] Luminance =
    [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    ];

    [Fact]
    public void Every_single_coefficient_block_matches_the_scalar_transform()
    {
        // Each of the 64 positions alone at every value from -2048 to 2047, and with a DC term beside it.
        short[] blocks = new short[64 * 2 * 4096 * 64];
        int n = 0;
        for (int position = 0; position < 64; position++)
        {
            for (int value = -2048; value < 2048; value++)
            {
                blocks[(n * 64) + position] = (short)value;
                n++;
                blocks[(n * 64) + position] = (short)value;
                blocks[n * 64] += (short)(value % 7 * 37);
                n++;
            }
        }

        Check(blocks, Luminance);
        Check(blocks, [.. Enumerable.Repeat(1, 64)]);
    }

    [Fact]
    public void Sparse_blocks_match_the_scalar_transform()
    {
        // Low frequencies more often non-zero, as after quantization; whole columns and rows of zeros take libjpeg's shortcuts.
        byte[] random = SimdKernelAssert.RandomBytes(3 * 64 * 50_000, seed: 474);
        short[] blocks = new short[64 * 50_000];
        for (int i = 0; i < blocks.Length; i++)
        {
            int position = i % 64;
            int frequency = (position >> 3) + (position & 7);
            bool nonZero = random[3 * i] < 256 >> Math.Min(frequency, 8);
            blocks[i] = nonZero ? (short)((sbyte)random[(3 * i) + 1] * (1 + (random[(3 * i) + 2] & 7))) : (short)0;
        }

        Check(blocks, Luminance);
        Check(blocks, [.. Luminance.Select(q => q * 3)]);
    }

    [Fact]
    public void Blocks_over_the_whole_range_match_the_scalar_transform_where_the_arithmetic_wraps()
    {
        // Corrupt data: any 16-bit coefficient and quantization values up to 65,535. Every third block keeps only its first row and
        // one coefficient of the second, so seven columns take libjpeg's column shortcut with DC terms whose full arithmetic wraps.
        short[] blocks = MemoryMarshal.Cast<byte, short>(SimdKernelAssert.RandomBytes(2 * 64 * 20_000, seed: 4741)).ToArray();
        for (int i = 0; i < blocks.Length; i += 64 * 3)
        {
            Array.Clear(blocks, i + 8, 56);
            blocks[i + 8 + (i / 64 % 8)] = 1;
        }

        int[] quantization = [.. MemoryMarshal.Cast<byte, ushort>(SimdKernelAssert.RandomBytes(128, seed: 4742)).ToArray().Select(q => (int)q)];
        quantization[0] = 65535;
        quantization[9] = 0;
        Check(blocks, quantization);
        Check(blocks, [.. Enumerable.Repeat(65535, 64)]);
    }

    [Fact]
    public void A_block_written_into_a_wider_buffer_leaves_the_bytes_between_rows_alone()
    {
        short[] block = MemoryMarshal.Cast<byte, short>(SimdKernelAssert.RandomBytes(128, seed: 7)).ToArray();
        for (int i = 0; i < 64; i++)
        {
            block[i] >>= 6;
        }

        byte[] expected = new byte[(7 * 13) + 8 + 5];
        byte[] actual = new byte[expected.Length];
        Array.Fill(expected, (byte)0xA5);
        Array.Fill(actual, (byte)0xA5);

        InverseDct.TransformScalar(block, Luminance, expected, 13);
        InverseDct.TransformVector(block, Luminance, actual, 13);

        Assert.Equal(expected, actual);
    }

    private static void Check(short[] blocks, int[] quantization)
    {
        SimdKernelAssert.Equal(Blocks(InverseDct.TransformScalar, quantization), Blocks(InverseDct.TransformVector, quantization), blocks, inputsPerItem: 64, outputsPerItem: 64);
        SimdKernelAssert.Equal(Blocks(InverseDct.TransformScalar, quantization), Blocks(InverseDct.Transform, quantization), blocks, inputsPerItem: 64, outputsPerItem: 64);
    }

    /// <summary>Adapts the transform to a row of blocks, each item 64 coefficients in and 64 samples (stride 8) out.</summary>
    private static SpanKernel<short, byte> Blocks(Idct kernel, int[] quantization) => (source, destination) =>
    {
        for (int i = 0; i < source.Length / 64; i++)
        {
            kernel(source.Slice(64 * i, 64), quantization, destination.Slice(64 * i, 64), 8);
        }
    };
}
