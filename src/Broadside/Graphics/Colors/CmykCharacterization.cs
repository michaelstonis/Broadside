namespace Broadside.Graphics.Colors;

/// <summary>
/// DeviceCMYK to sRGB through a cubic polynomial per channel fitted to the CGATS TR 001 (SWOP) characterisation data: what
/// <see cref="CmykConversion.Characterized"/> uses until the ICC engine (Phase 3) converts through a real CMYK profile.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §10.4.2.1 allows any conversion where the formulas of §10.4.2.4 and §10.4.2.5 are "crude approximations". The
/// coefficients come from <c>tools/CmykFit/fit.py</c>: the 928 IT8.7/3 patches of ANSI CGATS TR 001-1995 (TR001CLR.AVG, CIELAB
/// D50), media-relative to the paper, Bradford-adapted to D65, encoded as sRGB, clipped, then a least-squares fit of 1 plus the 34
/// monomials c^i m^j y^l k^n of degree 1 to 3 per channel. The constant 1 keeps 0 0 0 0 exactly white. Fit error: RMS 3.8, maximum
/// 59 (in a cyan region where sRGB clips) in 8-bit units.
/// </para>
/// <para>
/// No black point compensation is built in: 0 0 0 1 is the measured SWOP black, about (54, 53, 54) in 8-bit sRGB.
/// </para>
/// </remarks>
internal static class CmykCharacterization
{
    // Term order after the constant 1 (exponents of c, m, y, k):
    // k, y, m, c, k², yk, y², mk, my, m², ck, cy, cm, c², k³, yk², y²k, y³, mk², myk, my², m²k, m²y, m³,
    // ck², cyk, cy², cmk, cmy, cm², c²k, c²y, c²m, c³.
    private static readonly double[] Red =
    [
        -0.803180, 0.071494, -0.068862, -1.101559, -0.056887,
        -0.114768, -0.113958, 0.142824, -0.079101, 0.028996,
        0.966668, 0.277752, 0.360922, 0.026293, 0.091580,
        0.051949, 0.033237, 0.064307, -0.012075, 0.040213,
        0.027756, -0.050177, 0.015752, -0.039471, -0.069774,
        -0.059045, -0.050329, -0.316786, -0.033123, 0.063312,
        0.010640, -0.156283, -0.008079, -0.027286,
    ];

    private static readonly double[] Green =
    [
        -0.803683, -0.066935, -0.805601, -0.399815, 0.034791,
        0.046780, -0.000014, 0.649890, 0.080141, 0.133329,
        0.382238, 0.049054, 0.403126, 0.093551, -0.008413,
        -0.001591, -0.008639, 0.013069, 0.016473, -0.045031,
        -0.027600, 0.034470, 0.036746, -0.153136, -0.040624,
        -0.002809, -0.011811, -0.358493, -0.040933, 0.070848,
        -0.059523, -0.009579, -0.089144, -0.006788,
    ];

    private static readonly double[] Blue =
    [
        -0.757823, -0.806179, -0.468566, -0.066118, 0.014424,
        0.632180, 0.072745, 0.359828, 0.496836, 0.028606,
        0.039023, 0.208200, 0.038686, 0.004309, -0.023342,
        0.013525, 0.042963, -0.086250, 0.009252, -0.357426,
        0.035014, -0.024201, -0.066334, 0.005588, 0.005169,
        -0.166013, 0.079875, -0.001326, -0.157697, 0.019153,
        0.013494, -0.060840, 0.020458, -0.002912,
    ];

    /// <summary>
    /// Converts one CMYK colour (each 0 to 1, already clipped) to encoded sRGB, each 0 to 1: 1 plus the dot product of each channel's
    /// coefficients with the monomials, in the term order above. Unrolled, without a term buffer, since it runs once per pixel of
    /// CMYK images, groups and shadings; the sums keep their order, so the result is the same bits on every platform.
    /// </summary>
    /// <param name="c">Cyan, 0 to 1.</param>
    /// <param name="m">Magenta, 0 to 1.</param>
    /// <param name="y">Yellow, 0 to 1.</param>
    /// <param name="k">Black, 0 to 1.</param>
    /// <param name="r">Red, 0 to 1.</param>
    /// <param name="g">Green, 0 to 1.</param>
    /// <param name="b">Blue, 0 to 1.</param>
    public static void ToRgb(double c, double m, double y, double k, out double r, out double g, out double b)
    {
        double t0 = k;
        double t1 = y;
        double t2 = m;
        double t3 = c;
        double t4 = k * k;
        double t5 = y * k;
        double t6 = y * y;
        double t7 = m * k;
        double t8 = m * y;
        double t9 = m * m;
        double t10 = c * k;
        double t11 = c * y;
        double t12 = c * m;
        double t13 = c * c;
        double t14 = k * k * k;
        double t15 = y * k * k;
        double t16 = y * y * k;
        double t17 = y * y * y;
        double t18 = m * k * k;
        double t19 = m * y * k;
        double t20 = m * y * y;
        double t21 = m * m * k;
        double t22 = m * m * y;
        double t23 = m * m * m;
        double t24 = c * k * k;
        double t25 = c * y * k;
        double t26 = c * y * y;
        double t27 = c * m * k;
        double t28 = c * m * y;
        double t29 = c * m * m;
        double t30 = c * c * k;
        double t31 = c * c * y;
        double t32 = c * c * m;
        double t33 = c * c * c;
        double rSum = 0;
        rSum += Red[0] * t0;
        rSum += Red[1] * t1;
        rSum += Red[2] * t2;
        rSum += Red[3] * t3;
        rSum += Red[4] * t4;
        rSum += Red[5] * t5;
        rSum += Red[6] * t6;
        rSum += Red[7] * t7;
        rSum += Red[8] * t8;
        rSum += Red[9] * t9;
        rSum += Red[10] * t10;
        rSum += Red[11] * t11;
        rSum += Red[12] * t12;
        rSum += Red[13] * t13;
        rSum += Red[14] * t14;
        rSum += Red[15] * t15;
        rSum += Red[16] * t16;
        rSum += Red[17] * t17;
        rSum += Red[18] * t18;
        rSum += Red[19] * t19;
        rSum += Red[20] * t20;
        rSum += Red[21] * t21;
        rSum += Red[22] * t22;
        rSum += Red[23] * t23;
        rSum += Red[24] * t24;
        rSum += Red[25] * t25;
        rSum += Red[26] * t26;
        rSum += Red[27] * t27;
        rSum += Red[28] * t28;
        rSum += Red[29] * t29;
        rSum += Red[30] * t30;
        rSum += Red[31] * t31;
        rSum += Red[32] * t32;
        rSum += Red[33] * t33;
        r = ColorMath.Clamp01(1 + rSum);
        double gSum = 0;
        gSum += Green[0] * t0;
        gSum += Green[1] * t1;
        gSum += Green[2] * t2;
        gSum += Green[3] * t3;
        gSum += Green[4] * t4;
        gSum += Green[5] * t5;
        gSum += Green[6] * t6;
        gSum += Green[7] * t7;
        gSum += Green[8] * t8;
        gSum += Green[9] * t9;
        gSum += Green[10] * t10;
        gSum += Green[11] * t11;
        gSum += Green[12] * t12;
        gSum += Green[13] * t13;
        gSum += Green[14] * t14;
        gSum += Green[15] * t15;
        gSum += Green[16] * t16;
        gSum += Green[17] * t17;
        gSum += Green[18] * t18;
        gSum += Green[19] * t19;
        gSum += Green[20] * t20;
        gSum += Green[21] * t21;
        gSum += Green[22] * t22;
        gSum += Green[23] * t23;
        gSum += Green[24] * t24;
        gSum += Green[25] * t25;
        gSum += Green[26] * t26;
        gSum += Green[27] * t27;
        gSum += Green[28] * t28;
        gSum += Green[29] * t29;
        gSum += Green[30] * t30;
        gSum += Green[31] * t31;
        gSum += Green[32] * t32;
        gSum += Green[33] * t33;
        g = ColorMath.Clamp01(1 + gSum);
        double bSum = 0;
        bSum += Blue[0] * t0;
        bSum += Blue[1] * t1;
        bSum += Blue[2] * t2;
        bSum += Blue[3] * t3;
        bSum += Blue[4] * t4;
        bSum += Blue[5] * t5;
        bSum += Blue[6] * t6;
        bSum += Blue[7] * t7;
        bSum += Blue[8] * t8;
        bSum += Blue[9] * t9;
        bSum += Blue[10] * t10;
        bSum += Blue[11] * t11;
        bSum += Blue[12] * t12;
        bSum += Blue[13] * t13;
        bSum += Blue[14] * t14;
        bSum += Blue[15] * t15;
        bSum += Blue[16] * t16;
        bSum += Blue[17] * t17;
        bSum += Blue[18] * t18;
        bSum += Blue[19] * t19;
        bSum += Blue[20] * t20;
        bSum += Blue[21] * t21;
        bSum += Blue[22] * t22;
        bSum += Blue[23] * t23;
        bSum += Blue[24] * t24;
        bSum += Blue[25] * t25;
        bSum += Blue[26] * t26;
        bSum += Blue[27] * t27;
        bSum += Blue[28] * t28;
        bSum += Blue[29] * t29;
        bSum += Blue[30] * t30;
        bSum += Blue[31] * t31;
        bSum += Blue[32] * t32;
        bSum += Blue[33] * t33;
        b = ColorMath.Clamp01(1 + bSum);
    }
}
