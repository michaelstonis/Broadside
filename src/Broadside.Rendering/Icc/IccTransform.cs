using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>
/// A matrix/TRC or monochrome profile's colours to sRGB for one rendering intent: curves sampled into tables, one 3 × 3 matrix from
/// device-linear values to linear sRGB, and the sRGB encoding as a table. Immutable; converts without allocating.
/// </summary>
/// <remarks>
/// <para>
/// ICC.1:2022 Annex F.3 (linear = TRC(device); PCSXYZ = [rXYZ gXYZ bXYZ] × linear) and F.2 (PCS = TRC(device) × the PCS white; for
/// a PCSLAB profile the curve gives L* / 100). The PCS is media-relative and D50 (§6.3); it goes to sRGB by Bradford adaptation to
/// D65 and the IEC 61966-2-1 primaries and encoding, the same colorimetry as the managed default's CIE spaces. Matrix/TRC profiles
/// have one transform for every intent (§8.10.2 d)); the ICC-absolute colorimetric intent scales the PCS by the media white point
/// over the PCS white (§6.3.2.2, Equation 4) and is not adapted to D65.
/// </para>
/// <para>
/// Each curve is sampled at <see cref="TableSize"/> points and interpolated linearly, as is the sRGB encoding over linear values in
/// [0, 1]; both stay within 0.01 of 255 of the exact functions, far inside the 0.5 tolerance against the reference values.
/// Black point compensation (ISO 32000-2 §8.6.5.9) is not applied: a matrix/TRC profile's black is usually zero, and PDF 2.0
/// Application Note 001 applies to the profile pairs the ICC engine's later stages bring.
/// </para>
/// </remarks>
internal sealed class IccTransform
{
    /// <summary>The number of samples of each curve and of the encoding table.</summary>
    public const int TableSize = 4096;

    private static readonly float[] Encoding = BuildEncoding();

    private readonly float[][] _curves;
    private readonly float[] _matrix;

    private IccTransform(float[][] curves, float[] matrix)
    {
        _curves = curves;
        _matrix = matrix;
    }

    /// <summary>Gets the number of device components: 1 or 3.</summary>
    public int InputCount => _curves.Length;

    /// <summary>Builds the transform of a matrix/TRC or monochrome profile.</summary>
    /// <param name="profile">The profile; its model is <see cref="IccProfileModel.MatrixTrc"/> or <see cref="IccProfileModel.Monochrome"/>.</param>
    /// <param name="intent">The rendering intent.</param>
    /// <returns>The transform.</returns>
    public static IccTransform Create(IccProfile profile, RenderingIntent intent)
    {
        bool absolute = intent == RenderingIntent.AbsoluteColorimetric;
        CieXyz pcsWhite = CieXyz.D50;
        double[] toSrgb = absolute ? Colorimetry.XyzToSrgb : Colorimetry.AdaptedToSrgb(pcsWhite);
        double[] scale = absolute
            ? [profile.MediaWhitePoint.X / pcsWhite.X, profile.MediaWhitePoint.Y / pcsWhite.Y, profile.MediaWhitePoint.Z / pcsWhite.Z]
            : [1, 1, 1];

        // Columns of the device-linear to PCSXYZ matrix: the colorant columns, or the PCS white for the one gray channel.
        CieXyz[] columns = profile.Model == IccProfileModel.MatrixTrc ? profile.Columns : [pcsWhite];
        float[] matrix = new float[3 * columns.Length];
        for (int column = 0; column < columns.Length; column++)
        {
            double x = columns[column].X * scale[0];
            double y = columns[column].Y * scale[1];
            double z = columns[column].Z * scale[2];
            for (int row = 0; row < 3; row++)
            {
                matrix[(row * columns.Length) + column] = (float)((toSrgb[3 * row] * x) + (toSrgb[(3 * row) + 1] * y) + (toSrgb[(3 * row) + 2] * z));
            }
        }

        float[][] curves = new float[profile.Curves.Length][];
        for (int i = 0; i < curves.Length; i++)
        {
            curves[i] = profile.Curves[i].ToTable(TableSize);
        }

        if (profile.Model == IccProfileModel.Monochrome && profile.Header.ConnectionSpace == "Lab ")
        {
            // F.2 with a PCSLAB profile: the curve scales the PCS white L* = 100, so its value is L* / 100; Y is its inverse companding.
            float[] table = curves[0];
            for (int i = 0; i < table.Length; i++)
            {
                double m = ((100 * table[i]) + 16) / 116;
                table[i] = (float)(m >= 6.0 / 29 ? m * m * m : 108.0 / 841 * (m - (4.0 / 29)));
            }
        }

        return new IccTransform(curves, matrix);
    }

    /// <summary>Converts <paramref name="count"/> device colours to encoded sRGB, 0 to 1.</summary>
    /// <param name="source">At least <paramref name="count"/> × <see cref="InputCount"/> components; out-of-range values are clipped to [0, 1].</param>
    /// <param name="destination">At least <paramref name="count"/> × 3 values.</param>
    /// <param name="count">The number of colours.</param>
    public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        float[] m = _matrix;
        if (_curves.Length == 1)
        {
            float[] gray = _curves[0];
            for (int i = 0; i < count; i++)
            {
                float y = Lookup(gray, source[i]);
                Span<float> rgb = destination.Slice(3 * i, 3);
                rgb[0] = Lookup(Encoding, m[0] * y);
                rgb[1] = Lookup(Encoding, m[1] * y);
                rgb[2] = Lookup(Encoding, m[2] * y);
            }

            return;
        }

        float[] red = _curves[0];
        float[] green = _curves[1];
        float[] blue = _curves[2];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<float> device = source.Slice(3 * i, 3);
            float r = Lookup(red, device[0]);
            float g = Lookup(green, device[1]);
            float b = Lookup(blue, device[2]);
            Span<float> rgb = destination.Slice(3 * i, 3);
            rgb[0] = Lookup(Encoding, (m[0] * r) + (m[1] * g) + (m[2] * b));
            rgb[1] = Lookup(Encoding, (m[3] * r) + (m[4] * g) + (m[5] * b));
            rgb[2] = Lookup(Encoding, (m[6] * r) + (m[7] * g) + (m[8] * b));
        }
    }

    /// <summary>Interpolates a table of evenly spaced samples over [0, 1]; the input is clipped to [0, 1] (NaN to 0).</summary>
    private static float Lookup(float[] table, float x)
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

    /// <summary>The sRGB encoding of linear values 0 to 1 (IEC 61966-2-1).</summary>
    private static float[] BuildEncoding()
    {
        float[] table = new float[TableSize];
        for (int i = 0; i < table.Length; i++)
        {
            double linear = i / (double)(TableSize - 1);
            table[i] = (float)(linear <= 0.0031308 ? 12.92 * linear : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055);
        }

        return table;
    }
}
