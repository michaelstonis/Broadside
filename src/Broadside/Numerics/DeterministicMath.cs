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
/// Their polynomials and reductions are those of fdlibm (the FreeBSD <c>msun</c> library); errors are a few units in the last place,
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

            double magnitude = ExpCore(y * LogCore(-x));
            return IsOddInteger(y) ? -magnitude : magnitude;
        }

        return ExpCore(y * LogCore(x));
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

        // The remainder is exact, and so is subtracting the nearest multiple of 90 from it (Sterbenz), so only the conversion to
        // radians rounds.
        double reduced = degrees % 360;
        double n = Math.Round(reduced / 90);
        double r = (reduced - (90 * n)) * DegreesToRadians;
        if (OperatingSystem.IsBrowser())
        {
            (double sin, double cos) = Math.SinCos(r);
            return Rotate(sin, cos, n);
        }

        return Rotate(KernelSin(r), KernelCos(r), n);
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

    private static (double Sin, double Cos) Quadrant(double r, double n) => Rotate(KernelSin(r), KernelCos(r), n);

    /// <summary>The sine and cosine of r + n·π/2 from those of r; adding 0 turns a −0 into +0.</summary>
    private static (double Sin, double Cos) Rotate(double sin, double cos, double n)
    {
        long quadrant = (long)(n % 4);
        if (quadrant < 0)
        {
            quadrant += 4;
        }

        return quadrant switch
        {
            0 => (sin + 0.0, cos + 0.0),
            1 => (cos + 0.0, -sin + 0.0),
            2 => (-sin + 0.0, -cos + 0.0),
            _ => (-cos + 0.0, sin + 0.0),
        };
    }

    /// <summary>The unbiased binary exponent of a finite value; −1023 for zero and the subnormals.</summary>
    private static int Exponent(double x) => (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF) - 1023;
}
