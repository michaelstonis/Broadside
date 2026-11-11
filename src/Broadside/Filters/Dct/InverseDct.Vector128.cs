using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.Filters.Dct;

/// <summary>
/// The <see cref="Vector128{T}"/> path of the 8 x 8 inverse DCT (issue #474): four columns, then four rows, at a time in 32-bit
/// lanes, bit-exact with <see cref="TransformScalar"/>.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.81 §A.3.3, §A.3.4; libjpeg's <c>jpeg_idct_islow</c>. Each lane runs the scalar arithmetic in the same order: the same
/// 32-bit products, sums and arithmetic shifts, which wrap identically on corrupt input. Pass 1 needs no rearrangement: a coefficient
/// row, widened from 16 bits and multiplied by its quantization row, holds the same coefficient of four columns. Its results are
/// transposed (four 4 x 4 transposes, constant byte shuffles and selects), so pass 2 sees four rows in its lanes; the outputs, one
/// sample of four rows per vector, are range-limited, then packed four to a 32-bit lane by shifts, which transposes them back into
/// rows. The range-limit table becomes arithmetic: the descaled value's low 10 bits, sign-extended, plus 128 and clamped to 0..255,
/// is exactly <c>RangeLimit[x &amp; 1023]</c>.
/// </para>
/// <para>
/// libjpeg's column shortcut (a column whose AC coefficients are zero takes its DC term alone) gives different results from the full
/// arithmetic when the DC term overflows it, so pass 1 computes the shortcut too and selects it per lane where its condition holds.
/// Its row shortcut needs no select: it equals the full arithmetic in the 10 bits the range limit keeps. A block whose 63 AC
/// coefficients are all zero is one fill, as in the scalar path.
/// </para>
/// <para>
/// Only operations that are fast in every build Broadside runs in (adding-a-benchmark skill, "SIMD kernels"): <c>LoadUnsafe</c>/
/// <c>StoreUnsafe</c> bounded by the slices at the top, the shift operators, <c>WidenLower</c>/<c>WidenUpper</c>, constant byte
/// <c>Shuffle</c>s, <c>ConditionalSelect</c>, <c>Min</c>/<c>Max</c>. The source is BCL-only, so the Blazor sample's kernel benchmark
/// links it.
/// </para>
/// </remarks>
internal static partial class InverseDct
{
    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="Transform"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="coefficients">The 64 quantized coefficients in natural order.</param>
    /// <param name="quantization">The 64 quantization values in natural order.</param>
    /// <param name="output">Where the first sample row starts.</param>
    /// <param name="stride">The distance between sample rows in <paramref name="output"/>, at least 8.</param>
    [SkipLocalsInit]
    public static void TransformVector(ReadOnlySpan<short> coefficients, ReadOnlySpan<int> quantization, Span<byte> output, int stride)
    {
        // Every access below stays inside these slices: 8 vectors of 8 coefficients, 16 of 4 quantization values, 8 rows of 8 bytes.
        coefficients = coefficients[..64];
        quantization = quantization[..64];
        output = output[..((7 * stride) + 8)];
        ref short c = ref MemoryMarshal.GetReference(coefficients);
        ref int q = ref MemoryMarshal.GetReference(quantization);
        ref byte o = ref MemoryMarshal.GetReference(output);

        Vector128<short> c0 = Vector128.LoadUnsafe(ref c, 0);
        Vector128<short> c1 = Vector128.LoadUnsafe(ref c, 8);
        Vector128<short> c2 = Vector128.LoadUnsafe(ref c, 16);
        Vector128<short> c3 = Vector128.LoadUnsafe(ref c, 24);
        Vector128<short> c4 = Vector128.LoadUnsafe(ref c, 32);
        Vector128<short> c5 = Vector128.LoadUnsafe(ref c, 40);
        Vector128<short> c6 = Vector128.LoadUnsafe(ref c, 48);
        Vector128<short> c7 = Vector128.LoadUnsafe(ref c, 56);
        Vector128<short> ac = c1 | c2 | c3 | c4 | c5 | c6 | c7;
        if ((ac | (c0 & Vector128.Create((short)0, -1, -1, -1, -1, -1, -1, -1))) == Vector128<short>.Zero)
        {
            // The DC term alone: every column takes the shortcut, then every row.
            int dc = c0.ToScalar() * q << Pass1Bits;
            ulong fill = RangeLimit[((dc + (1 << (Pass1Bits + 2))) >> (Pass1Bits + 3)) & 1023] * 0x0101010101010101UL;
            for (int row = 0; row < 8; row++)
            {
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, row * stride), fill);
            }

            return;
        }

        Vector128<int> fix0298631336 = Vector128.Create(Fix0298631336);
        Vector128<int> fix0390180644 = Vector128.Create(-Fix0390180644);
        Vector128<int> fix0541196100 = Vector128.Create(Fix0541196100);
        Vector128<int> fix0765366865 = Vector128.Create(Fix0765366865);
        Vector128<int> fix0899976223 = Vector128.Create(-Fix0899976223);
        Vector128<int> fix1175875602 = Vector128.Create(Fix1175875602);
        Vector128<int> fix1501321110 = Vector128.Create(Fix1501321110);
        Vector128<int> fix1847759065 = Vector128.Create(-Fix1847759065);
        Vector128<int> fix1961570560 = Vector128.Create(-Fix1961570560);
        Vector128<int> fix2053119869 = Vector128.Create(Fix2053119869);
        Vector128<int> fix2562915447 = Vector128.Create(-Fix2562915447);
        Vector128<int> fix3072711026 = Vector128.Create(Fix3072711026);
        Vector128<int> zero = Vector128<int>.Zero;
        Vector128<int> scale = Vector128.Create(1 << ConstBits);

        // Pass 1: columns 0-3, then 4-7, from the dequantized coefficient rows into the workspace (lanes are columns).
        Span<int> workspace = stackalloc int[64];
        ref int w = ref MemoryMarshal.GetReference(workspace);
        for (int half = 0; half < 8; half += 4)
        {
            bool lower = half == 0;
            Vector128<int> shortcut = Vector128.Equals(lower ? Vector128.WidenLower(ac).AsInt32() : Vector128.WidenUpper(ac).AsInt32(), zero);
            Vector128<int> r0 = (lower ? Vector128.WidenLower(c0) : Vector128.WidenUpper(c0)) * Vector128.LoadUnsafe(ref q, (nuint)half);
            Vector128<int> r1 = (lower ? Vector128.WidenLower(c1) : Vector128.WidenUpper(c1)) * Vector128.LoadUnsafe(ref q, (nuint)(8 + half));
            Vector128<int> r2 = (lower ? Vector128.WidenLower(c2) : Vector128.WidenUpper(c2)) * Vector128.LoadUnsafe(ref q, (nuint)(16 + half));
            Vector128<int> r3 = (lower ? Vector128.WidenLower(c3) : Vector128.WidenUpper(c3)) * Vector128.LoadUnsafe(ref q, (nuint)(24 + half));
            Vector128<int> r4 = (lower ? Vector128.WidenLower(c4) : Vector128.WidenUpper(c4)) * Vector128.LoadUnsafe(ref q, (nuint)(32 + half));
            Vector128<int> r5 = (lower ? Vector128.WidenLower(c5) : Vector128.WidenUpper(c5)) * Vector128.LoadUnsafe(ref q, (nuint)(40 + half));
            Vector128<int> r6 = (lower ? Vector128.WidenLower(c6) : Vector128.WidenUpper(c6)) * Vector128.LoadUnsafe(ref q, (nuint)(48 + half));
            Vector128<int> r7 = (lower ? Vector128.WidenLower(c7) : Vector128.WidenUpper(c7)) * Vector128.LoadUnsafe(ref q, (nuint)(56 + half));

            Vector128<int> z1 = (r2 + r6) * fix0541196100;
            Vector128<int> tmp2 = z1 + (r6 * fix1847759065);
            Vector128<int> tmp3 = z1 + (r2 * fix0765366865);
            Vector128<int> tmp0 = (r0 + r4) * scale;
            Vector128<int> tmp1 = (r0 - r4) * scale;
            Vector128<int> tmp10 = tmp0 + tmp3;
            Vector128<int> tmp13 = tmp0 - tmp3;
            Vector128<int> tmp11 = tmp1 + tmp2;
            Vector128<int> tmp12 = tmp1 - tmp2;

            // The odd part, inputs r7, r5, r3, r1 (the scalar Odd).
            z1 = r7 + r1;
            Vector128<int> z2 = r5 + r3;
            Vector128<int> z3 = r7 + r3;
            Vector128<int> z4 = r5 + r1;
            Vector128<int> z5 = (z3 + z4) * fix1175875602;
            z1 *= fix0899976223;
            z2 *= fix2562915447;
            z3 = (z3 * fix1961570560) + z5;
            z4 = (z4 * fix0390180644) + z5;
            tmp0 = (r7 * fix0298631336) + (z1 + z3);
            tmp1 = (r5 * fix2053119869) + (z2 + z4);
            tmp2 = (r3 * fix3072711026) + (z2 + z3);
            tmp3 = (r1 * fix1501321110) + (z1 + z4);

            const int shift = ConstBits - Pass1Bits;
            Vector128<int> round = Vector128.Create(1 << (shift - 1));
            Vector128<int> dc = r0 * Vector128.Create(1 << Pass1Bits);
            Vector128.ConditionalSelect(shortcut, dc, (tmp10 + tmp3 + round) >> shift).StoreUnsafe(ref w, (nuint)half);
            Vector128.ConditionalSelect(shortcut, dc, (tmp11 + tmp2 + round) >> shift).StoreUnsafe(ref w, (nuint)(8 + half));
            Vector128.ConditionalSelect(shortcut, dc, (tmp12 + tmp1 + round) >> shift).StoreUnsafe(ref w, (nuint)(16 + half));
            Vector128.ConditionalSelect(shortcut, dc, (tmp13 + tmp0 + round) >> shift).StoreUnsafe(ref w, (nuint)(24 + half));
            Vector128.ConditionalSelect(shortcut, dc, (tmp13 - tmp0 + round) >> shift).StoreUnsafe(ref w, (nuint)(32 + half));
            Vector128.ConditionalSelect(shortcut, dc, (tmp12 - tmp1 + round) >> shift).StoreUnsafe(ref w, (nuint)(40 + half));
            Vector128.ConditionalSelect(shortcut, dc, (tmp11 - tmp2 + round) >> shift).StoreUnsafe(ref w, (nuint)(48 + half));
            Vector128.ConditionalSelect(shortcut, dc, (tmp10 - tmp3 + round) >> shift).StoreUnsafe(ref w, (nuint)(56 + half));
        }

        // Pass 2: rows 0-3, then 4-7 (lanes are rows), descaled, level-shifted and range-limited into the output.
        Vector128<int> odd = Vector128.Create(0, -1, 0, -1);
        Vector128<int> high = Vector128.Create(0, 0, -1, -1);
        for (int top = 0; top < 64; top += 32)
        {
            // Columns 0-3 of the four rows, transposed: v0 holds column 0 of rows top/8 .. top/8 + 3.
            Vector128<int> a = Vector128.LoadUnsafe(ref w, (nuint)top);
            Vector128<int> b = Vector128.LoadUnsafe(ref w, (nuint)(top + 8));
            Vector128<int> d = Vector128.LoadUnsafe(ref w, (nuint)(top + 16));
            Vector128<int> e = Vector128.LoadUnsafe(ref w, (nuint)(top + 24));
            Vector128<int> t0 = Vector128.ConditionalSelect(odd, Vector128.Shuffle(b.AsByte(), Vector128.Create((byte)4, 5, 6, 7, 0, 1, 2, 3, 12, 13, 14, 15, 8, 9, 10, 11)).AsInt32(), a);
            Vector128<int> t1 = Vector128.ConditionalSelect(odd, b, Vector128.Shuffle(a.AsByte(), Vector128.Create((byte)4, 5, 6, 7, 0, 1, 2, 3, 12, 13, 14, 15, 8, 9, 10, 11)).AsInt32());
            Vector128<int> t2 = Vector128.ConditionalSelect(odd, Vector128.Shuffle(e.AsByte(), Vector128.Create((byte)4, 5, 6, 7, 0, 1, 2, 3, 12, 13, 14, 15, 8, 9, 10, 11)).AsInt32(), d);
            Vector128<int> t3 = Vector128.ConditionalSelect(odd, e, Vector128.Shuffle(d.AsByte(), Vector128.Create((byte)4, 5, 6, 7, 0, 1, 2, 3, 12, 13, 14, 15, 8, 9, 10, 11)).AsInt32());
            Vector128<int> v0 = Vector128.ConditionalSelect(high, Vector128.Shuffle(t2.AsByte(), Vector128.Create((byte)8, 9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7)).AsInt32(), t0);
            Vector128<int> v2 = Vector128.ConditionalSelect(high, t2, Vector128.Shuffle(t0.AsByte(), Vector128.Create((byte)8, 9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7)).AsInt32());
            Vector128<int> v1 = Vector128.ConditionalSelect(high, Vector128.Shuffle(t3.AsByte(), Vector128.Create((byte)8, 9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7)).AsInt32(), t1);
            Vector128<int> v3 = Vector128.ConditionalSelect(high, t3, Vector128.Shuffle(t1.AsByte(), Vector128.Create((byte)8, 9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7)).AsInt32());

            // Columns 4-7, the same way.
            a = Vector128.LoadUnsafe(ref w, (nuint)(top + 4));
            b = Vector128.LoadUnsafe(ref w, (nuint)(top + 12));
            d = Vector128.LoadUnsafe(ref w, (nuint)(top + 20));
            e = Vector128.LoadUnsafe(ref w, (nuint)(top + 28));
            t0 = Vector128.ConditionalSelect(odd, Vector128.Shuffle(b.AsByte(), Vector128.Create((byte)4, 5, 6, 7, 0, 1, 2, 3, 12, 13, 14, 15, 8, 9, 10, 11)).AsInt32(), a);
            t1 = Vector128.ConditionalSelect(odd, b, Vector128.Shuffle(a.AsByte(), Vector128.Create((byte)4, 5, 6, 7, 0, 1, 2, 3, 12, 13, 14, 15, 8, 9, 10, 11)).AsInt32());
            t2 = Vector128.ConditionalSelect(odd, Vector128.Shuffle(e.AsByte(), Vector128.Create((byte)4, 5, 6, 7, 0, 1, 2, 3, 12, 13, 14, 15, 8, 9, 10, 11)).AsInt32(), d);
            t3 = Vector128.ConditionalSelect(odd, e, Vector128.Shuffle(d.AsByte(), Vector128.Create((byte)4, 5, 6, 7, 0, 1, 2, 3, 12, 13, 14, 15, 8, 9, 10, 11)).AsInt32());
            Vector128<int> v4 = Vector128.ConditionalSelect(high, Vector128.Shuffle(t2.AsByte(), Vector128.Create((byte)8, 9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7)).AsInt32(), t0);
            Vector128<int> v6 = Vector128.ConditionalSelect(high, t2, Vector128.Shuffle(t0.AsByte(), Vector128.Create((byte)8, 9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7)).AsInt32());
            Vector128<int> v5 = Vector128.ConditionalSelect(high, Vector128.Shuffle(t3.AsByte(), Vector128.Create((byte)8, 9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7)).AsInt32(), t1);
            Vector128<int> v7 = Vector128.ConditionalSelect(high, t3, Vector128.Shuffle(t1.AsByte(), Vector128.Create((byte)8, 9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 3, 4, 5, 6, 7)).AsInt32());

            Vector128<int> z1 = (v2 + v6) * fix0541196100;
            Vector128<int> tmp2 = z1 + (v6 * fix1847759065);
            Vector128<int> tmp3 = z1 + (v2 * fix0765366865);
            Vector128<int> tmp0 = (v0 + v4) * scale;
            Vector128<int> tmp1 = (v0 - v4) * scale;
            Vector128<int> tmp10 = tmp0 + tmp3;
            Vector128<int> tmp13 = tmp0 - tmp3;
            Vector128<int> tmp11 = tmp1 + tmp2;
            Vector128<int> tmp12 = tmp1 - tmp2;

            z1 = v7 + v1;
            Vector128<int> z2 = v5 + v3;
            Vector128<int> z3 = v7 + v3;
            Vector128<int> z4 = v5 + v1;
            Vector128<int> z5 = (z3 + z4) * fix1175875602;
            z1 *= fix0899976223;
            z2 *= fix2562915447;
            z3 = (z3 * fix1961570560) + z5;
            z4 = (z4 * fix0390180644) + z5;
            tmp0 = (v7 * fix0298631336) + (z1 + z3);
            tmp1 = (v5 * fix2053119869) + (z2 + z4);
            tmp2 = (v3 * fix3072711026) + (z2 + z3);
            tmp3 = (v1 * fix1501321110) + (z1 + z4);

            // No shortcut for a row whose AC terms are zero: libjpeg's ((w0 + 16) >> 5) & 1023 and the full arithmetic's
            // (((w0 << 13) + 2^17) >> 18) & 1023 are the same bits of w0 + 16, even where w0 << 13 wraps. RangeLimit[x & 1023] is
            // the descaled value's low 10 bits sign-extended, plus 128, clamped to a byte: bits 18-27 of the sum moved to the top
            // (x 16) and shifted down arithmetically.
            const int shift = ConstBits + Pass1Bits + 3;
            Vector128<int> round = Vector128.Create(1 << (shift - 1));
            Vector128<int> top4 = Vector128.Create(16);
            Vector128<int> center = Vector128.Create(128);
            Vector128<int> max = Vector128.Create(255);
            Vector128<int> o0 = Vector128.Min(Vector128.Max((((tmp10 + tmp3 + round) * top4) >> 22) + center, zero), max);
            Vector128<int> o1 = Vector128.Min(Vector128.Max((((tmp11 + tmp2 + round) * top4) >> 22) + center, zero), max);
            Vector128<int> o2 = Vector128.Min(Vector128.Max((((tmp12 + tmp1 + round) * top4) >> 22) + center, zero), max);
            Vector128<int> o3 = Vector128.Min(Vector128.Max((((tmp13 + tmp0 + round) * top4) >> 22) + center, zero), max);
            Vector128<int> o4 = Vector128.Min(Vector128.Max((((tmp13 - tmp0 + round) * top4) >> 22) + center, zero), max);
            Vector128<int> o5 = Vector128.Min(Vector128.Max((((tmp12 - tmp1 + round) * top4) >> 22) + center, zero), max);
            Vector128<int> o6 = Vector128.Min(Vector128.Max((((tmp11 - tmp2 + round) * top4) >> 22) + center, zero), max);
            Vector128<int> o7 = Vector128.Min(Vector128.Max((((tmp10 - tmp3 + round) * top4) >> 22) + center, zero), max);

            // Samples 0-3 and 4-7 of each row in one 32-bit lane, interleaved into 8-byte rows.
            // (Multiplications rather than left shifts: the browser AOT build runs a vector shift at several times their cost.)
            Vector128<int> byte1 = Vector128.Create(1 << 8);
            Vector128<int> byte2 = Vector128.Create(1 << 16);
            Vector128<int> byte3 = Vector128.Create(1 << 24);
            Vector128<int> left = o0 | (o1 * byte1) | (o2 * byte2) | (o3 * byte3);
            Vector128<int> right = o4 | (o5 * byte1) | (o6 * byte2) | (o7 * byte3);
            Vector128<ulong> rows01 = Vector128.ConditionalSelect(
                odd,
                Vector128.Shuffle(right.AsByte(), Vector128.Create((byte)0, 1, 2, 3, 0, 1, 2, 3, 4, 5, 6, 7, 4, 5, 6, 7)).AsInt32(),
                Vector128.Shuffle(left.AsByte(), Vector128.Create((byte)0, 1, 2, 3, 0, 1, 2, 3, 4, 5, 6, 7, 4, 5, 6, 7)).AsInt32()).AsUInt64();
            Vector128<ulong> rows23 = Vector128.ConditionalSelect(
                odd,
                Vector128.Shuffle(right.AsByte(), Vector128.Create((byte)8, 9, 10, 11, 8, 9, 10, 11, 12, 13, 14, 15, 12, 13, 14, 15)).AsInt32(),
                Vector128.Shuffle(left.AsByte(), Vector128.Create((byte)8, 9, 10, 11, 8, 9, 10, 11, 12, 13, 14, 15, 12, 13, 14, 15)).AsInt32()).AsUInt64();
            int row = top >> 3;
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, row * stride), rows01.ToScalar());
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, (row + 1) * stride), rows01.GetElement(1));
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, (row + 2) * stride), rows23.ToScalar());
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref o, (row + 3) * stride), rows23.GetElement(1));
        }
    }
}
