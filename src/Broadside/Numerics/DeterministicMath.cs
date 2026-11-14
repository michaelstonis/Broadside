using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Broadside.Numerics;

/// <summary>
/// The elementary functions rendering needs, computed the same way on every platform: power, natural and decimal logarithm,
/// exponential, sine and cosine (in radians and in degrees), arc tangent, arc cosine and cube root.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Math"/> and <see cref="MathF"/> hand these functions to the platform's C library (glibc, the Windows CRT, Apple's libm),
/// whose results may differ in the last bit; a last bit that moves a sample across a rounding boundary changes a pixel, and the
/// software rasterizer promises the same bytes on every platform. These implementations use only IEEE 754 addition, subtraction,
/// multiplication, division, square root and exact scaling, never a fused multiply-add, so they round identically wherever .NET runs.
/// Their polynomials and reductions are those of fdlibm (the FreeBSD <c>msun</c> library), except that the power is table-driven after
/// ARM's optimized-routines (as musl's is), twice as fast as log and exp composed; errors are a few units in the last place,
/// far below what any 8- or 16-bit sample resolves. Special values (zeros, infinities, NaN) follow IEEE 754 §9.2.1.
/// </para>
/// <para>
/// In the browser (WebAssembly) the runtime's library is used instead: the interpreter runs these managed versions several times
/// slower than the library compiled to WebAssembly, and rendering there must not get slower. WebAssembly is therefore outside the
/// bit-identical guarantee until it is verified on its own.
/// </para>
/// <para>
/// This file is compiled into both the core package and <c>Broadside.Rendering</c> (which cannot see the core's internals), so it
/// must stay self-contained.
/// </para>
/// </remarks>
internal static class DeterministicMath
{
    private const double Ln2Hi = 6.93147180369123816490e-01;
    private const double Ln2Lo = 1.90821492927058770002e-10;
    private const double InverseLn2 = 1.44269504088896338700e+00;
    private const double Sqrt2 = 1.41421356237309504880;
    private const double Two54 = 1.80143985094819840000e+16;
    private const double InverseLn10Hi = 4.34294481878168880939e-01;
    private const double InverseLn10Lo = 2.50829467116452752298e-11;

    // log(1 + f) = f - f²/2 + s·(f²/2 + R(s²)), s = f / (2 + f) (fdlibm e_log.c).
    private const double Lg1 = 6.666666666666735130e-01;
    private const double Lg2 = 3.999999999940941908e-01;
    private const double Lg3 = 2.857142874366239149e-01;
    private const double Lg4 = 2.222219843214978396e-01;
    private const double Lg5 = 1.818357216161805012e-01;
    private const double Lg6 = 1.531383769920937332e-01;
    private const double Lg7 = 1.479819860511658591e-01;

    // exp(r) on |r| <= ln2/2 through the Remez polynomial of r·(e^r + 1)/(e^r - 1) (fdlibm e_exp.c).
    private const double P1 = 1.66666666666666019037e-01;
    private const double P2 = -2.77777777770155933842e-03;
    private const double P3 = 6.61375632143793436117e-05;
    private const double P4 = -1.65339022054652515390e-06;
    private const double P5 = 4.13813679705723846039e-08;
    private const double ExpOverflow = 7.09782712893383973096e+02;
    private const double ExpUnderflow = -7.45133219101941108420e+02;

    // sin and cos on [-π/4, π/4] (fdlibm k_sin.c, k_cos.c).
    private const double S1 = -1.66666666666666324348e-01;
    private const double S2 = 8.33333333332248946124e-03;
    private const double S3 = -1.98412698298579493134e-04;
    private const double S4 = 2.75573137070700676789e-06;
    private const double S5 = -2.50507602534068634195e-08;
    private const double S6 = 1.58969099521155010221e-10;
    private const double C1 = 4.16666666666666019037e-02;
    private const double C2 = -1.38888888888741095749e-03;
    private const double C3 = 2.48015872894767294178e-05;
    private const double C4 = -2.75573143513906633035e-07;
    private const double C5 = 2.08757232129817482790e-09;
    private const double C6 = -1.13596475577881948265e-11;

    // π/2 in three parts, the first two with trailing zeros so n·part is exact for the multiples a reduction meets (fdlibm e_rem_pio2.c).
    private const double TwoOverPi = 6.36619772367581382433e-01;
    private const double PiOver2Part1 = 1.57079632673412561417e+00;
    private const double PiOver2Part2 = 6.07710050630396597660e-11;
    private const double PiOver2Part3 = 2.02226624871116645580e-21;
    private const double DegreesToRadians = 1.74532925199432957692e-02;

    // atan (fdlibm s_atan.c, e_atan2.c).
    private const double AtanHalfHi = 4.63647609000806093515e-01;
    private const double AtanOneHi = 7.85398163397448278999e-01;
    private const double AtanThreeHalvesHi = 9.82793723247329054082e-01;
    private const double AtanInfinityHi = 1.57079632679489655800e+00;
    private const double AtanHalfLo = 2.26987774529616870924e-17;
    private const double AtanOneLo = 3.06161699786838301793e-17;
    private const double AtanThreeHalvesLo = 1.39033110312309984516e-17;
    private const double AtanInfinityLo = 6.12323399573676603587e-17;
    private const double AT0 = 3.33333333333329318027e-01;
    private const double AT1 = -1.99999999998764832476e-01;
    private const double AT2 = 1.42857142725034663711e-01;
    private const double AT3 = -1.11111104054623557880e-01;
    private const double AT4 = 9.09088713343650656196e-02;
    private const double AT5 = -7.69187620504482999495e-02;
    private const double AT6 = 6.66107313738753120669e-02;
    private const double AT7 = -5.83357013379057348645e-02;
    private const double AT8 = 4.97687799461593236017e-02;
    private const double AT9 = -3.65315727442169155270e-02;
    private const double AT10 = 1.62858201153657823623e-02;
    private const double Pi = 3.1415926535897931160e+00;
    private const double PiLo = 1.2246467991473531772e-16;
    private const double PiOver2 = 1.5707963267948965580e+00;
    private const double PiOver4 = 7.8539816339744827900e-01;

    // cbrt (fdlibm s_cbrt.c): the exponent bias divided by 3 in the high word, and the polynomial refining 1/∛ of the guess.
    private const ulong CbrtB1 = 715094163;
    private const double CbrtP0 = 1.87595182427177009643;
    private const double CbrtP1 = -1.88497979543377169875;
    private const double CbrtP2 = 1.621429720105354466140;
    private const double CbrtP3 = -0.758397934778766047437;
    private const double CbrtP4 = 0.145996192886612446982;

    // Pow: table-driven log and exp after ARM's optimized-routines pow (also musl's), without a fused multiply-add and without its
    // extra-precise tail. log(x) = k·ln 2 + log(c) + log1p(z/c − 1) for x = 2^k·z, z in [0x1.69555p-1, 0x1.69555p0) split bitwise
    // into 128 subintervals; 1/c has at most 9 significant bits so z/c − 1 splits exactly. exp(x) = 2^(k/128)·exp(r) with
    // |r| <= ln 2/256. Both polynomials are Taylor series, accurate to better than 2^-55 relative here.
    private const int PowTableBits = 7;
    private const int PowTableSize = 1 << PowTableBits;
    private const ulong PowLogOffset = 0x3FE6_9555_0000_0000;
    private const double PowLn2Hi = 0.6931471805598903;
    private const double PowLn2Lo = 5.497923018708371e-14;
    private const double PowLogA2 = -1.0 / 2;
    private const double PowLogA3 = 1.0 / 3;
    private const double PowLogA4 = -1.0 / 4;
    private const double PowLogA5 = 1.0 / 5;
    private const double PowLogA6 = -1.0 / 6;
    private const double PowLogA7 = 1.0 / 7;
    private const double PowLogA8 = -1.0 / 8;
    private const double PowExpInverseLn2N = 184.6649652337873;
    private const double PowExpLn2HiN = 0.005415212348111709;
    private const double PowExpLn2LoN = 1.2864023111638346e-14;
    private const double PowExpShift = 6755399441055744.0;
    private const double PowExpC2 = 0.5;
    private const double PowExpC3 = 1.0 / 6;
    private const double PowExpC4 = 1.0 / 24;
    private const double PowExpC5 = 1.0 / 120;
    private const double Two52 = 4503599627370496.0;
    private const double Two1009 = 5.486124068793689e+303;
    private const double TwoMinus1022 = 2.2250738585072014e-308;

    /// <summary>The integer exponents up to which <see cref="Pow"/> multiplies instead of going through the logarithm.</summary>
    private const int MaxMultipliedExponent = 64;

    /// <summary>Returns <paramref name="x"/> raised to <paramref name="y"/>, as <see cref="Math.Pow"/>.</summary>
    /// <param name="x">The base.</param>
    /// <param name="y">The exponent.</param>
    /// <returns>x<sup>y</sup>; exact for y of 0, 1 and 2, the square root for y = 1/2, and exact for small integer y whenever the
    /// result is representable.</returns>
    public static double Pow(double x, double y)
    {
        if (OperatingSystem.IsBrowser())
        {
            return Math.Pow(x, y);
        }

        // The common case first, a positive finite base and a finite exponent that is neither an integer nor 1/2, so it pays for
        // none of the special-value tests (PowOther reaches PowCore for it all the same) and Pow stays small enough to inline.
        if (x > 0 && x < double.PositiveInfinity && Math.Abs(y) < 4503599627370496.0 && y != Math.Truncate(y) && y != 0.5)
        {
            return PowCore(x, y);
        }

        return PowOther(x, y);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double PowOther(double x, double y)
    {
        if (y == 0 || x == 1)
        {
            return 1;
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        if (y == 1)
        {
            return x;
        }

        if (!double.IsFinite(x) || !double.IsFinite(y) || x == 0)
        {
            return PowSpecial(x, y);
        }

        if (y == 2)
        {
            return x * x;
        }

        if (y == 0.5 && x > 0)
        {
            return Math.Sqrt(x);
        }

        if (double.IsInteger(y) && Math.Abs(y) <= MaxMultipliedExponent)
        {
            return MultipliedPower(x, (int)y);
        }

        if (x < 0)
        {
            if (!double.IsInteger(y))
            {
                return double.NaN;
            }

            double magnitude = PowCore(-x, y);
            return IsOddInteger(y) ? -magnitude : magnitude;
        }

        return PowCore(x, y);
    }

    /// <summary>Returns the natural logarithm of <paramref name="x"/>, as <see cref="Math.Log(double)"/>.</summary>
    /// <param name="x">The value.</param>
    /// <returns>ln x; −∞ for ±0, NaN for a negative value.</returns>
    public static double Log(double x) => OperatingSystem.IsBrowser() ? Math.Log(x) : LogCore(x);

    /// <summary>Returns the base-10 logarithm of <paramref name="x"/>, as <see cref="Math.Log10"/>.</summary>
    /// <param name="x">The value.</param>
    /// <returns>log<sub>10</sub> x; exact for the powers of ten a double holds exactly.</returns>
    public static double Log10(double x)
    {
        if (OperatingSystem.IsBrowser())
        {
            return Math.Log10(x);
        }

        if (x >= 1 && x <= 1e22)
        {
            double power = 1;
            for (int k = 0; k <= 22; k++, power *= 10)
            {
                if (x == power)
                {
                    return k;
                }

                if (x < power)
                {
                    break;
                }
            }
        }

        double ln = LogCore(x);
        if (!double.IsFinite(ln) || ln == 0)
        {
            return ln;
        }

        // ln·(1/ln 10) with the reciprocal in two parts: the low part restores the bits the rounded reciprocal drops.
        return (ln * InverseLn10Hi) + (ln * InverseLn10Lo);
    }

    /// <summary>Returns e raised to <paramref name="x"/>, as <see cref="Math.Exp"/>.</summary>
    /// <param name="x">The exponent.</param>
    /// <returns>e<sup>x</sup>.</returns>
    public static double Exp(double x) => OperatingSystem.IsBrowser() ? Math.Exp(x) : ExpCore(x);

    /// <summary>Returns the sine and cosine of <paramref name="x"/> radians, as <see cref="Math.SinCos"/>.</summary>
    /// <param name="x">The angle in radians.</param>
    /// <returns>The sine and the cosine; NaN for an infinite or NaN angle.</returns>
    public static (double Sin, double Cos) SinCos(double x)
    {
        if (OperatingSystem.IsBrowser())
        {
            return Math.SinCos(x);
        }

        if (!double.IsFinite(x))
        {
            return (double.NaN, double.NaN);
        }

        if (Math.Abs(x) <= PiOver4)
        {
            return (KernelSin(x), KernelCos(x));
        }

        // Cody-Waite reduction by π/2 in three parts: exact products for the multiples a renderer meets (|n| < 2^20), and still
        // the same bits everywhere, if less accurate, beyond.
        double n = Math.Round(x * TwoOverPi);
        double r = ((x - (n * PiOver2Part1)) - (n * PiOver2Part2)) - (n * PiOver2Part3);
        return Quadrant(r, n);
    }

    /// <summary>Returns the sine of <paramref name="x"/> radians.</summary>
    /// <param name="x">The angle in radians.</param>
    /// <returns>The sine.</returns>
    public static double Sin(double x) => SinCos(x).Sin;

    /// <summary>Returns the cosine of <paramref name="x"/> radians.</summary>
    /// <param name="x">The angle in radians.</param>
    /// <returns>The cosine.</returns>
    public static double Cos(double x) => SinCos(x).Cos;

    /// <summary>
    /// Returns the sine and cosine of <paramref name="degrees"/> degrees, exact at every multiple of 90° (0, 1 or −1, never −0).
    /// </summary>
    /// <param name="degrees">The angle in degrees.</param>
    /// <returns>The sine and the cosine; NaN for an infinite or NaN angle.</returns>
    public static (double Sin, double Cos) SinCosDegrees(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            return (double.NaN, double.NaN);
        }

        (double r, int quadrant) = ReduceDegrees(degrees);
        if (OperatingSystem.IsBrowser())
        {
            (double sin, double cos) = Math.SinCos(r);
            return Rotate(sin, cos, quadrant);
        }

        return Rotate(KernelSin(r), KernelCos(r), quadrant);
    }

    /// <summary>Returns the sine of <paramref name="degrees"/> degrees, the same bits as <see cref="SinCosDegrees"/> gives.</summary>
    /// <param name="degrees">The angle in degrees.</param>
    /// <returns>The sine; NaN for an infinite or NaN angle.</returns>
    public static double SinDegrees(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            return double.NaN;
        }

        if (OperatingSystem.IsBrowser())
        {
            return SinCosDegrees(degrees).Sin;
        }

        (double r, int quadrant) = ReduceDegrees(degrees);

        return quadrant switch
        {
            0 => KernelSin(r) + 0.0,
            1 => KernelCos(r) + 0.0,
            2 => -KernelSin(r) + 0.0,
            _ => -KernelCos(r) + 0.0,
        };
    }

    /// <summary>Returns the cosine of <paramref name="degrees"/> degrees, the same bits as <see cref="SinCosDegrees"/> gives.</summary>
    /// <param name="degrees">The angle in degrees.</param>
    /// <returns>The cosine; NaN for an infinite or NaN angle.</returns>
    public static double CosDegrees(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            return double.NaN;
        }

        if (OperatingSystem.IsBrowser())
        {
            return SinCosDegrees(degrees).Cos;
        }

        (double r, int quadrant) = ReduceDegrees(degrees);

        return quadrant switch
        {
            0 => KernelCos(r) + 0.0,
            1 => -KernelSin(r) + 0.0,
            2 => -KernelCos(r) + 0.0,
            _ => KernelSin(r) + 0.0,
        };
    }

    /// <summary>Returns the angle whose tangent is <paramref name="y"/>/<paramref name="x"/>, as <see cref="Math.Atan2"/>.</summary>
    /// <param name="y">The y coordinate.</param>
    /// <param name="x">The x coordinate.</param>
    /// <returns>The angle in radians, in [−π, π].</returns>
    public static double Atan2(double y, double x)
    {
        if (OperatingSystem.IsBrowser())
        {
            return Math.Atan2(y, x);
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        bool yNegative = double.IsNegative(y);
        bool xNegative = double.IsNegative(x);
        if (y == 0)
        {
            // atan2(±0, +0 or x > 0) = ±0; atan2(±0, −0 or x < 0) = ±π.
            return xNegative ? (yNegative ? -Pi : Pi) : y;
        }

        if (x == 0)
        {
            return yNegative ? -PiOver2 : PiOver2;
        }

        if (double.IsInfinity(x))
        {
            double angle = double.IsInfinity(y) ? (xNegative ? 3 * PiOver4 : PiOver4) : (xNegative ? Pi : 0);
            return yNegative ? -angle : angle;
        }

        if (double.IsInfinity(y))
        {
            return yNegative ? -PiOver2 : PiOver2;
        }

        int exponentDifference = Exponent(y) - Exponent(x);
        double z;
        if (exponentDifference > 60)
        {
            z = PiOver2 + (0.5 * PiLo);
        }
        else if (xNegative && exponentDifference < -60)
        {
            z = 0;
        }
        else
        {
            z = AtanCore(Math.Abs(y / x));
        }

        return (yNegative, xNegative) switch
        {
            (false, false) => z,
            (true, false) => -z,
            (false, true) => Pi - (z - PiLo),
            (true, true) => (z - PiLo) - Pi,
        };
    }

    /// <summary>Returns the angle whose cosine is <paramref name="x"/>, as <see cref="Math.Acos"/>.</summary>
    /// <param name="x">The cosine, in [−1, 1].</param>
    /// <returns>The angle in radians, in [0, π]; NaN outside [−1, 1].</returns>
    public static double Acos(double x)
    {
        if (OperatingSystem.IsBrowser())
        {
            return Math.Acos(x);
        }

        if (!(Math.Abs(x) <= 1))
        {
            return double.NaN;
        }

        // sin = √((1 − x)(1 + x)): 1 − x is exact near 1, where 1 − x² would lose the bits that matter.
        return Atan2(Math.Sqrt((1 - x) * (1 + x)), x);
    }

    /// <summary>Returns the cube root of <paramref name="x"/>, as <see cref="Math.Cbrt"/>.</summary>
    /// <param name="x">The value.</param>
    /// <returns>∛x, with the sign of <paramref name="x"/>.</returns>
    public static double Cbrt(double x)
    {
        if (OperatingSystem.IsBrowser())
        {
            return Math.Cbrt(x);
        }

        if (x == 0 || !double.IsFinite(x))
        {
            return x;
        }

        double a = Math.Abs(x);
        int exponent = Exponent(a);
        if (exponent == -1023)
        {
            // A subnormal: ∛(x·2^54) = ∛x·2^18, exact scalings both.
            return Math.ScaleB(Cbrt(x * Two54), -18);
        }

        // fdlibm s_cbrt.c: a 5-bit guess from dividing the exponent field by 3, a polynomial step to 23 bits, rounded to 22 bits
        // so that t·t is exact, then one Newton step to 53 bits.
        ulong high = (ulong)BitConverter.DoubleToInt64Bits(a) >> 32;
        double t = BitConverter.Int64BitsToDouble((long)(((high / 3) + CbrtB1) << 32));
        double r = t * t * (t / a);
        t *= (CbrtP0 + (r * (CbrtP1 + (r * CbrtP2)))) + (r * r * r * (CbrtP3 + (r * CbrtP4)));
        t = BitConverter.Int64BitsToDouble((long)(((ulong)BitConverter.DoubleToInt64Bits(t) + 0x8000_0000UL) & 0xFFFF_FFFF_C000_0000UL));
        double s = t * t;
        r = a / s;
        double w = t + t;
        r = (r - t) / (w + r);
        t += t * r;
        return x < 0 ? -t : t;
    }

    private static double PowSpecial(double x, double y)
    {
        // IEEE 754 §9.2.1, for a zero or infinite base or an infinite exponent (x ≠ 1, y ≠ 0, neither NaN).
        if (double.IsInfinity(y))
        {
            double magnitude = Math.Abs(x);
            if (magnitude == 1)
            {
                return 1;
            }

            return (magnitude < 1) == (y < 0) ? double.PositiveInfinity : 0;
        }

        bool odd = IsOddInteger(y);
        if (x == 0)
        {
            bool negativeZero = double.IsNegative(x) && odd;
            return y < 0 ? (negativeZero ? double.NegativeInfinity : double.PositiveInfinity) : (negativeZero ? -0.0 : 0);
        }

        // x is ±∞.
        if (x > 0)
        {
            return y < 0 ? 0 : double.PositiveInfinity;
        }

        return y < 0 ? (odd ? -0.0 : 0) : (odd ? double.NegativeInfinity : double.PositiveInfinity);
    }

    private static bool IsOddInteger(double y) => double.IsInteger(y) && Math.Abs(y) < 9007199254740992.0 && Math.Abs(y % 2) == 1;

    private static double MultipliedPower(double x, int n)
    {
        // Binary powering: exact while every partial product is representable.
        uint remaining = (uint)Math.Abs(n);
        double result = 1;
        double square = x;
        while (remaining != 0)
        {
            if ((remaining & 1) != 0)
            {
                result *= square;
            }

            remaining >>= 1;
            if (remaining != 0)
            {
                square *= square;
            }
        }

        return n < 0 ? 1 / result : result;
    }

    /// <summary>x^y for a positive finite x and a finite nonzero y: exp(y·log x).</summary>
    /// <remarks>
    /// log x comes out within about an ulp, so the result is within about |y·ln x| + 1 ulps, the error budget of the composed
    /// exp(y·log x) this replaces; carrying log x in two parts (as ARM's routine does) would reach half an ulp at a third more time.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double PowCore(double x, double y)
    {
        ulong ix = (ulong)BitConverter.DoubleToInt64Bits(x);
        if (ix < 0x0010_0000_0000_0000UL)
        {
            // A subnormal: scale it up exactly and take the scaling back out of the exponent field (wrapping is intended).
            ix = unchecked((ulong)BitConverter.DoubleToInt64Bits(x * Two52) - (52UL << 52));
        }

        // x = 2^k·z with z in [0x1.69555p-1, 0x1.69555p0); i picks z's subinterval.
        ulong tmp = unchecked(ix - PowLogOffset);
        int i = (int)((tmp >> (52 - PowTableBits)) & (PowTableSize - 1));
        double kd = (long)tmp >> 52;
        ulong iz = unchecked(ix - (tmp & (0xFFFUL << 52)));
        double z = BitConverter.UInt64BitsToDouble(iz);

        // i < 128 by construction, so the three reads stay inside the 384-entry table.
        ref double entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(PowLogTable), 3 * i);
        double invc = entry;

        // r = z/c − 1 nearly exactly: zhi has 21 significant bits and 1/c at most 9, so zhi/c − 1 is exact.
        double zhi = BitConverter.UInt64BitsToDouble(unchecked(iz + (1UL << 31)) & (ulong.MaxValue << 32));
        double r = ((zhi * invc) - 1.0) + ((z - zhi) * invc);

        // log x = k·ln 2 + log(c) + log1p(r); k·ln2hi + log(c) is exact, and the small terms are summed before they meet it.
        double r2 = r * r;
        double p = r2 * ((PowLogA2 + (r * PowLogA3)) + (r2 * ((PowLogA4 + (r * PowLogA5)) + (r2 * ((PowLogA6 + (r * PowLogA7)) + (r2 * PowLogA8))))));
        double log = ((kd * PowLn2Hi) + Unsafe.Add(ref entry, 1)) + (r + (p + ((kd * PowLn2Lo) + Unsafe.Add(ref entry, 2))));
        return PowExp(y * log);
    }

    /// <summary>exp(x), table-driven.</summary>
    private static double PowExp(double x)
    {
        uint abstop = (uint)(BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF;
        bool large = false;
        if (abstop - 0x3C9 >= 0x408 - 0x3C9)
        {
            if (abstop < 0x3C9)
            {
                // |x| < 2^-54.
                return 1.0 + x;
            }

            if (abstop >= 0x409)
            {
                // |x| >= 1024, infinities included (x is never NaN).
                return x < 0 ? 0 : double.PositiveInfinity;
            }

            large = true;
        }

        // x = k·ln 2/128 + r, |r| <= ln 2/256; adding 1.5·2^52 rounds k into the low bits.
        double kd = (PowExpInverseLn2N * x) + PowExpShift;
        ulong ki = (ulong)BitConverter.DoubleToInt64Bits(kd);
        kd -= PowExpShift;
        double r = (x - (kd * PowExpLn2HiN)) - (kd * PowExpLn2LoN);

        // 2^(k/128) ≈ scale·(1 + tail); the index is below 128 by the mask, so both reads stay inside the 256-entry table.
        ref ulong entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(ExpTable), 2 * (int)(ki & (PowTableSize - 1)));
        double tail = BitConverter.UInt64BitsToDouble(entry);
        ulong sbits = unchecked(Unsafe.Add(ref entry, 1) + (ki << (52 - PowTableBits)));
        double r2 = r * r;
        double tmp = tail + r + (r2 * (PowExpC2 + (r * PowExpC3))) + (r2 * r2 * (PowExpC4 + (r * PowExpC5)));
        if (large)
        {
            return PowExpScaled(tmp, sbits, ki);
        }

        double scale = BitConverter.UInt64BitsToDouble(sbits);
        return scale + (scale * tmp);
    }

    /// <summary>scale·(1 + tmp) when the exponent of scale may have left the normal range (512 ≤ |x| &lt; 1024).</summary>
    private static double PowExpScaled(double tmp, ulong sbits, ulong ki)
    {
        double scale;
        if ((ki & 0x8000_0000) == 0)
        {
            // k > 0: the exponent may have overflowed by up to 460.
            scale = BitConverter.UInt64BitsToDouble(unchecked(sbits - (1009UL << 52)));
            return Two1009 * (scale + (scale * tmp));
        }

        // k < 0: round to the final precision before scaling into the subnormal range, so the result is rounded once.
        scale = BitConverter.UInt64BitsToDouble(unchecked(sbits + (1022UL << 52)));
        double y = scale + (scale * tmp);
        if (y < 1.0)
        {
            double lo = (scale - y) + (scale * tmp);
            double hi = 1.0 + y;
            lo = ((1.0 - hi) + y) + lo;
            y = (hi + lo) - 1.0;
        }

        return TwoMinus1022 * y;
    }

    private static double LogCore(double x)
    {
        if (!(x > 0))
        {
            return x == 0 ? double.NegativeInfinity : double.NaN;
        }

        if (double.IsPositiveInfinity(x))
        {
            return x;
        }

        int k = 0;
        if (Exponent(x) == -1023)
        {
            x *= Two54;
            k = -54;
        }

        // x = 2^k·m with m in [√2/2, √2).
        long bits = BitConverter.DoubleToInt64Bits(x);
        k += (int)((bits >> 52) & 0x7FF) - 1023;
        double m = BitConverter.Int64BitsToDouble((bits & 0x000F_FFFF_FFFF_FFFFL) | 0x3FF0_0000_0000_0000L);
        if (m > Sqrt2)
        {
            m *= 0.5;
            k++;
        }

        double f = m - 1;
        double s = f / (2 + f);
        double z = s * s;
        double w = z * z;
        double t1 = w * (Lg2 + (w * (Lg4 + (w * Lg6))));
        double t2 = z * (Lg1 + (w * (Lg3 + (w * (Lg5 + (w * Lg7))))));
        double r = t2 + t1;
        double halfSquare = 0.5 * f * f;
        double dk = k;
        return (dk * Ln2Hi) - ((halfSquare - ((s * (halfSquare + r)) + (dk * Ln2Lo))) - f);
    }

    private static double ExpCore(double x)
    {
        if (double.IsNaN(x))
        {
            return x;
        }

        if (x > ExpOverflow)
        {
            return double.PositiveInfinity;
        }

        if (x < ExpUnderflow)
        {
            return 0;
        }

        if (Math.Abs(x) < 1.0 / (1L << 28))
        {
            return 1 + x;
        }

        // x = k·ln 2 + r, |r| <= ln2/2, with ln 2 in two parts so hi is exact.
        double k = Math.Round(x * InverseLn2);
        double hi = x - (k * Ln2Hi);
        double lo = k * Ln2Lo;
        double r = hi - lo;
        double z = r * r;
        double c = r - (z * (P1 + (z * (P2 + (z * (P3 + (z * (P4 + (z * P5)))))))));
        double y = 1 - ((lo - ((r * c) / (2 - c))) - hi);
        return Math.ScaleB(y, (int)k);
    }

    private static double AtanCore(double x)
    {
        // x >= 0, finite.
        double hi;
        double lo;
        if (x < 0.4375)
        {
            if (x < 1.0 / (1L << 27))
            {
                return x;
            }

            return x - (x * AtanSeries(x));
        }

        if (x < 1.1875)
        {
            if (x < 0.6875)
            {
                (hi, lo) = (AtanHalfHi, AtanHalfLo);
                x = ((2 * x) - 1) / (2 + x);
            }
            else
            {
                (hi, lo) = (AtanOneHi, AtanOneLo);
                x = (x - 1) / (x + 1);
            }
        }
        else if (x < 2.4375)
        {
            (hi, lo) = (AtanThreeHalvesHi, AtanThreeHalvesLo);
            x = (x - 1.5) / (1 + (1.5 * x));
        }
        else
        {
            if (x >= 7.3786976294838206464e19)
            {
                return AtanInfinityHi + AtanInfinityLo;
            }

            (hi, lo) = (AtanInfinityHi, AtanInfinityLo);
            x = -1 / x;
        }

        return hi - (((x * AtanSeries(x)) - lo) - x);
    }

    private static double AtanSeries(double x)
    {
        double z = x * x;
        double w = z * z;
        double s1 = z * (AT0 + (w * (AT2 + (w * (AT4 + (w * (AT6 + (w * (AT8 + (w * AT10))))))))));
        double s2 = w * (AT1 + (w * (AT3 + (w * (AT5 + (w * (AT7 + (w * AT9))))))));
        return s1 + s2;
    }

    private static double KernelSin(double x)
    {
        double z = x * x;
        double v = z * x;
        double r = S2 + (z * (S3 + (z * (S4 + (z * (S5 + (z * S6)))))));
        return x + (v * (S1 + (z * r)));
    }

    private static double KernelCos(double x)
    {
        double z = x * x;
        double r = z * (C1 + (z * (C2 + (z * (C3 + (z * (C4 + (z * (C5 + (z * C6))))))))));
        double half = 0.5 * z;
        double w = 1 - half;
        return w + (((1 - w) - half) + (z * r));
    }

    private static (double Sin, double Cos) Quadrant(double r, double n) => Rotate(KernelSin(r), KernelCos(r), QuadrantOf(n));

    /// <summary>
    /// A finite angle in degrees as r radians plus a quadrant q, the angle being r + q·π/2 (mod 2π) with |r| ≤ π/4.
    /// </summary>
    private static (double R, int Quadrant) ReduceDegrees(double degrees)
    {
        // The remainder is exact, and so is subtracting the nearest multiple of 90 from it (Sterbenz), so only the conversion to
        // radians rounds. Below 360 the remainder is the angle itself, so the library call is skipped.
        double reduced = Math.Abs(degrees) < 360 ? degrees : degrees % 360;
        double n = Math.Round(reduced / 90);
        return ((reduced - (90 * n)) * DegreesToRadians, QuadrantOf(n));
    }

    /// <summary>n mod 4 for an integral n, in 0..3; every double of magnitude 2^54 or more is a multiple of 4.</summary>
    private static int QuadrantOf(double n) => Math.Abs(n) < 18014398509481984.0 ? (int)((long)n & 3) : 0;

    /// <summary>The sine and cosine of r + quadrant·π/2 from those of r; adding 0 turns a −0 into +0.</summary>
    private static (double Sin, double Cos) Rotate(double sin, double cos, int quadrant)
    {
        return quadrant switch
        {
            0 => (sin + 0.0, cos + 0.0),
            1 => (cos + 0.0, -sin + 0.0),
            2 => (-sin + 0.0, -cos + 0.0),
            _ => (-cos + 0.0, sin + 0.0),
        };
    }

    /// <summary>The pow logarithm table: 1/c, log(c) rounded to a multiple of 2^-43 (so k·ln 2 + log(c) is exact), and the rest of
    /// log(c), for each of the 128 subintervals.</summary>
    private static ReadOnlySpan<double> PowLogTable =>
    [
        1.4140625, -0.3464667673462145, 5.929407345889625e-15,
        1.40625, -0.34092658697056777, -2.544157440035963e-14,
        1.3984375, -0.3353555419211034, -3.443525940775045e-14,
        1.390625, -0.3297532863724655, -2.500123826022799e-15,
        1.3828125, -0.32411946865420305, -8.929337133850617e-15,
        1.375, -0.31845373111855224, 1.7625431312172662e-14,
        1.3671875, -0.31275571000389846, 1.5688303180062087e-15,
        1.359375, -0.3070250352949415, 2.9655274673691784e-14,
        1.3515625, -0.3012613305781997, 3.7923164802093147e-14,
        1.34375, -0.2954642128938758, 3.993416384387844e-14,
        1.3359375, -0.28963329258306203, 1.9352855826489123e-14,
        1.3359375, -0.28963329258306203, 1.9352855826489123e-14,
        1.328125, -0.28376817313062475, -1.9852665484979036e-14,
        1.3203125, -0.27786845100342816, -2.814323765595281e-14,
        1.3125, -0.2719337154836694, 2.7643769993528702e-14,
        1.3046875, -0.2659635484970977, -4.025092402293806e-14,
        1.296875, -0.25995752443691345, -1.2621729398885316e-14,
        1.2890625, -0.25391520998095984, -3.600176732637335e-15,
        1.2890625, -0.25391520998095984, -3.600176732637335e-15,
        1.28125, -0.2478361639045943, 1.3029797173308663e-14,
        1.2734375, -0.2417199368871934, 4.8230289429940886e-14,
        1.265625, -0.23556607131274632, -2.0592242769647135e-14,
        1.2578125, -0.22937410106487732, 3.149265065191484e-14,
        1.25, -0.22314355131425145, 4.169796584527195e-14,
        1.25, -0.22314355131425145, 4.169796584527195e-14,
        1.2421875, -0.21687393830063684, 2.2477465222466186e-14,
        1.234375, -0.21056476910735, 3.6507188831790577e-16,
        1.2265625, -0.2042155414286526, -3.827767260205414e-14,
        1.2265625, -0.2042155414286526, -3.827767260205414e-14,
        1.21875, -0.19782574332987224, -4.7641388950792196e-14,
        1.2109375, -0.19139485299967873, 4.9278276214647115e-14,
        1.203125, -0.18492233849406148, 4.9485167661250996e-14,
        1.203125, -0.18492233849406148, 4.9485167661250996e-14,
        1.1953125, -0.1784076574728033, -1.5003333854266542e-14,
        1.1875, -0.17185025692663203, -2.7194441649495324e-14,
        1.1875, -0.17185025692663203, -2.7194441649495324e-14,
        1.1796875, -0.1652495728952772, -2.99659267292569e-14,
        1.171875, -0.15860503017665906, 2.0472357800461955e-14,
        1.171875, -0.15860503017665906, 2.0472357800461955e-14,
        1.1640625, -0.15191604202584585, 3.879296723063646e-15,
        1.15625, -0.1451820098444614, -3.6506824353335045e-14,
        1.1484375, -0.13840232285906495, -5.4183331379008994e-14,
        1.1484375, -0.13840232285906495, -5.4183331379008994e-14,
        1.140625, -0.131576357788731, 1.1729485484531301e-14,
        1.140625, -0.131576357788731, 1.1729485484531301e-14,
        1.1328125, -0.12470347850091912, -3.811763084710266e-14,
        1.125, -0.11778303565643, 4.654729747598445e-14,
        1.125, -0.11778303565643, 4.654729747598445e-14,
        1.1171875, -0.11081436634026431, -2.5799991283069902e-14,
        1.109375, -0.10379679368168127, 3.7700471749674615e-14,
        1.109375, -0.10379679368168127, 3.7700471749674615e-14,
        1.1015625, -0.09672962645856842, 1.7306161136093256e-14,
        1.1015625, -0.09672962645856842, 1.7306161136093256e-14,
        1.09375, -0.089612158689647, -4.012913552726574e-14,
        1.0859375, -0.08244366921110213, 2.7541708360737882e-14,
        1.0859375, -0.08244366921110213, 2.7541708360737882e-14,
        1.078125, -0.07522342123763792, 5.0396178134370583e-14,
        1.078125, -0.07522342123763792, 5.0396178134370583e-14,
        1.0703125, -0.06795066190852594, 1.8195060030168815e-14,
        1.0625, -0.06062462181648698, 5.213620639136504e-14,
        1.0625, -0.06062462181648698, 5.213620639136504e-14,
        1.0546875, -0.053244514518837605, 2.532168943117445e-14,
        1.0546875, -0.053244514518837605, 2.532168943117445e-14,
        1.046875, -0.045809536031242715, -5.148849572685811e-14,
        1.046875, -0.045809536031242715, -5.148849572685811e-14,
        1.0390625, -0.038318864302141264, 4.6652946995830086e-15,
        1.0390625, -0.038318864302141264, 4.6652946995830086e-15,
        1.03125, -0.03077165866670839, -4.529814257790929e-14,
        1.03125, -0.03077165866670839, -4.529814257790929e-14,
        1.0234375, -0.023167059281490765, -4.361324067851568e-14,
        1.015625, -0.015504186535963527, -1.7274567499706107e-15,
        1.015625, -0.015504186535963527, -1.7274567499706107e-15,
        1.0078125, -0.0077821404420319595, -2.298941004620351e-14,
        1.0078125, -0.0077821404420319595, -2.298941004620351e-14,
        1.0, 0.0, 0.0,
        1.0, 0.0, 0.0,
        0.9921875, 0.007843177461040796, -1.4902732911301337e-14,
        0.984375, 0.01574835696817445, -3.527980389655325e-14,
        0.9765625, 0.023716526617363343, -4.730054772033249e-14,
        0.96875, 0.03174869831457272, 7.580310369375161e-15,
        0.9609375, 0.039845908547249564, -4.9893776716773285e-14,
        0.953125, 0.048009219186383234, -2.262629393030674e-14,
        0.9453125, 0.056239718322899535, -2.345674491018699e-14,
        0.94140625, 0.06038051098892083, -1.3352588834854848e-14,
        0.93359375, 0.06871389254808946, -3.765296820388875e-14,
        0.92578125, 0.07711730334438016, 5.1128335719851986e-14,
        0.91796875, 0.08559193033545398, -5.046674438470119e-14,
        0.9140625, 0.08985632912185793, 3.1218748807418837e-15,
        0.90625, 0.09844007281321865, 3.3871241029241416e-14,
        0.8984375, 0.10709813555638448, -1.7376727386423858e-14,
        0.89453125, 0.11145544092528326, 3.957125899799804e-14,
        0.88671875, 0.12022742699821265, -5.2849453521890294e-14,
        0.8828125, 0.12464244520731427, -3.767012502308738e-14,
        0.875, 0.13353139262449076, 3.1859736349078334e-14,
        0.87109375, 0.13800567301939282, 5.0900642926060466e-14,
        0.86328125, 0.14701474296180095, 8.710783796122478e-15,
        0.859375, 0.15154989812720032, 6.157896229122976e-16,
        0.8515625, 0.16068238169043525, 3.821577743916796e-14,
        0.84765625, 0.16528009093906348, 3.9440046718453496e-14,
        0.83984375, 0.17453941635187675, 2.2924522154618074e-14,
        0.8359375, 0.17920142945774842, -3.742530094732263e-14,
        0.83203125, 0.18388527877016259, -2.5223102140407338e-14,
        0.82421875, 0.1933193110035063, -1.0320443688698849e-14,
        0.8203125, 0.19806991376208316, 1.0634128304268335e-14,
        0.8125, 0.20763936477828793, -4.3425422595242564e-14,
        0.80859375, 0.21245865121420593, -1.2527395755711364e-14,
        0.8046875, 0.21730127569003344, -5.204008743405884e-14,
        0.80078125, 0.22216746534115828, -3.979844515951702e-15,
        0.79296875, 0.2319714654378231, -4.7955860343296286e-14,
        0.7890625, 0.2369097470783572, 5.015686013791602e-16,
        0.78515625, 0.24187253642048745, -7.252318953240293e-16,
        0.78125, 0.2468600779315011, 2.4688324156011588e-14,
        0.7734375, 0.2569104137850218, 5.465121253624792e-15,
        0.76953125, 0.26197371574153294, 4.102651071698446e-14,
        0.765625, 0.2670627852490952, -4.996736502345936e-14,
        0.76171875, 0.27217788591576664, 4.903580708156347e-14,
        0.7578125, 0.27731928541618345, 5.089628039500759e-14,
        0.75390625, 0.28248725557466514, 1.1782016386565151e-14,
        0.74609375, 0.29290401643288533, 4.727452940514406e-14,
        0.7421875, 0.29815337231912054, -4.4204083338755686e-14,
        0.73828125, 0.3034304294199046, 1.548345993498083e-14,
        0.734375, 0.30873548164959175, 2.1522127491642888e-14,
        0.73046875, 0.3140688276249648, 1.1054030169005386e-14,
        0.7265625, 0.31943077076641657, -5.534326352070679e-14,
        0.72265625, 0.3248216194012912, -5.351646604259541e-14,
        0.71875, 0.33024168687052224, 5.4612144489920215e-14,
        0.71484375, 0.3356912916381134, 2.8136969901227338e-14,
        0.7109375, 0.3411707574027787, -1.156568624616423e-14,
    ];

    /// <summary>The exp table: for each i in 0..127, the bits of 2^(i/128) − round(2^(i/128)) and those of round(2^(i/128)) less
    /// i·2^45, so adding k·2^45 for k = 128·e + i gives 2^(k/128) rounded.</summary>
    private static ReadOnlySpan<ulong> ExpTable =>
    [
        0x0000000000000000, 0x3FF0000000000000,
        0x3C9B61299AB8CDB7, 0x3FEFF63DA9FB3335,
        0xBC719083535B085D, 0x3FEFEC9A3E778061,
        0xBC90A31C1977C96E, 0x3FEFE315E86E7F85,
        0x3C8D73E2A475B465, 0x3FEFD9B0D3158574,
        0xBC8C91DFE2B13C27, 0x3FEFD06B29DDF6DE,
        0x3C6186BE4BB284FF, 0x3FEFC74518759BC8,
        0x3C91487818316136, 0x3FEFBE3ECAC6F383,
        0x3C98A62E4ADC610B, 0x3FEFB5586CF9890F,
        0x3C901EDC16E24F71, 0x3FEFAC922B7247F7,
        0x3C403A1727C57B53, 0x3FEFA3EC32D3D1A2,
        0xBC6B9BEDC44EBD7B, 0x3FEF9B66AFFED31B,
        0xBC96C51039449B3A, 0x3FEF9301D0125B51,
        0xBC51B514B36CA5C7, 0x3FEF8ABDC06C31CC,
        0xBC932FBF9AF1369E, 0x3FEF829AAEA92DE0,
        0x3C82406AB9EEAB0A, 0x3FEF7A98C8A58E51,
        0xBC819041B9D78A76, 0x3FEF72B83C7D517B,
        0xBC911023D1970F6C, 0x3FEF6AF9388C8DEA,
        0x3C8E5B4C7B4968E4, 0x3FEF635BEB6FCB75,
        0xBC995386352EF607, 0x3FEF5BE084045CD4,
        0x3C9E016E00A2643C, 0x3FEF54873168B9AA,
        0xBC91DF98027BB78C, 0x3FEF4D5022FCD91D,
        0x3C8DC775814A8495, 0x3FEF463B88628CD6,
        0x3C82A97E9494A5EE, 0x3FEF3F49917DDC96,
        0x3C99B07EB6C70573, 0x3FEF387A6E756238,
        0x3C8AC155BEF4F4A4, 0x3FEF31CE4FB2A63F,
        0x3C82BD339940E9D9, 0x3FEF2B4565E27CDD,
        0xBC9A4C3A8C3F0D7E, 0x3FEF24DFE1F56381,
        0x3C8612E8AFAD1255, 0x3FEF1E9DF51FDEE1,
        0xBC410ADCD6381AA4, 0x3FEF187FD0DAD990,
        0x3C90024754DB41D5, 0x3FEF1285A6E4030B,
        0x3C71CA0F45D52383, 0x3FEF0CAFA93E2F56,
        0x3C86F46AD23182E4, 0x3FEF06FE0A31B715,
        0x3C8A9CE78E18047C, 0x3FEF0170FC4CD831,
        0x3C932721843659A6, 0x3FEEFC08B26416FF,
        0xBC8B5CEE5C4E4628, 0x3FEEF6C55F929FF1,
        0xBC963AEABF42EAE2, 0x3FEEF1A7373AA9CB,
        0xBC9E958D3C9904BD, 0x3FEEECAE6D05D866,
        0xBC75E436D661F5E3, 0x3FEEE7DB34E59FF7,
        0xBC9EFFF8375D29C3, 0x3FEEE32DC313A8E5,
        0x3C8ADA0911F09EBC, 0x3FEEDEA64C123422,
        0xBC97D023F956F9F3, 0x3FEEDA4504AC801C,
        0xBC5EF3691C309278, 0x3FEED60A21F72E2A,
        0xBC81C7DDE35F7999, 0x3FEED1F5D950A897,
        0x3C489B7A04EF80D0, 0x3FEECE086061892D,
        0x3C9C944BD1648A76, 0x3FEECA41ED1D0057,
        0x3C73C1A3B69062F0, 0x3FEEC6A2B5C13CD0,
        0x3C99CB62F3D1BE56, 0x3FEEC32AF0D7D3DE,
        0x3C7D4397AFEC42E2, 0x3FEEBFDAD5362A27,
        0x3C98ECDBBC6A7833, 0x3FEEBCB299FDDD0D,
        0xBC94B309D25957E3, 0x3FEEB9B2769D2CA7,
        0xBC8F768569BD93EF, 0x3FEEB6DAA2CF6642,
        0xBC807ABE1DB13CAD, 0x3FEEB42B569D4F82,
        0xBC8D689CEFEDE59B, 0x3FEEB1A4CA5D920F,
        0x3C99BB2C011D93AD, 0x3FEEAF4736B527DA,
        0x3C8295E15B9A1DE8, 0x3FEEAD12D497C7FD,
        0x3C96324C054647AD, 0x3FEEAB07DD485429,
        0x3C3C4B1B816986A2, 0x3FEEA9268A5946B7,
        0x3C9BA6F93080E65E, 0x3FEEA76F15AD2148,
        0xBC93E2429B56DE47, 0x3FEEA5E1B976DC09,
        0xBC9383C17E40B497, 0x3FEEA47EB03A5585,
        0xBC8C483C759D8933, 0x3FEEA34634CCC320,
        0xBC9BB60987591C34, 0x3FEEA23882552225,
        0x3C6038AE44F73E65, 0x3FEEA155D44CA973,
        0xBC9BDD3413B26456, 0x3FEEA09E667F3BCD,
        0xBC72895667FF0B0D, 0x3FEEA012750BDABF,
        0xBC6BBE3A683C88AB, 0x3FEE9FB23C651A2F,
        0xBC883C0F25860EF6, 0x3FEE9F7DF9519484,
        0xBC816E4786887A99, 0x3FEE9F75E8EC5F74,
        0xBC90A8D96C65D53C, 0x3FEE9F9A48A58174,
        0xBC90245957316DD3, 0x3FEE9FEB564267C9,
        0x3C9866B80A02162D, 0x3FEEA0694FDE5D3F,
        0xBC841577EE04992F, 0x3FEEA11473EB0187,
        0x3C9F124CD1164DD6, 0x3FEEA1ED0130C132,
        0x3C705D02BA15797E, 0x3FEEA2F336CF4E62,
        0xBC927C86626D972B, 0x3FEEA427543E1A12,
        0xBC9D4C1DD41532D8, 0x3FEEA589994CCE13,
        0xBC88D684A341CDFB, 0x3FEEA71A4623C7AD,
        0xBC9FC6F89BD4F6BA, 0x3FEEA8D99B4492ED,
        0x3C9994C2F37CB53A, 0x3FEEAAC7D98A6699,
        0x3C96E9F156864B27, 0x3FEEACE5422AA0DB,
        0xBC70D55E32E9E3AA, 0x3FEEAF3216B5448C,
        0x3C85CC13A2E3976C, 0x3FEEB1AE99157736,
        0xBC9DD6792E582524, 0x3FEEB45B0B91FFC6,
        0xBC675FC781B57EBC, 0x3FEEB737B0CDC5E5,
        0xBC764B7C96A5F039, 0x3FEEBA44CBC8520F,
        0xBC9D185B7C1B85D1, 0x3FEEBD829FDE4E50,
        0xBC9173BD91CEE632, 0x3FEEC0F170CA07BA,
        0x3C7C7C46B071F2BE, 0x3FEEC49182A3F090,
        0x3C7824CA78E64C6E, 0x3FEEC86319E32323,
        0xBC9359495D1CD533, 0x3FEECC667B5DE565,
        0x3C96305C7DDC36AB, 0x3FEED09BEC4A2D33,
        0xBC9D2F6EDB8D41E1, 0x3FEED503B23E255D,
        0x3C9BCB7ECAC563C7, 0x3FEED99E1330B358,
        0x3C90FAC90EF7FD31, 0x3FEEDE6B5579FDBF,
        0xBC8F9234CAE76CD0, 0x3FEEE36BBFD3F37A,
        0x3C97A1CD345DCC81, 0x3FEEE89F995AD3AD,
        0xBC9BDEF54C80E425, 0x3FEEEE07298DB666,
        0xBC62805E3084D708, 0x3FEEF3A2B84F15FB,
        0xBC9C71DFBBBA6DE3, 0x3FEEF9728DE5593A,
        0xBC75584F7E54AC3B, 0x3FEEFF76F2FB5E47,
        0xBC9EFCD30E54292E, 0x3FEF05B030A1064A,
        0x3C823DD07A2D9E84, 0x3FEF0C1E904BC1D2,
        0xBC9EFDCA3F6B9C73, 0x3FEF12C25BD71E09,
        0x3C811065895048DD, 0x3FEF199BDD85529C,
        0x3C9B4537E083C60A, 0x3FEF20AB5FFFD07A,
        0x3C92884DFF483CAD, 0x3FEF27F12E57D14B,
        0x3C71ACBC48805C44, 0x3FEF2F6D9406E7B5,
        0x3C7503CBD1E949DB, 0x3FEF3720DCEF9069,
        0xBC8DD83B53829D72, 0x3FEF3F0B555DC3FA,
        0xBC9CBC3743797A9C, 0x3FEF472D4A07897C,
        0xBC9D487B719D8578, 0x3FEF4F87080D89F2,
        0x3C82ED02D75B3707, 0x3FEF5818DCFBA487,
        0xBC911EC18BEDDFE8, 0x3FEF60E316C98398,
        0x3C9C2300696DB532, 0x3FEF69E603DB3285,
        0x3C92DA5778F018C3, 0x3FEF7321F301B460,
        0xBC91A5CD4F184B5C, 0x3FEF7C97337B9B5F,
        0xBC97B627817A1496, 0x3FEF864614F5A129,
        0x3C839E8980A9CC8F, 0x3FEF902EE78B3FF6,
        0x3C92D522CA0C8DE2, 0x3FEF9A51FBC74C83,
        0xBC9E9C23179C2893, 0x3FEFA4AFA2A490DA,
        0xBC9C93F3B411AD8C, 0x3FEFAF482D8E67F1,
        0x3C9DC7F486A4B6B0, 0x3FEFBA1BEE615A27,
        0x3C93A1A5BF0D8E43, 0x3FEFC52B376BBA97,
        0x3C99D3E12DD8A18B, 0x3FEFD0765B6E4540,
        0xBC9DBB12D006350A, 0x3FEFDBFDAD9CBE14,
        0x3C874853F3A5931E, 0x3FEFE7C1819E90D8,
        0x3C62EB74966579E7, 0x3FEFF3C22B8F71F1,
    ];

    /// <summary>The unbiased binary exponent of a finite value; −1023 for zero and the subnormals.</summary>
    private static int Exponent(double x) => (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF) - 1023;
}
