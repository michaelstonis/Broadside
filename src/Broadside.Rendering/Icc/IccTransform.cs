using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>A profile's device colours to encoded sRGB for one rendering intent. Immutable; converts without allocating.</summary>
/// <remarks>
/// <para>
/// The PCS is media-relative and D50 (ICC.1:2022 §6.3); it goes to sRGB by Bradford adaptation to D65 and the IEC 61966-2-1
/// primaries and encoding, the same colorimetry as the managed default's CIE spaces. The ICC-absolute colorimetric intent scales
/// the PCS by the media white point over the PCS white (§6.3.2.2, Equation 4) and is not adapted to D65.
/// </para>
/// <para>
/// The tags are chosen by §8.10.2 (<see cref="IccProfile.SelectAToB"/>): a lookup table builds an <see cref="IccLutTransform"/>,
/// the matrix/TRC and gray TRC tags an <see cref="IccMatrixTransform"/>. Black point compensation (ISO 32000-2 §8.6.5.9) is not
/// applied yet.
/// </para>
/// </remarks>
internal abstract class IccTransform
{
    /// <summary>The number of samples of each curve and of the encoding table.</summary>
    public const int TableSize = IccTables.Size;

    /// <summary>Gets the number of device components.</summary>
    public abstract int InputCount { get; }

    /// <summary>Builds the transform of a profile that has tags to convert by (any <see cref="IccProfileModel"/> but <see cref="IccProfileModel.Other"/>).</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="intent">The rendering intent.</param>
    /// <returns>The transform.</returns>
    public static IccTransform Create(IccProfile profile, RenderingIntent intent) =>
        profile.SelectAToB(intent) is { } lut ? new IccLutTransform(profile, lut, intent) : IccMatrixTransform.Create(profile, intent);

    /// <summary>Converts <paramref name="count"/> device colours to encoded sRGB, 0 to 1.</summary>
    /// <param name="source">At least <paramref name="count"/> × <see cref="InputCount"/> components; out-of-range values are clipped to [0, 1].</param>
    /// <param name="destination">At least <paramref name="count"/> × 3 values.</param>
    /// <param name="count">The number of colours.</param>
    public abstract void Convert(ReadOnlySpan<float> source, Span<float> destination, int count);

    /// <summary>The PCSXYZ to linear sRGB matrix for an intent, row-major: media-relative and adapted, or ICC-absolute.</summary>
    protected static float[] PcsToSrgb(IccProfile profile, RenderingIntent intent)
    {
        bool absolute = intent == RenderingIntent.AbsoluteColorimetric;
        CieXyz pcsWhite = CieXyz.D50;
        double[] toSrgb = absolute ? Colorimetry.XyzToSrgb : Colorimetry.AdaptedToSrgb(pcsWhite);
        double[] scale = absolute
            ? [profile.MediaWhitePoint.X / pcsWhite.X, profile.MediaWhitePoint.Y / pcsWhite.Y, profile.MediaWhitePoint.Z / pcsWhite.Z]
            : [1, 1, 1];
        float[] matrix = new float[9];
        for (int i = 0; i < 9; i++)
        {
            matrix[i] = (float)(toSrgb[i] * scale[i % 3]);
        }

        return matrix;
    }
}

/// <summary>
/// A matrix/TRC or monochrome profile's transform: curves sampled into tables, one 3 × 3 matrix from device-linear values to linear
/// sRGB, and the sRGB encoding as a table.
/// </summary>
/// <remarks>
/// ICC.1:2022 Annex F.3 (linear = TRC(device); PCSXYZ = [rXYZ gXYZ bXYZ] × linear) and F.2 (PCS = TRC(device) × the PCS white; for
/// a PCSLAB profile the curve gives L* / 100). Matrix/TRC profiles have one transform for every intent (§8.10.2 d)). Each curve is
/// sampled at <see cref="IccTransform.TableSize"/> points and interpolated linearly, as is the sRGB encoding over linear values in
/// [0, 1]; both stay within 0.01 of 255 of the exact functions.
/// </remarks>
internal sealed class IccMatrixTransform : IccTransform
{
    private readonly float[][] _curves;
    private readonly float[] _matrix;

    private IccMatrixTransform(float[][] curves, float[] matrix)
    {
        _curves = curves;
        _matrix = matrix;
    }

    /// <inheritdoc/>
    public override int InputCount => _curves.Length;

    /// <summary>Builds the transform of a profile with matrix/TRC or gray TRC tags.</summary>
    public static new IccMatrixTransform Create(IccProfile profile, RenderingIntent intent)
    {
        float[] toSrgb = PcsToSrgb(profile, intent);

        // Columns of the device-linear to PCSXYZ matrix: the colorant columns, or the PCS white for the one gray channel.
        CieXyz[] columns = profile.Columns.Length == 3 ? profile.Columns : [CieXyz.D50];
        float[] matrix = new float[3 * columns.Length];
        for (int column = 0; column < columns.Length; column++)
        {
            double x = columns[column].X;
            double y = columns[column].Y;
            double z = columns[column].Z;
            for (int row = 0; row < 3; row++)
            {
                matrix[(row * columns.Length) + column] = (float)((toSrgb[3 * row] * x) + (toSrgb[(3 * row) + 1] * y) + (toSrgb[(3 * row) + 2] * z));
            }
        }

        float[][] curves = new float[columns.Length][];
        for (int i = 0; i < curves.Length; i++)
        {
            curves[i] = profile.Curves[i].ToTable(TableSize);
        }

        if (columns.Length == 1 && profile.Header.ConnectionSpace == "Lab ")
        {
            // F.2 with a PCSLAB profile: the curve scales the PCS white L* = 100, so its value is L* / 100; Y is its inverse companding.
            float[] table = curves[0];
            for (int i = 0; i < table.Length; i++)
            {
                double m = ((100 * table[i]) + 16) / 116;
                table[i] = (float)(m >= 6.0 / 29 ? m * m * m : 108.0 / 841 * (m - (4.0 / 29)));
            }
        }

        return new IccMatrixTransform(curves, matrix);
    }

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        float[] m = _matrix;
        float[] encoding = IccTables.SrgbEncoding;
        if (_curves.Length == 1)
        {
            float[] gray = _curves[0];
            for (int i = 0; i < count; i++)
            {
                float y = IccTables.Lookup(gray, source[i]);
                Span<float> rgb = destination.Slice(3 * i, 3);
                rgb[0] = IccTables.Lookup(encoding, m[0] * y);
                rgb[1] = IccTables.Lookup(encoding, m[1] * y);
                rgb[2] = IccTables.Lookup(encoding, m[2] * y);
            }

            return;
        }

        float[] red = _curves[0];
        float[] green = _curves[1];
        float[] blue = _curves[2];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<float> device = source.Slice(3 * i, 3);
            float r = IccTables.Lookup(red, device[0]);
            float g = IccTables.Lookup(green, device[1]);
            float b = IccTables.Lookup(blue, device[2]);
            Span<float> rgb = destination.Slice(3 * i, 3);
            rgb[0] = IccTables.Lookup(encoding, (m[0] * r) + (m[1] * g) + (m[2] * b));
            rgb[1] = IccTables.Lookup(encoding, (m[3] * r) + (m[4] * g) + (m[5] * b));
            rgb[2] = IccTables.Lookup(encoding, (m[6] * r) + (m[7] * g) + (m[8] * b));
        }
    }
}

/// <summary>A lookup-table profile's transform: the AToB tag, the PCS decoded to XYZ, and the PCSXYZ to sRGB matrix and encoding.</summary>
/// <remarks>
/// ICC.1:2022 §10.10 to §10.12 (the table, see <see cref="IccLut"/>), §6.3.4.2 and §10.10 (the PCS encodings, see <see cref="IccPcs"/>)
/// and CIE L*a*b* to XYZ relative to the D50 PCS white.
/// </remarks>
internal sealed class IccLutTransform : IccTransform
{
    private readonly IccLut _lut;
    private readonly bool _lab;
    private readonly float[] _matrix;

    public IccLutTransform(IccProfile profile, IccLut lut, RenderingIntent intent)
    {
        _lut = lut;
        _lab = profile.Header.ConnectionSpace == "Lab ";
        _matrix = PcsToSrgb(profile, intent);
    }

    /// <inheritdoc/>
    public override int InputCount => _lut.InputCount;

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        Span<float> scratch = stackalloc float[2 * IccClut.MaxChannels];
        Span<float> pcs = stackalloc float[3];
        float[] m = _matrix;
        float[] encoding = IccTables.SrgbEncoding;
        int inputs = _lut.InputCount;
        IccLutKind kind = _lut.Kind;
        for (int i = 0; i < count; i++)
        {
            _lut.Evaluate(source.Slice(i * inputs, inputs), pcs, scratch);
            IccPcs.Decode(kind, _lab, pcs, pcs);
            float x = pcs[0];
            float y = pcs[1];
            float z = pcs[2];
            if (_lab)
            {
                IccPcs.LabToXyz(x, y, z, out x, out y, out z);
            }

            Span<float> rgb = destination.Slice(3 * i, 3);
            rgb[0] = IccTables.Lookup(encoding, (m[0] * x) + (m[1] * y) + (m[2] * z));
            rgb[1] = IccTables.Lookup(encoding, (m[3] * x) + (m[4] * y) + (m[5] * z));
            rgb[2] = IccTables.Lookup(encoding, (m[6] * x) + (m[7] * y) + (m[8] * z));
        }
    }
}
