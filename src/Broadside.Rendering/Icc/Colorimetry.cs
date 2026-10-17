using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>The Bradford chromatic adaptation and 3 × 3 matrix arithmetic the ICC engine builds its transforms and its sRGB profile from.</summary>
/// <remarks>
/// ICC.1:2022 Annex E (the linearised Bradford transform). The constants are those of the core's managed default, so a colour
/// converts to the same sRGB whether a CIE-based space or a profile describes it.
/// </remarks>
internal static class Colorimetry
{
    /// <summary>The Bradford cone response matrix, row-major.</summary>
    private static readonly double[] BradfordCone = [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296];

    /// <summary>The inverse of <see cref="BradfordCone"/>.</summary>
    private static readonly double[] BradfordInverse = [0.9869929, -0.1470543, 0.1599627, 0.4323053, 0.5183603, 0.0492912, -0.0085287, 0.0400428, 0.9684867];

    /// <summary>Returns the linearised Bradford chromatic adaptation from one white to another (ICC.1:2022 Annex E).</summary>
    /// <param name="from">The source white.</param>
    /// <param name="to">The destination white.</param>
    /// <returns>A row-major 3 × 3 matrix taking XYZ under <paramref name="from"/> to XYZ under <paramref name="to"/>.</returns>
    public static double[] Bradford(CieXyz from, CieXyz to)
    {
        double[] source = Apply(BradfordCone, from);
        double[] target = Apply(BradfordCone, to);
        double[] scaled = new double[9];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                scaled[(3 * row) + column] = target[row] / source[row] * BradfordCone[(3 * row) + column];
            }
        }

        return Multiply(BradfordInverse, scaled);
    }

    /// <summary>Returns the inverse of a 3 × 3 matrix, or <see langword="null"/> when it is singular.</summary>
    /// <param name="m">A row-major 3 × 3 matrix.</param>
    /// <returns>The row-major inverse.</returns>
    public static double[]? Invert(ReadOnlySpan<double> m)
    {
        double c0 = (m[4] * m[8]) - (m[5] * m[7]);
        double c1 = (m[5] * m[6]) - (m[3] * m[8]);
        double c2 = (m[3] * m[7]) - (m[4] * m[6]);
        double determinant = (m[0] * c0) + (m[1] * c1) + (m[2] * c2);
        if (!(Math.Abs(determinant) > 1e-12) || !double.IsFinite(determinant))
        {
            return null;
        }

        double f = 1 / determinant;
        return
        [
            c0 * f, ((m[2] * m[7]) - (m[1] * m[8])) * f, ((m[1] * m[5]) - (m[2] * m[4])) * f,
            c1 * f, ((m[0] * m[8]) - (m[2] * m[6])) * f, ((m[2] * m[3]) - (m[0] * m[5])) * f,
            c2 * f, ((m[1] * m[6]) - (m[0] * m[7])) * f, ((m[0] * m[4]) - (m[1] * m[3])) * f,
        ];
    }

    /// <summary>Applies a row-major 3 × 3 matrix to XYZ.</summary>
    /// <param name="matrix">The matrix.</param>
    /// <param name="xyz">The colour.</param>
    /// <returns>The product.</returns>
    public static CieXyz Transform(ReadOnlySpan<double> matrix, CieXyz xyz) => new(
        (matrix[0] * xyz.X) + (matrix[1] * xyz.Y) + (matrix[2] * xyz.Z),
        (matrix[3] * xyz.X) + (matrix[4] * xyz.Y) + (matrix[5] * xyz.Z),
        (matrix[6] * xyz.X) + (matrix[7] * xyz.Y) + (matrix[8] * xyz.Z));

    /// <summary>Returns the product of two row-major 3 × 3 matrices.</summary>
    /// <param name="left">The left factor.</param>
    /// <param name="right">The right factor.</param>
    /// <returns>left × right.</returns>
    public static double[] Multiply(double[] left, double[] right)
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

    private static double[] Apply(double[] matrix, CieXyz xyz) =>
    [
        (matrix[0] * xyz.X) + (matrix[1] * xyz.Y) + (matrix[2] * xyz.Z),
        (matrix[3] * xyz.X) + (matrix[4] * xyz.Y) + (matrix[5] * xyz.Z),
        (matrix[6] * xyz.X) + (matrix[7] * xyz.Y) + (matrix[8] * xyz.Z),
    ];
}
