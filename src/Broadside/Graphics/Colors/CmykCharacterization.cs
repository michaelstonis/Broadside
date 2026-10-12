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

    /// <summary>Converts one CMYK colour (each 0 to 1, already clipped) to encoded sRGB, each 0 to 1.</summary>
    /// <param name="c">Cyan.</param>
    /// <param name="m">Magenta.</param>
    /// <param name="y">Yellow.</param>
    /// <param name="k">Black.</param>
    /// <param name="rgb">Three values.</param>
    public static void ToRgb(double c, double m, double y, double k, Span<double> rgb)
    {
        Span<double> terms = stackalloc double[34];
        terms[0] = k;
        terms[1] = y;
        terms[2] = m;
        terms[3] = c;
        terms[4] = k * k;
        terms[5] = y * k;
        terms[6] = y * y;
        terms[7] = m * k;
        terms[8] = m * y;
        terms[9] = m * m;
        terms[10] = c * k;
        terms[11] = c * y;
        terms[12] = c * m;
        terms[13] = c * c;
        terms[14] = k * k * k;
        terms[15] = y * k * k;
        terms[16] = y * y * k;
        terms[17] = y * y * y;
        terms[18] = m * k * k;
        terms[19] = m * y * k;
        terms[20] = m * y * y;
        terms[21] = m * m * k;
        terms[22] = m * m * y;
        terms[23] = m * m * m;
        terms[24] = c * k * k;
        terms[25] = c * y * k;
        terms[26] = c * y * y;
        terms[27] = c * m * k;
        terms[28] = c * m * y;
        terms[29] = c * m * m;
        terms[30] = c * c * k;
        terms[31] = c * c * y;
        terms[32] = c * c * m;
        terms[33] = c * c * c;
        rgb[0] = ColorMath.Clamp01(1 + Dot(Red, terms));
        rgb[1] = ColorMath.Clamp01(1 + Dot(Green, terms));
        rgb[2] = ColorMath.Clamp01(1 + Dot(Blue, terms));
    }

    private static double Dot(ReadOnlySpan<double> coefficients, ReadOnlySpan<double> terms)
    {
        double sum = 0;
        for (int i = 0; i < coefficients.Length; i++)
        {
            sum += coefficients[i] * terms[i];
        }

        return sum;
    }
}
