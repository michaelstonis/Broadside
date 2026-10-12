namespace Broadside.Graphics.Colors;

/// <summary>The colorimetry the managed colour management is built from: chromatic adaptation, XYZ to sRGB and the device formulas.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.5 (CIE-based spaces), §10.4.2 (conversions among device spaces). The Bradford transform and the sRGB primaries
/// and encoding are those of IEC 61966-2-1 and ICC.1:2022 Annex E; all arithmetic is in double precision.
/// </remarks>
internal static class ColorMath
{
    /// <summary>The Bradford cone response matrix, row-major.</summary>
    private static readonly double[] Bradford = [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296];

    /// <summary>The inverse of <see cref="Bradford"/>.</summary>
    private static readonly double[] BradfordInverse = [0.9869929, -0.1470543, 0.1599627, 0.4323053, 0.5183603, 0.0492912, -0.0085287, 0.0400428, 0.9684867];

    /// <summary>XYZ (D65) to linear sRGB (IEC 61966-2-1).</summary>
    private static readonly double[] XyzToSrgb = [3.2404542, -1.5371385, -0.4985314, -0.9692660, 1.8760108, 0.0415560, 0.0556434, -0.2040259, 1.0572252];

    /// <summary>Returns the matrix taking XYZ under <paramref name="source"/> white to XYZ under D65 and then to linear sRGB.</summary>
    /// <param name="source">The source white, or <see langword="null"/> to skip adaptation (absolute colorimetric).</param>
    /// <returns>A row-major 3 × 3 matrix.</returns>
    public static double[] XyzToLinearSrgb(CieXyz? source)
    {
        if (source is not { } white)
        {
            return XyzToSrgb;
        }

        Span<double> sourceCone = stackalloc double[3];
        Span<double> targetCone = stackalloc double[3];
        Apply(Bradford, white.X, white.Y, white.Z, sourceCone);
        CieXyz d65 = CieXyz.D65;
        Apply(Bradford, d65.X, d65.Y, d65.Z, targetCone);
        double[] scaled = new double[9];
        for (int row = 0; row < 3; row++)
        {
            double factor = targetCone[row] / sourceCone[row];
            for (int column = 0; column < 3; column++)
            {
                scaled[(3 * row) + column] = factor * Bradford[(3 * row) + column];
            }
        }

        return Multiply(XyzToSrgb, Multiply(BradfordInverse, scaled));
    }

    /// <summary>The sRGB transfer function, linear to encoded, clipped to 0..1.</summary>
    /// <param name="linear">The linear value.</param>
    /// <returns>The encoded value.</returns>
    public static double EncodeSrgb(double linear)
    {
        if (!(linear > 0.0031308))
        {
            return linear > 0 ? 12.92 * linear : 0;
        }

        return linear >= 1 ? 1 : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
    }

    /// <summary>The inverse of the Lab companding function: g(x) = x³ for x ≥ 6/29, else 108/841·(x − 4/29).</summary>
    /// <param name="x">The value.</param>
    /// <returns>g(x).</returns>
    /// <remarks>ISO 32000-2 §8.6.5.4.</remarks>
    public static double LabG(double x) => x >= 6.0 / 29 ? x * x * x : 108.0 / 841 * (x - (4.0 / 29));

    /// <summary>Writes an RGB colour (each 0 to 1) in the target model.</summary>
    /// <param name="r">Red.</param>
    /// <param name="g">Green.</param>
    /// <param name="b">Blue.</param>
    /// <param name="destination">One, three or four values.</param>
    /// <param name="conversion">The target and, for CMYK, the black generation and undercolour removal.</param>
    /// <remarks>ISO 32000-2 §10.4.2.2 (RGB to gray) and §10.4.2.3 (RGB to CMYK).</remarks>
    public static void FromRgb(double r, double g, double b, Span<float> destination, in ColorConversion conversion)
    {
        switch (conversion.Target)
        {
            case DeviceColorModel.Gray:
                destination[0] = (float)Clamp01((0.3 * r) + (0.59 * g) + (0.11 * b));
                break;
            case DeviceColorModel.Cmyk:
                RgbToCmyk(r, g, b, destination, conversion);
                break;
            default:
                destination[0] = (float)Clamp01(r);
                destination[1] = (float)Clamp01(g);
                destination[2] = (float)Clamp01(b);
                break;
        }
    }

    /// <summary>RGB to CMYK: c = 1 − r, m = 1 − g, y = 1 − b, k = min(c, m, y); then UCR(k) is removed from each and BG(k) is black.</summary>
    /// <param name="r">Red.</param>
    /// <param name="g">Green.</param>
    /// <param name="b">Blue.</param>
    /// <param name="destination">Four values.</param>
    /// <param name="conversion">The black generation and undercolour removal functions; null for BG(k) = k and UCR(k) = k.</param>
    /// <remarks>
    /// ISO 32000-2 §10.4.2.3. BG and UCR are device-dependent; without them Broadside removes all of the undercolour and replaces it
    /// by black (BG(k) = k, UCR(k) = k).
    /// </remarks>
    public static void RgbToCmyk(double r, double g, double b, Span<float> destination, in ColorConversion conversion)
    {
        double c = 1 - Clamp01(r);
        double m = 1 - Clamp01(g);
        double y = 1 - Clamp01(b);
        double k = Math.Min(c, Math.Min(m, y));
        double black = k;
        double undercolor = k;
        Span<float> io = stackalloc float[2];
        if (conversion.BlackGeneration is { } generation)
        {
            io[0] = (float)k;
            generation.Evaluate(io[..1], io[1..]);
            black = io[1];
        }

        if (conversion.UndercolorRemoval is { } removal)
        {
            io[0] = (float)k;
            removal.Evaluate(io[..1], io[1..]);
            undercolor = io[1];
        }

        destination[0] = (float)Clamp01(c - undercolor);
        destination[1] = (float)Clamp01(m - undercolor);
        destination[2] = (float)Clamp01(y - undercolor);
        destination[3] = (float)Clamp01(black);
    }

    /// <summary>Clips to 0..1; NaN becomes 0.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The clipped value.</returns>
    public static double Clamp01(double value) => value > 0 ? Math.Min(value, 1) : 0;

    /// <summary>Multiplies a row-major 3 × 3 matrix by a vector.</summary>
    public static void Apply(ReadOnlySpan<double> matrix, double x, double y, double z, Span<double> result)
    {
        result[0] = (matrix[0] * x) + (matrix[1] * y) + (matrix[2] * z);
        result[1] = (matrix[3] * x) + (matrix[4] * y) + (matrix[5] * z);
        result[2] = (matrix[6] * x) + (matrix[7] * y) + (matrix[8] * z);
    }

    private static double[] Multiply(double[] left, double[] right)
    {
        double[] product = new double[9];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                double sum = 0;
                for (int k = 0; k < 3; k++)
                {
                    sum += left[(3 * row) + k] * right[(3 * k) + column];
                }

                product[(3 * row) + column] = sum;
            }
        }

        return product;
    }
}
