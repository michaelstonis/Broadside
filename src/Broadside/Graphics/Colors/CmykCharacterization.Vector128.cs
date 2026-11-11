using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.Graphics.Colors;

/// <summary>
/// The <see cref="Vector128{T}"/> path of <see cref="ToRgbRow"/> (issue #476): four colors per iteration in two pairs of
/// <see langword="double"/> lanes, bit-exact with <see cref="ToRgbRowScalar"/>, which finishes an odd row.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §10.4.2.1. Each lane runs the scalar arithmetic in the scalar order: the same clip (two comparisons and selects, as
/// <see cref="Clamp01(double)"/>), the same monomials multiplied left to right, and each channel's sum started at 0 and added term
/// after term in the order of the coefficient tables, so every lane gives the bits <see cref="ToRgb"/> gives. Multiplies and adds are
/// separate operations (.NET never fuses them into a multiply-add on its own). The colors are gathered into lanes with
/// <c>Create</c> and written back one channel at a time: the interleaved four-in, three-out layout has no cheap shuffle across two
/// vectors in the cross-platform API.
/// </para>
/// <para>
/// Faster than the scalar path on the JIT, Native AOT and both browser builds (<c>docs/benchmarks/simd-kernels.md</c>). The source
/// is BCL-only, so the Blazor sample's kernel benchmark links it.
/// </para>
/// </remarks>
internal static partial class CmykCharacterization
{
    /// <summary>The coefficients broadcast to both lanes, term after term: red, green, blue.</summary>
    private static readonly Vector128<double>[] Weights = CreateWeights();
    /// <summary>The <see cref="Vector128{T}"/> path of <see cref="ToRgbRow"/>, run whether or not it is accelerated (so tests reach it).</summary>
    /// <param name="cmyk">Four components per color.</param>
    /// <param name="rgb">Three channels per color; its length sets the number of colors.</param>
    public static void ToRgbRowVector(ReadOnlySpan<float> cmyk, Span<float> rgb)
    {
        int count = rgb.Length / 3;
        cmyk = cmyk[..(count * 4)];
        ref float source = ref MemoryMarshal.GetReference(cmyk);
        ref float target = ref MemoryMarshal.GetReference(rgb);
        ref Vector128<double> weights = ref MemoryMarshal.GetArrayDataReference(Weights);
        int i = 0;
        for (; i <= count - 4; i += 4)
        {
            ref float first = ref Unsafe.Add(ref source, 4 * i);
            Vector128<double> cA = Clamp01(Vector128.Create((double)first, (double)Unsafe.Add(ref first, 4)));
            Vector128<double> mA = Clamp01(Vector128.Create((double)Unsafe.Add(ref first, 1), (double)Unsafe.Add(ref first, 5)));
            Vector128<double> yA = Clamp01(Vector128.Create((double)Unsafe.Add(ref first, 2), (double)Unsafe.Add(ref first, 6)));
            Vector128<double> kA = Clamp01(Vector128.Create((double)Unsafe.Add(ref first, 3), (double)Unsafe.Add(ref first, 7)));
            Vector128<double> cB = Clamp01(Vector128.Create((double)Unsafe.Add(ref first, 8), (double)Unsafe.Add(ref first, 12)));
            Vector128<double> mB = Clamp01(Vector128.Create((double)Unsafe.Add(ref first, 9), (double)Unsafe.Add(ref first, 13)));
            Vector128<double> yB = Clamp01(Vector128.Create((double)Unsafe.Add(ref first, 10), (double)Unsafe.Add(ref first, 14)));
            Vector128<double> kB = Clamp01(Vector128.Create((double)Unsafe.Add(ref first, 11), (double)Unsafe.Add(ref first, 15)));
            Vector128<double> rSumA = Vector128<double>.Zero;
            Vector128<double> gSumA = Vector128<double>.Zero;
            Vector128<double> bSumA = Vector128<double>.Zero;
            Vector128<double> rSumB = Vector128<double>.Zero;
            Vector128<double> gSumB = Vector128<double>.Zero;
            Vector128<double> bSumB = Vector128<double>.Zero;

            // k
            Vector128<double> r0 = Unsafe.Add(ref weights, 0);
            rSumA += r0 * kA;
            rSumB += r0 * kB;
            Vector128<double> g0 = Unsafe.Add(ref weights, 1);
            gSumA += g0 * kA;
            gSumB += g0 * kB;
            Vector128<double> b0 = Unsafe.Add(ref weights, 2);
            bSumA += b0 * kA;
            bSumB += b0 * kB;

            // y
            Vector128<double> r1 = Unsafe.Add(ref weights, 3);
            rSumA += r1 * yA;
            rSumB += r1 * yB;
            Vector128<double> g1 = Unsafe.Add(ref weights, 4);
            gSumA += g1 * yA;
            gSumB += g1 * yB;
            Vector128<double> b1 = Unsafe.Add(ref weights, 5);
            bSumA += b1 * yA;
            bSumB += b1 * yB;

            // m
            Vector128<double> r2 = Unsafe.Add(ref weights, 6);
            rSumA += r2 * mA;
            rSumB += r2 * mB;
            Vector128<double> g2 = Unsafe.Add(ref weights, 7);
            gSumA += g2 * mA;
            gSumB += g2 * mB;
            Vector128<double> b2 = Unsafe.Add(ref weights, 8);
            bSumA += b2 * mA;
            bSumB += b2 * mB;

            // c
            Vector128<double> r3 = Unsafe.Add(ref weights, 9);
            rSumA += r3 * cA;
            rSumB += r3 * cB;
            Vector128<double> g3 = Unsafe.Add(ref weights, 10);
            gSumA += g3 * cA;
            gSumB += g3 * cB;
            Vector128<double> b3 = Unsafe.Add(ref weights, 11);
            bSumA += b3 * cA;
            bSumB += b3 * cB;

            // k * k
            Vector128<double> t4A = kA * kA;
            Vector128<double> t4B = kB * kB;
            Vector128<double> r4 = Unsafe.Add(ref weights, 12);
            rSumA += r4 * t4A;
            rSumB += r4 * t4B;
            Vector128<double> g4 = Unsafe.Add(ref weights, 13);
            gSumA += g4 * t4A;
            gSumB += g4 * t4B;
            Vector128<double> b4 = Unsafe.Add(ref weights, 14);
            bSumA += b4 * t4A;
            bSumB += b4 * t4B;

            // y * k
            Vector128<double> t5A = yA * kA;
            Vector128<double> t5B = yB * kB;
            Vector128<double> r5 = Unsafe.Add(ref weights, 15);
            rSumA += r5 * t5A;
            rSumB += r5 * t5B;
            Vector128<double> g5 = Unsafe.Add(ref weights, 16);
            gSumA += g5 * t5A;
            gSumB += g5 * t5B;
            Vector128<double> b5 = Unsafe.Add(ref weights, 17);
            bSumA += b5 * t5A;
            bSumB += b5 * t5B;

            // y * y
            Vector128<double> t6A = yA * yA;
            Vector128<double> t6B = yB * yB;
            Vector128<double> r6 = Unsafe.Add(ref weights, 18);
            rSumA += r6 * t6A;
            rSumB += r6 * t6B;
            Vector128<double> g6 = Unsafe.Add(ref weights, 19);
            gSumA += g6 * t6A;
            gSumB += g6 * t6B;
            Vector128<double> b6 = Unsafe.Add(ref weights, 20);
            bSumA += b6 * t6A;
            bSumB += b6 * t6B;

            // m * k
            Vector128<double> t7A = mA * kA;
            Vector128<double> t7B = mB * kB;
            Vector128<double> r7 = Unsafe.Add(ref weights, 21);
            rSumA += r7 * t7A;
            rSumB += r7 * t7B;
            Vector128<double> g7 = Unsafe.Add(ref weights, 22);
            gSumA += g7 * t7A;
            gSumB += g7 * t7B;
            Vector128<double> b7 = Unsafe.Add(ref weights, 23);
            bSumA += b7 * t7A;
            bSumB += b7 * t7B;

            // m * y
            Vector128<double> t8A = mA * yA;
            Vector128<double> t8B = mB * yB;
            Vector128<double> r8 = Unsafe.Add(ref weights, 24);
            rSumA += r8 * t8A;
            rSumB += r8 * t8B;
            Vector128<double> g8 = Unsafe.Add(ref weights, 25);
            gSumA += g8 * t8A;
            gSumB += g8 * t8B;
            Vector128<double> b8 = Unsafe.Add(ref weights, 26);
            bSumA += b8 * t8A;
            bSumB += b8 * t8B;

            // m * m
            Vector128<double> t9A = mA * mA;
            Vector128<double> t9B = mB * mB;
            Vector128<double> r9 = Unsafe.Add(ref weights, 27);
            rSumA += r9 * t9A;
            rSumB += r9 * t9B;
            Vector128<double> g9 = Unsafe.Add(ref weights, 28);
            gSumA += g9 * t9A;
            gSumB += g9 * t9B;
            Vector128<double> b9 = Unsafe.Add(ref weights, 29);
            bSumA += b9 * t9A;
            bSumB += b9 * t9B;

            // c * k
            Vector128<double> t10A = cA * kA;
            Vector128<double> t10B = cB * kB;
            Vector128<double> r10 = Unsafe.Add(ref weights, 30);
            rSumA += r10 * t10A;
            rSumB += r10 * t10B;
            Vector128<double> g10 = Unsafe.Add(ref weights, 31);
            gSumA += g10 * t10A;
            gSumB += g10 * t10B;
            Vector128<double> b10 = Unsafe.Add(ref weights, 32);
            bSumA += b10 * t10A;
            bSumB += b10 * t10B;

            // c * y
            Vector128<double> t11A = cA * yA;
            Vector128<double> t11B = cB * yB;
            Vector128<double> r11 = Unsafe.Add(ref weights, 33);
            rSumA += r11 * t11A;
            rSumB += r11 * t11B;
            Vector128<double> g11 = Unsafe.Add(ref weights, 34);
            gSumA += g11 * t11A;
            gSumB += g11 * t11B;
            Vector128<double> b11 = Unsafe.Add(ref weights, 35);
            bSumA += b11 * t11A;
            bSumB += b11 * t11B;

            // c * m
            Vector128<double> t12A = cA * mA;
            Vector128<double> t12B = cB * mB;
            Vector128<double> r12 = Unsafe.Add(ref weights, 36);
            rSumA += r12 * t12A;
            rSumB += r12 * t12B;
            Vector128<double> g12 = Unsafe.Add(ref weights, 37);
            gSumA += g12 * t12A;
            gSumB += g12 * t12B;
            Vector128<double> b12 = Unsafe.Add(ref weights, 38);
            bSumA += b12 * t12A;
            bSumB += b12 * t12B;

            // c * c
            Vector128<double> t13A = cA * cA;
            Vector128<double> t13B = cB * cB;
            Vector128<double> r13 = Unsafe.Add(ref weights, 39);
            rSumA += r13 * t13A;
            rSumB += r13 * t13B;
            Vector128<double> g13 = Unsafe.Add(ref weights, 40);
            gSumA += g13 * t13A;
            gSumB += g13 * t13B;
            Vector128<double> b13 = Unsafe.Add(ref weights, 41);
            bSumA += b13 * t13A;
            bSumB += b13 * t13B;

            // k * k * k
            Vector128<double> t14A = kA * kA * kA;
            Vector128<double> t14B = kB * kB * kB;
            Vector128<double> r14 = Unsafe.Add(ref weights, 42);
            rSumA += r14 * t14A;
            rSumB += r14 * t14B;
            Vector128<double> g14 = Unsafe.Add(ref weights, 43);
            gSumA += g14 * t14A;
            gSumB += g14 * t14B;
            Vector128<double> b14 = Unsafe.Add(ref weights, 44);
            bSumA += b14 * t14A;
            bSumB += b14 * t14B;

            // y * k * k
            Vector128<double> t15A = yA * kA * kA;
            Vector128<double> t15B = yB * kB * kB;
            Vector128<double> r15 = Unsafe.Add(ref weights, 45);
            rSumA += r15 * t15A;
            rSumB += r15 * t15B;
            Vector128<double> g15 = Unsafe.Add(ref weights, 46);
            gSumA += g15 * t15A;
            gSumB += g15 * t15B;
            Vector128<double> b15 = Unsafe.Add(ref weights, 47);
            bSumA += b15 * t15A;
            bSumB += b15 * t15B;

            // y * y * k
            Vector128<double> t16A = yA * yA * kA;
            Vector128<double> t16B = yB * yB * kB;
            Vector128<double> r16 = Unsafe.Add(ref weights, 48);
            rSumA += r16 * t16A;
            rSumB += r16 * t16B;
            Vector128<double> g16 = Unsafe.Add(ref weights, 49);
            gSumA += g16 * t16A;
            gSumB += g16 * t16B;
            Vector128<double> b16 = Unsafe.Add(ref weights, 50);
            bSumA += b16 * t16A;
            bSumB += b16 * t16B;

            // y * y * y
            Vector128<double> t17A = yA * yA * yA;
            Vector128<double> t17B = yB * yB * yB;
            Vector128<double> r17 = Unsafe.Add(ref weights, 51);
            rSumA += r17 * t17A;
            rSumB += r17 * t17B;
            Vector128<double> g17 = Unsafe.Add(ref weights, 52);
            gSumA += g17 * t17A;
            gSumB += g17 * t17B;
            Vector128<double> b17 = Unsafe.Add(ref weights, 53);
            bSumA += b17 * t17A;
            bSumB += b17 * t17B;

            // m * k * k
            Vector128<double> t18A = mA * kA * kA;
            Vector128<double> t18B = mB * kB * kB;
            Vector128<double> r18 = Unsafe.Add(ref weights, 54);
            rSumA += r18 * t18A;
            rSumB += r18 * t18B;
            Vector128<double> g18 = Unsafe.Add(ref weights, 55);
            gSumA += g18 * t18A;
            gSumB += g18 * t18B;
            Vector128<double> b18 = Unsafe.Add(ref weights, 56);
            bSumA += b18 * t18A;
            bSumB += b18 * t18B;

            // m * y * k
            Vector128<double> t19A = mA * yA * kA;
            Vector128<double> t19B = mB * yB * kB;
            Vector128<double> r19 = Unsafe.Add(ref weights, 57);
            rSumA += r19 * t19A;
            rSumB += r19 * t19B;
            Vector128<double> g19 = Unsafe.Add(ref weights, 58);
            gSumA += g19 * t19A;
            gSumB += g19 * t19B;
            Vector128<double> b19 = Unsafe.Add(ref weights, 59);
            bSumA += b19 * t19A;
            bSumB += b19 * t19B;

            // m * y * y
            Vector128<double> t20A = mA * yA * yA;
            Vector128<double> t20B = mB * yB * yB;
            Vector128<double> r20 = Unsafe.Add(ref weights, 60);
            rSumA += r20 * t20A;
            rSumB += r20 * t20B;
            Vector128<double> g20 = Unsafe.Add(ref weights, 61);
            gSumA += g20 * t20A;
            gSumB += g20 * t20B;
            Vector128<double> b20 = Unsafe.Add(ref weights, 62);
            bSumA += b20 * t20A;
            bSumB += b20 * t20B;

            // m * m * k
            Vector128<double> t21A = mA * mA * kA;
            Vector128<double> t21B = mB * mB * kB;
            Vector128<double> r21 = Unsafe.Add(ref weights, 63);
            rSumA += r21 * t21A;
            rSumB += r21 * t21B;
            Vector128<double> g21 = Unsafe.Add(ref weights, 64);
            gSumA += g21 * t21A;
            gSumB += g21 * t21B;
            Vector128<double> b21 = Unsafe.Add(ref weights, 65);
            bSumA += b21 * t21A;
            bSumB += b21 * t21B;

            // m * m * y
            Vector128<double> t22A = mA * mA * yA;
            Vector128<double> t22B = mB * mB * yB;
            Vector128<double> r22 = Unsafe.Add(ref weights, 66);
            rSumA += r22 * t22A;
            rSumB += r22 * t22B;
            Vector128<double> g22 = Unsafe.Add(ref weights, 67);
            gSumA += g22 * t22A;
            gSumB += g22 * t22B;
            Vector128<double> b22 = Unsafe.Add(ref weights, 68);
            bSumA += b22 * t22A;
            bSumB += b22 * t22B;

            // m * m * m
            Vector128<double> t23A = mA * mA * mA;
            Vector128<double> t23B = mB * mB * mB;
            Vector128<double> r23 = Unsafe.Add(ref weights, 69);
            rSumA += r23 * t23A;
            rSumB += r23 * t23B;
            Vector128<double> g23 = Unsafe.Add(ref weights, 70);
            gSumA += g23 * t23A;
            gSumB += g23 * t23B;
            Vector128<double> b23 = Unsafe.Add(ref weights, 71);
            bSumA += b23 * t23A;
            bSumB += b23 * t23B;

            // c * k * k
            Vector128<double> t24A = cA * kA * kA;
            Vector128<double> t24B = cB * kB * kB;
            Vector128<double> r24 = Unsafe.Add(ref weights, 72);
            rSumA += r24 * t24A;
            rSumB += r24 * t24B;
            Vector128<double> g24 = Unsafe.Add(ref weights, 73);
            gSumA += g24 * t24A;
            gSumB += g24 * t24B;
            Vector128<double> b24 = Unsafe.Add(ref weights, 74);
            bSumA += b24 * t24A;
            bSumB += b24 * t24B;

            // c * y * k
            Vector128<double> t25A = cA * yA * kA;
            Vector128<double> t25B = cB * yB * kB;
            Vector128<double> r25 = Unsafe.Add(ref weights, 75);
            rSumA += r25 * t25A;
            rSumB += r25 * t25B;
            Vector128<double> g25 = Unsafe.Add(ref weights, 76);
            gSumA += g25 * t25A;
            gSumB += g25 * t25B;
            Vector128<double> b25 = Unsafe.Add(ref weights, 77);
            bSumA += b25 * t25A;
            bSumB += b25 * t25B;

            // c * y * y
            Vector128<double> t26A = cA * yA * yA;
            Vector128<double> t26B = cB * yB * yB;
            Vector128<double> r26 = Unsafe.Add(ref weights, 78);
            rSumA += r26 * t26A;
            rSumB += r26 * t26B;
            Vector128<double> g26 = Unsafe.Add(ref weights, 79);
            gSumA += g26 * t26A;
            gSumB += g26 * t26B;
            Vector128<double> b26 = Unsafe.Add(ref weights, 80);
            bSumA += b26 * t26A;
            bSumB += b26 * t26B;

            // c * m * k
            Vector128<double> t27A = cA * mA * kA;
            Vector128<double> t27B = cB * mB * kB;
            Vector128<double> r27 = Unsafe.Add(ref weights, 81);
            rSumA += r27 * t27A;
            rSumB += r27 * t27B;
            Vector128<double> g27 = Unsafe.Add(ref weights, 82);
            gSumA += g27 * t27A;
            gSumB += g27 * t27B;
            Vector128<double> b27 = Unsafe.Add(ref weights, 83);
            bSumA += b27 * t27A;
            bSumB += b27 * t27B;

            // c * m * y
            Vector128<double> t28A = cA * mA * yA;
            Vector128<double> t28B = cB * mB * yB;
            Vector128<double> r28 = Unsafe.Add(ref weights, 84);
            rSumA += r28 * t28A;
            rSumB += r28 * t28B;
            Vector128<double> g28 = Unsafe.Add(ref weights, 85);
            gSumA += g28 * t28A;
            gSumB += g28 * t28B;
            Vector128<double> b28 = Unsafe.Add(ref weights, 86);
            bSumA += b28 * t28A;
            bSumB += b28 * t28B;

            // c * m * m
            Vector128<double> t29A = cA * mA * mA;
            Vector128<double> t29B = cB * mB * mB;
            Vector128<double> r29 = Unsafe.Add(ref weights, 87);
            rSumA += r29 * t29A;
            rSumB += r29 * t29B;
            Vector128<double> g29 = Unsafe.Add(ref weights, 88);
            gSumA += g29 * t29A;
            gSumB += g29 * t29B;
            Vector128<double> b29 = Unsafe.Add(ref weights, 89);
            bSumA += b29 * t29A;
            bSumB += b29 * t29B;

            // c * c * k
            Vector128<double> t30A = cA * cA * kA;
            Vector128<double> t30B = cB * cB * kB;
            Vector128<double> r30 = Unsafe.Add(ref weights, 90);
            rSumA += r30 * t30A;
            rSumB += r30 * t30B;
            Vector128<double> g30 = Unsafe.Add(ref weights, 91);
            gSumA += g30 * t30A;
            gSumB += g30 * t30B;
            Vector128<double> b30 = Unsafe.Add(ref weights, 92);
            bSumA += b30 * t30A;
            bSumB += b30 * t30B;

            // c * c * y
            Vector128<double> t31A = cA * cA * yA;
            Vector128<double> t31B = cB * cB * yB;
            Vector128<double> r31 = Unsafe.Add(ref weights, 93);
            rSumA += r31 * t31A;
            rSumB += r31 * t31B;
            Vector128<double> g31 = Unsafe.Add(ref weights, 94);
            gSumA += g31 * t31A;
            gSumB += g31 * t31B;
            Vector128<double> b31 = Unsafe.Add(ref weights, 95);
            bSumA += b31 * t31A;
            bSumB += b31 * t31B;

            // c * c * m
            Vector128<double> t32A = cA * cA * mA;
            Vector128<double> t32B = cB * cB * mB;
            Vector128<double> r32 = Unsafe.Add(ref weights, 96);
            rSumA += r32 * t32A;
            rSumB += r32 * t32B;
            Vector128<double> g32 = Unsafe.Add(ref weights, 97);
            gSumA += g32 * t32A;
            gSumB += g32 * t32B;
            Vector128<double> b32 = Unsafe.Add(ref weights, 98);
            bSumA += b32 * t32A;
            bSumB += b32 * t32B;

            // c * c * c
            Vector128<double> t33A = cA * cA * cA;
            Vector128<double> t33B = cB * cB * cB;
            Vector128<double> r33 = Unsafe.Add(ref weights, 99);
            rSumA += r33 * t33A;
            rSumB += r33 * t33B;
            Vector128<double> g33 = Unsafe.Add(ref weights, 100);
            gSumA += g33 * t33A;
            gSumB += g33 * t33B;
            Vector128<double> b33 = Unsafe.Add(ref weights, 101);
            bSumA += b33 * t33A;
            bSumB += b33 * t33B;

            ref float pixel = ref Unsafe.Add(ref target, 3 * i);
            Store(ref pixel, Clamp01(Vector128<double>.One + rSumA), Clamp01(Vector128<double>.One + gSumA), Clamp01(Vector128<double>.One + bSumA));
            Store(ref Unsafe.Add(ref pixel, 6), Clamp01(Vector128<double>.One + rSumB), Clamp01(Vector128<double>.One + gSumB), Clamp01(Vector128<double>.One + bSumB));
        }

        ToRgbRowScalar(cmyk[(4 * i)..], rgb[(3 * i)..]);
    }

    private static Vector128<double>[] CreateWeights()
    {
        var weights = new Vector128<double>[3 * Red.Length];
        for (int j = 0; j < Red.Length; j++)
        {
            weights[3 * j] = Vector128.Create(Red[j]);
            weights[(3 * j) + 1] = Vector128.Create(Green[j]);
            weights[(3 * j) + 2] = Vector128.Create(Blue[j]);
        }

        return weights;
    }

    /// <summary>Writes two colors, each lane's channels narrowed to <see langword="float"/> as the scalar path narrows them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(ref float pixel, Vector128<double> r, Vector128<double> g, Vector128<double> b)
    {
        pixel = (float)r.ToScalar();
        Unsafe.Add(ref pixel, 1) = (float)g.ToScalar();
        Unsafe.Add(ref pixel, 2) = (float)b.ToScalar();
        Unsafe.Add(ref pixel, 3) = (float)r.GetElement(1);
        Unsafe.Add(ref pixel, 4) = (float)g.GetElement(1);
        Unsafe.Add(ref pixel, 5) = (float)b.GetElement(1);
    }

    /// <summary><see cref="Clamp01(double)"/> in each lane: 0 unless greater than 0 (so NaN becomes 0), then at most 1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> Clamp01(Vector128<double> value) =>
        Vector128.ConditionalSelect(
            Vector128.GreaterThan(value, Vector128<double>.Zero),
            Vector128.ConditionalSelect(Vector128.LessThan(value, Vector128<double>.One), value, Vector128<double>.One),
            Vector128<double>.Zero);
}
