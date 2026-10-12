using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>XYZ to sRGB: the Bradford chromatic adaptation and the sRGB primaries, as the managed default's CIE conversion uses them.</summary>
/// <remarks>
/// ICC.1:2022 Annex E (the linearised Bradford transform) and IEC 61966-2-1 (sRGB). The constants are those of the core's managed
/// default, so a colour converts to the same sRGB whether a CIE-based space or a profile describes it.
/// </remarks>
internal static class Colorimetry
{
    /// <summary>XYZ (D65) to linear sRGB, row-major.</summary>
    public static readonly double[] XyzToSrgb = [3.2404542, -1.5371385, -0.4985314, -0.9692660, 1.8760108, 0.0415560, 0.0556434, -0.2040259, 1.0572252];

    /// <summary>The Bradford cone response matrix, row-major.</summary>
    private static readonly double[] Bradford = [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296];

    /// <summary>The inverse of <see cref="Bradford"/>.</summary>
    private static readonly double[] BradfordInverse = [0.9869929, -0.1470543, 0.1599627, 0.4323053, 0.5183603, 0.0492912, -0.0085287, 0.0400428, 0.9684867];

    /// <summary>Returns the matrix that adapts XYZ under <paramref name="white"/> to D65 and takes it to linear sRGB.</summary>
    /// <param name="white">The source white.</param>
    /// <returns>A row-major 3 × 3 matrix.</returns>
    public static double[] AdaptedToSrgb(CieXyz white)
    {
        double[] source = Apply(Bradford, white);
        double[] target = Apply(Bradford, CieXyz.D65);
        double[] scaled = new double[9];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                scaled[(3 * row) + column] = target[row] / source[row] * Bradford[(3 * row) + column];
            }
        }

        return Multiply(XyzToSrgb, Multiply(BradfordInverse, scaled));
    }

    private static double[] Apply(double[] matrix, CieXyz xyz) =>
    [
        (matrix[0] * xyz.X) + (matrix[1] * xyz.Y) + (matrix[2] * xyz.Z),
        (matrix[3] * xyz.X) + (matrix[4] * xyz.Y) + (matrix[5] * xyz.Z),
        (matrix[6] * xyz.X) + (matrix[7] * xyz.Y) + (matrix[8] * xyz.Z),
    ];

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
