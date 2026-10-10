using System.Runtime.CompilerServices;

namespace Broadside.Filters.Dct;

/// <summary>
/// Dequantization, the 8 x 8 inverse DCT, level shift and clamping for 8-bit and 12-bit samples: a port of libjpeg's
/// <c>jpeg_idct_islow</c> (the Loeffler-Ligtenberg-Moschytz integer algorithm), so the samples equal libjpeg-turbo's
/// <c>-dct int</c> output exactly.
/// </summary>
/// <remarks>
/// ITU-T T.81 §A.3.3 (the IDCT, informative), §A.3.4 (dequantization R = S x Q, normative), §A.3.1 and §F.2.1.5 (level shift by
/// 2^(P-1) and clamp). Ported from libjpeg-turbo <c>src/jidctint.c</c> (commit 43ea8097), which is the Independent JPEG Group's
/// software (see THIRD-PARTY-NOTICES.txt). Changes: C# syntax, coefficients in natural order with the quantization table as
/// <see cref="int"/>s, 32-bit arithmetic for 8-bit samples (libjpeg's <c>JLONG</c> on 32-bit platforms; it wraps the same way on
/// corrupt input) and 64-bit arithmetic for 12-bit samples, whose products need it (libjpeg's 12-bit build, PASS1_BITS 1).
/// </remarks>
internal static class InverseDct
{
    private const int ConstBits = 13;
    private const int Pass1Bits = 2;
    private const int Pass1Bits12 = 1;
    private const int Fix0298631336 = 2446;
    private const int Fix0390180644 = 3196;
    private const int Fix0541196100 = 4433;
    private const int Fix0765366865 = 6270;
    private const int Fix0899976223 = 7373;
    private const int Fix1175875602 = 9633;
    private const int Fix1501321110 = 12299;
    private const int Fix1847759065 = 15137;
    private const int Fix1961570560 = 16069;
    private const int Fix2053119869 = 16819;
    private const int Fix2562915447 = 20995;
    private const int Fix3072711026 = 25172;

    /// <summary>
    /// libjpeg's post-IDCT range-limit table indexed by the descaled value masked to 10 bits: [0, 127] map to 128-255, [128, 511]
    /// clamp to 255, [512, 895] (negative values) clamp to 0, [896, 1023] map to 0-127.
    /// </summary>
    private static readonly byte[] RangeLimit = CreateRangeLimit();

    /// <summary>The 12-bit range-limit table, indexed by the descaled value masked to 14 bits (the same layout scaled by 16).</summary>
    private static readonly ushort[] RangeLimit12 = CreateRangeLimit12();

    /// <summary>Transforms one block of quantized coefficients into 8 x 8 samples.</summary>
    /// <param name="coefficients">The 64 quantized coefficients in natural order.</param>
    /// <param name="quantization">The 64 quantization values in natural order.</param>
    /// <param name="output">Where the first sample row starts.</param>
    /// <param name="stride">The distance between sample rows in <paramref name="output"/>.</param>
    [SkipLocalsInit]
    public static void Transform(ReadOnlySpan<short> coefficients, ReadOnlySpan<int> quantization, Span<byte> output, int stride)
    {
        Span<int> workspace = stackalloc int[64];
        coefficients = coefficients[..64];
        quantization = quantization[..64];
        byte[] range = RangeLimit;

        // Pass 1: columns from the input into the workspace, scaled up by sqrt(8) and 2^Pass1Bits.
        for (int column = 0; column < 8; column++)
        {
            if (coefficients[8 + column] == 0 && coefficients[16 + column] == 0 && coefficients[24 + column] == 0
                && coefficients[32 + column] == 0 && coefficients[40 + column] == 0 && coefficients[48 + column] == 0
                && coefficients[56 + column] == 0)
            {
                int dc = coefficients[column] * quantization[column] << Pass1Bits;
                for (int row = 0; row < 64; row += 8)
                {
                    workspace[row + column] = dc;
                }

                continue;
            }

            int z2 = coefficients[16 + column] * quantization[16 + column];
            int z3 = coefficients[48 + column] * quantization[48 + column];
            int z1 = (z2 + z3) * Fix0541196100;
            int tmp2 = z1 + (z3 * -Fix1847759065);
            int tmp3 = z1 + (z2 * Fix0765366865);

            z2 = coefficients[column] * quantization[column];
            z3 = coefficients[32 + column] * quantization[32 + column];
            int tmp0 = (z2 + z3) << ConstBits;
            int tmp1 = (z2 - z3) << ConstBits;

            int tmp10 = tmp0 + tmp3;
            int tmp13 = tmp0 - tmp3;
            int tmp11 = tmp1 + tmp2;
            int tmp12 = tmp1 - tmp2;

            tmp0 = coefficients[56 + column] * quantization[56 + column];
            tmp1 = coefficients[40 + column] * quantization[40 + column];
            tmp2 = coefficients[24 + column] * quantization[24 + column];
            tmp3 = coefficients[8 + column] * quantization[8 + column];
            Odd(ref tmp0, ref tmp1, ref tmp2, ref tmp3);

            const int shift = ConstBits - Pass1Bits;
            const int round = 1 << (shift - 1);
            workspace[column] = (tmp10 + tmp3 + round) >> shift;
            workspace[56 + column] = (tmp10 - tmp3 + round) >> shift;
            workspace[8 + column] = (tmp11 + tmp2 + round) >> shift;
            workspace[48 + column] = (tmp11 - tmp2 + round) >> shift;
            workspace[16 + column] = (tmp12 + tmp1 + round) >> shift;
            workspace[40 + column] = (tmp12 - tmp1 + round) >> shift;
            workspace[24 + column] = (tmp13 + tmp0 + round) >> shift;
            workspace[32 + column] = (tmp13 - tmp0 + round) >> shift;
        }

        // Pass 2: rows from the workspace into the output, descaled by 8 and 2^Pass1Bits, level-shifted and range-limited.
        for (int row = 0; row < 8; row++)
        {
            Span<int> w = workspace.Slice(row * 8, 8);
            Span<byte> o = output.Slice(row * stride, 8);
            if (w[1] == 0 && w[2] == 0 && w[3] == 0 && w[4] == 0 && w[5] == 0 && w[6] == 0 && w[7] == 0)
            {
                o.Fill(range[((w[0] + (1 << (Pass1Bits + 2))) >> (Pass1Bits + 3)) & 1023]);
                continue;
            }

            int z2 = w[2];
            int z3 = w[6];
            int z1 = (z2 + z3) * Fix0541196100;
            int tmp2 = z1 + (z3 * -Fix1847759065);
            int tmp3 = z1 + (z2 * Fix0765366865);

            int tmp0 = (w[0] + w[4]) << ConstBits;
            int tmp1 = (w[0] - w[4]) << ConstBits;

            int tmp10 = tmp0 + tmp3;
            int tmp13 = tmp0 - tmp3;
            int tmp11 = tmp1 + tmp2;
            int tmp12 = tmp1 - tmp2;

            tmp0 = w[7];
            tmp1 = w[5];
            tmp2 = w[3];
            tmp3 = w[1];
            Odd(ref tmp0, ref tmp1, ref tmp2, ref tmp3);

            const int shift = ConstBits + Pass1Bits + 3;
            const int round = 1 << (shift - 1);
            o[0] = range[((tmp10 + tmp3 + round) >> shift) & 1023];
            o[7] = range[((tmp10 - tmp3 + round) >> shift) & 1023];
            o[1] = range[((tmp11 + tmp2 + round) >> shift) & 1023];
            o[6] = range[((tmp11 - tmp2 + round) >> shift) & 1023];
            o[2] = range[((tmp12 + tmp1 + round) >> shift) & 1023];
            o[5] = range[((tmp12 - tmp1 + round) >> shift) & 1023];
            o[3] = range[((tmp13 + tmp0 + round) >> shift) & 1023];
            o[4] = range[((tmp13 - tmp0 + round) >> shift) & 1023];
        }
    }

    /// <summary>Transforms one block of quantized coefficients of a 12-bit frame into 8 x 8 samples from 0 to 4095.</summary>
    /// <param name="coefficients">The 64 quantized coefficients in natural order.</param>
    /// <param name="quantization">The 64 quantization values in natural order.</param>
    /// <param name="output">Where the first sample row starts.</param>
    /// <param name="stride">The distance between sample rows in <paramref name="output"/>.</param>
    /// <remarks>ITU-T T.81 §A.3.1 and §F.2.1.5: level shift 2048, clamp to 0..4095.</remarks>
    [SkipLocalsInit]
    public static void Transform12(ReadOnlySpan<short> coefficients, ReadOnlySpan<int> quantization, Span<ushort> output, int stride)
    {
        Span<int> workspace = stackalloc int[64];
        coefficients = coefficients[..64];
        quantization = quantization[..64];
        ushort[] range = RangeLimit12;
        const int mask = 16383;

        for (int column = 0; column < 8; column++)
        {
            if (coefficients[8 + column] == 0 && coefficients[16 + column] == 0 && coefficients[24 + column] == 0
                && coefficients[32 + column] == 0 && coefficients[40 + column] == 0 && coefficients[48 + column] == 0
                && coefficients[56 + column] == 0)
            {
                int dc = (int)((long)coefficients[column] * quantization[column] << Pass1Bits12);
                for (int row = 0; row < 64; row += 8)
                {
                    workspace[row + column] = dc;
                }

                continue;
            }

            long z2 = (long)coefficients[16 + column] * quantization[16 + column];
            long z3 = (long)coefficients[48 + column] * quantization[48 + column];
            long z1 = (z2 + z3) * Fix0541196100;
            long tmp2 = z1 + (z3 * -Fix1847759065);
            long tmp3 = z1 + (z2 * Fix0765366865);

            z2 = (long)coefficients[column] * quantization[column];
            z3 = (long)coefficients[32 + column] * quantization[32 + column];
            long tmp0 = (z2 + z3) << ConstBits;
            long tmp1 = (z2 - z3) << ConstBits;

            long tmp10 = tmp0 + tmp3;
            long tmp13 = tmp0 - tmp3;
            long tmp11 = tmp1 + tmp2;
            long tmp12 = tmp1 - tmp2;

            tmp0 = (long)coefficients[56 + column] * quantization[56 + column];
            tmp1 = (long)coefficients[40 + column] * quantization[40 + column];
            tmp2 = (long)coefficients[24 + column] * quantization[24 + column];
            tmp3 = (long)coefficients[8 + column] * quantization[8 + column];
            Odd(ref tmp0, ref tmp1, ref tmp2, ref tmp3);

            const int shift = ConstBits - Pass1Bits12;
            const long round = 1L << (shift - 1);
            workspace[column] = (int)((tmp10 + tmp3 + round) >> shift);
            workspace[56 + column] = (int)((tmp10 - tmp3 + round) >> shift);
            workspace[8 + column] = (int)((tmp11 + tmp2 + round) >> shift);
            workspace[48 + column] = (int)((tmp11 - tmp2 + round) >> shift);
            workspace[16 + column] = (int)((tmp12 + tmp1 + round) >> shift);
            workspace[40 + column] = (int)((tmp12 - tmp1 + round) >> shift);
            workspace[24 + column] = (int)((tmp13 + tmp0 + round) >> shift);
            workspace[32 + column] = (int)((tmp13 - tmp0 + round) >> shift);
        }

        for (int row = 0; row < 8; row++)
        {
            Span<int> w = workspace.Slice(row * 8, 8);
            Span<ushort> o = output.Slice(row * stride, 8);
            if (w[1] == 0 && w[2] == 0 && w[3] == 0 && w[4] == 0 && w[5] == 0 && w[6] == 0 && w[7] == 0)
            {
                o.Fill(range[(int)(((long)w[0] + (1 << (Pass1Bits12 + 2))) >> (Pass1Bits12 + 3)) & mask]);
                continue;
            }

            long z2 = w[2];
            long z3 = w[6];
            long z1 = (z2 + z3) * Fix0541196100;
            long tmp2 = z1 + (z3 * -Fix1847759065);
            long tmp3 = z1 + (z2 * Fix0765366865);

            long tmp0 = ((long)w[0] + w[4]) << ConstBits;
            long tmp1 = ((long)w[0] - w[4]) << ConstBits;

            long tmp10 = tmp0 + tmp3;
            long tmp13 = tmp0 - tmp3;
            long tmp11 = tmp1 + tmp2;
            long tmp12 = tmp1 - tmp2;

            tmp0 = w[7];
            tmp1 = w[5];
            tmp2 = w[3];
            tmp3 = w[1];
            Odd(ref tmp0, ref tmp1, ref tmp2, ref tmp3);

            const int shift = ConstBits + Pass1Bits12 + 3;
            const long round = 1L << (shift - 1);
            o[0] = range[(int)((tmp10 + tmp3 + round) >> shift) & mask];
            o[7] = range[(int)((tmp10 - tmp3 + round) >> shift) & mask];
            o[1] = range[(int)((tmp11 + tmp2 + round) >> shift) & mask];
            o[6] = range[(int)((tmp11 - tmp2 + round) >> shift) & mask];
            o[2] = range[(int)((tmp12 + tmp1 + round) >> shift) & mask];
            o[5] = range[(int)((tmp12 - tmp1 + round) >> shift) & mask];
            o[3] = range[(int)((tmp13 + tmp0 + round) >> shift) & mask];
            o[4] = range[(int)((tmp13 - tmp0 + round) >> shift) & mask];
        }
    }

    /// <summary>The odd part of <see cref="Transform12"/>, in 64 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Odd(ref long tmp0, ref long tmp1, ref long tmp2, ref long tmp3)
    {
        long z1 = tmp0 + tmp3;
        long z2 = tmp1 + tmp2;
        long z3 = tmp0 + tmp2;
        long z4 = tmp1 + tmp3;
        long z5 = (z3 + z4) * Fix1175875602;

        tmp0 *= Fix0298631336;
        tmp1 *= Fix2053119869;
        tmp2 *= Fix3072711026;
        tmp3 *= Fix1501321110;
        z1 *= -Fix0899976223;
        z2 *= -Fix2562915447;
        z3 = (z3 * -Fix1961570560) + z5;
        z4 = (z4 * -Fix0390180644) + z5;

        tmp0 += z1 + z3;
        tmp1 += z2 + z4;
        tmp2 += z2 + z3;
        tmp3 += z1 + z4;
    }

    /// <summary>The odd part (figure 8 of the LLM paper): inputs are the coefficients 7, 5, 3, 1; outputs the four odd terms.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Odd(ref int tmp0, ref int tmp1, ref int tmp2, ref int tmp3)
    {
        int z1 = tmp0 + tmp3;
        int z2 = tmp1 + tmp2;
        int z3 = tmp0 + tmp2;
        int z4 = tmp1 + tmp3;
        int z5 = (z3 + z4) * Fix1175875602;

        tmp0 *= Fix0298631336;
        tmp1 *= Fix2053119869;
        tmp2 *= Fix3072711026;
        tmp3 *= Fix1501321110;
        z1 *= -Fix0899976223;
        z2 *= -Fix2562915447;
        z3 = (z3 * -Fix1961570560) + z5;
        z4 = (z4 * -Fix0390180644) + z5;

        tmp0 += z1 + z3;
        tmp1 += z2 + z4;
        tmp2 += z2 + z3;
        tmp3 += z1 + z4;
    }

    private static byte[] CreateRangeLimit()
    {
        byte[] table = new byte[1024];
        for (int x = 0; x < 1024; x++)
        {
            table[x] = x switch
            {
                < 128 => (byte)(x + 128),
                < 512 => 255,
                < 896 => 0,
                _ => (byte)(x - 896),
            };
        }

        return table;
    }

    private static ushort[] CreateRangeLimit12()
    {
        ushort[] table = new ushort[16384];
        for (int x = 0; x < table.Length; x++)
        {
            table[x] = x switch
            {
                < 2048 => (ushort)(x + 2048),
                < 8192 => 4095,
                < 14336 => 0,
                _ => (ushort)(x - 14336),
            };
        }

        return table;
    }
}
