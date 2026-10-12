namespace Broadside.Filters.Dct;

/// <summary>
/// The 8 x 8 forward DCT of level-shifted samples and the quantization of its output: a port of libjpeg's <c>jpeg_fdct_islow</c>
/// (the Loeffler-Ligtenberg-Moschytz integer algorithm) and of libjpeg-turbo's reciprocal quantizer, so the coefficients equal
/// libjpeg-turbo's <c>-dct int</c> encoder exactly, on every platform.
/// </summary>
/// <remarks>
/// ITU-T T.81 §A.3.1 (level shift by 2^(P-1)), §A.3.3 (the FDCT, informative: any implementation meeting the accuracy of Annex A
/// is allowed), §A.3.4 (quantization Sq = round(S / Q), normative). Ported from libjpeg-turbo <c>src/jfdctint.c</c> and
/// <c>src/jcdctmgr.c</c> (<c>compute_reciprocal</c>, <c>quantize</c>), commit 43ea8097, which are the Independent JPEG Group's
/// software (see THIRD-PARTY-NOTICES.txt). The output of <c>jpeg_fdct_islow</c> is scaled up by 8, so the divisor of each
/// coefficient is 8 Q. Changes: C# syntax, 32-bit arithmetic throughout (libjpeg's <c>JLONG</c>); the reciprocal multiply gives
/// round(|S| / 8Q) with the sign restored, the same as libjpeg's division for every input an 8-bit FDCT produces.
/// </remarks>
internal static class ForwardDct
{
    private const int ConstBits = 13;
    private const int Pass1Bits = 2;
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

    /// <summary>The largest AC coefficient magnitude baseline Huffman coding represents (category 10, T.81 Table F.2).</summary>
    private const int MaxAc = 1023;

    /// <summary>
    /// Computes the reciprocal, rounding correction and shift that quantize by <paramref name="quantization"/> x 8 (libjpeg-turbo
    /// <c>compute_reciprocal</c> with 16-bit <c>DCTELEM</c>s).
    /// </summary>
    /// <param name="quantization">The quantization value, 1 to 255.</param>
    /// <returns>The divisor as (reciprocal, correction, shift): round(x / 8Q) = ((x + correction) x reciprocal) &gt;&gt; shift.</returns>
    public static (uint Reciprocal, uint Correction, int Shift) Divisor(int quantization)
    {
        uint divisor = (uint)quantization << 3;
        int b = 31 - System.Numerics.BitOperations.LeadingZeroCount(divisor);
        int r = 16 + b;
        uint fq = (1u << r) / divisor;
        uint fr = (1u << r) % divisor;
        uint c = divisor / 2;
        if (fr == 0)
        {
            fq >>= 1;
            r--;
        }
        else if (fr <= divisor / 2)
        {
            c++;
        }
        else
        {
            fq++;
        }

        return (fq, c, r);
    }

    /// <summary>
    /// Transforms one block of 8 x 8 samples and quantizes it.
    /// </summary>
    /// <param name="samples">Where the first sample row starts.</param>
    /// <param name="stride">The distance between sample rows in <paramref name="samples"/>.</param>
    /// <param name="reciprocals">Per coefficient in natural order: the reciprocal of <see cref="Divisor"/>.</param>
    /// <param name="corrections">Per coefficient in natural order: the correction.</param>
    /// <param name="shifts">Per coefficient in natural order: the shift.</param>
    /// <param name="coefficients">The 64 quantized coefficients, in natural order.</param>
    public static void Transform(
        ReadOnlySpan<byte> samples,
        int stride,
        ReadOnlySpan<uint> reciprocals,
        ReadOnlySpan<uint> corrections,
        ReadOnlySpan<byte> shifts,
        Span<int> coefficients)
    {
        Span<int> data = coefficients[..64];
        reciprocals = reciprocals[..64];
        corrections = corrections[..64];
        shifts = shifts[..64];

        // Pass 1: rows, level-shifted, results scaled up by sqrt(8) and 2^Pass1Bits.
        for (int row = 0; row < 8; row++)
        {
            ReadOnlySpan<byte> line = samples.Slice(row * stride, 8);
            int d0 = line[0] - 128, d1 = line[1] - 128, d2 = line[2] - 128, d3 = line[3] - 128;
            int d4 = line[4] - 128, d5 = line[5] - 128, d6 = line[6] - 128, d7 = line[7] - 128;
            Span<int> output = data.Slice(row * 8, 8);
            Butterfly(d0, d1, d2, d3, d4, d5, d6, d7, out int o0, out int o1, out int o2, out int o3, out int o4, out int o5, out int o6, out int o7, pass1: true);
            output[0] = o0;
            output[1] = o1;
            output[2] = o2;
            output[3] = o3;
            output[4] = o4;
            output[5] = o5;
            output[6] = o6;
            output[7] = o7;
        }

        // Pass 2: columns, removing the Pass1Bits scaling but leaving the factor of 8.
        for (int column = 0; column < 8; column++)
        {
            Butterfly(
                data[column], data[8 + column], data[16 + column], data[24 + column], data[32 + column], data[40 + column], data[48 + column], data[56 + column],
                out int o0, out int o1, out int o2, out int o3, out int o4, out int o5, out int o6, out int o7, pass1: false);
            data[column] = o0;
            data[8 + column] = o1;
            data[16 + column] = o2;
            data[24 + column] = o3;
            data[32 + column] = o4;
            data[40 + column] = o5;
            data[48 + column] = o6;
            data[56 + column] = o7;
        }

        // Quantization: round(|x| / 8Q) by reciprocal multiplication, sign restored. An 8-bit FDCT never reaches the AC limit; the
        // clamp only guarantees the Huffman coder a category it has a code for. DC (at most 1024 in magnitude) is never clamped.
        for (int i = 0; i < 64; i++)
        {
            int value = data[i];
            int sign = value >> 31;
            uint magnitude = (uint)((value ^ sign) - sign);
            int quantized = (int)(((magnitude + corrections[i]) * (ulong)reciprocals[i]) >> shifts[i]);
            if (i > 0)
            {
                quantized = Math.Min(quantized, MaxAc);
            }

            data[i] = (quantized ^ sign) - sign;
        }
    }

    /// <summary>One 8-point pass of <c>jpeg_fdct_islow</c>.</summary>
    private static void Butterfly(
        int d0, int d1, int d2, int d3, int d4, int d5, int d6, int d7,
        out int o0, out int o1, out int o2, out int o3, out int o4, out int o5, out int o6, out int o7, bool pass1)
    {
        int tmp0 = d0 + d7;
        int tmp7 = d0 - d7;
        int tmp1 = d1 + d6;
        int tmp6 = d1 - d6;
        int tmp2 = d2 + d5;
        int tmp5 = d2 - d5;
        int tmp3 = d3 + d4;
        int tmp4 = d3 - d4;

        // Even part (Figure 1 of Loeffler et al.: the rotator is sqrt(2) * c6).
        int tmp10 = tmp0 + tmp3;
        int tmp13 = tmp0 - tmp3;
        int tmp11 = tmp1 + tmp2;
        int tmp12 = tmp1 - tmp2;
        int shift = pass1 ? ConstBits - Pass1Bits : ConstBits + Pass1Bits;
        if (pass1)
        {
            o0 = (tmp10 + tmp11) << Pass1Bits;
            o4 = (tmp10 - tmp11) << Pass1Bits;
        }
        else
        {
            o0 = Descale(tmp10 + tmp11, Pass1Bits);
            o4 = Descale(tmp10 - tmp11, Pass1Bits);
        }

        int z1 = (tmp12 + tmp13) * Fix0541196100;
        o2 = Descale(z1 + (tmp13 * Fix0765366865), shift);
        o6 = Descale(z1 + (tmp12 * -Fix1847759065), shift);

        // Odd part (Figure 8 of Loeffler et al.).
        z1 = tmp4 + tmp7;
        int z2 = tmp5 + tmp6;
        int z3 = tmp4 + tmp6;
        int z4 = tmp5 + tmp7;
        int z5 = (z3 + z4) * Fix1175875602;

        tmp4 *= Fix0298631336;
        tmp5 *= Fix2053119869;
        tmp6 *= Fix3072711026;
        tmp7 *= Fix1501321110;
        z1 *= -Fix0899976223;
        z2 *= -Fix2562915447;
        z3 *= -Fix1961570560;
        z4 *= -Fix0390180644;

        z3 += z5;
        z4 += z5;

        o7 = Descale(tmp4 + z1 + z3, shift);
        o5 = Descale(tmp5 + z2 + z4, shift);
        o3 = Descale(tmp6 + z2 + z3, shift);
        o1 = Descale(tmp7 + z1 + z4, shift);
    }

    /// <summary>libjpeg's <c>DESCALE</c>: a right shift with rounding.</summary>
    private static int Descale(int value, int bits) => (value + (1 << (bits - 1))) >> bits;
}
