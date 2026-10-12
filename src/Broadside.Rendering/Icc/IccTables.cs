using System.Runtime.CompilerServices;

namespace Broadside.Rendering.Icc;

/// <summary>Evenly spaced one-dimensional tables, as the ICC engine samples curves and the sRGB encoding into them.</summary>
/// <remarks>
/// ICC.1:2022 §10.6 (a curve's entries span [0, 1] evenly, linear interpolation between them), §10.10 and §10.11 (the same for the
/// input and output tables of lut16Type and lut8Type) and IEC 61966-2-1 (the sRGB encoding).
/// </remarks>
internal static class IccTables
{
    /// <summary>The number of samples of a curve that is not a table itself, and of the encoding table.</summary>
    public const int Size = 4096;

    /// <summary>Gets the sRGB encoding of linear values 0 to 1, <see cref="Size"/> samples.</summary>
    public static float[] SrgbEncoding { get; } = BuildEncoding();

    /// <summary>Interpolates a table of evenly spaced samples over [0, 1]; the input is clipped to [0, 1] (NaN to 0).</summary>
    /// <param name="table">At least two samples.</param>
    /// <param name="x">The input.</param>
    /// <returns>The interpolated value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lookup(float[] table, float x)
    {
        float position = (x > 0 ? MathF.Min(x, 1) : 0) * (table.Length - 1);
        int index = (int)position;
        if (index >= table.Length - 1)
        {
            return table[^1];
        }

        float low = table[index];
        return low + ((position - index) * (table[index + 1] - low));
    }

    private static float[] BuildEncoding()
    {
        float[] table = new float[Size];
        for (int i = 0; i < table.Length; i++)
        {
            double linear = i / (double)(Size - 1);
            table[i] = (float)(linear <= 0.0031308 ? 12.92 * linear : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055);
        }

        return table;
    }
}
