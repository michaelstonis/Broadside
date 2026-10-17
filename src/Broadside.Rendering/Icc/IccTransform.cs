using Broadside.Graphics;

namespace Broadside.Rendering.Icc;

/// <summary>
/// A conversion from one profile's device colours to another's for one rendering intent and black point compensation setting.
/// Immutable; converts without allocating.
/// </summary>
/// <remarks>
/// <para>
/// The source's tags for the intent take a colour to the PCS (ICC.1:2022 §8.10.2: an AToB tag, else matrix/TRC); the PCS colour, as
/// media-relative D50 PCSXYZ, is adjusted; the destination's tags for the intent take it to device values (a BToA tag, else the
/// inverse of the matrix/TRC model, Annex F.3 Equations F.7 to F.10, with the curves inverted by Annex F.1).
/// </para>
/// <para>
/// The adjustment: for the ICC-absolute colorimetric intent, the source's PCS white scaled to its actual medium white and the
/// destination's back (§6.3.2.2, Equations 1 and 4), with the actual white of each profile (<see cref="IccProfile.ActualWhitePoint"/>),
/// so a display destination shows a source medium against its own white; with black point compensation (ISO 32000-2 §8.6.5.9,
/// ISO 18619, see <see cref="IccBlackPoint"/>), each tristimulus value scaled and offset so the source black point lands on the
/// destination's while the PCS white stays; otherwise nothing.
/// </para>
/// </remarks>
internal abstract class IccTransform
{
    /// <summary>The number of samples of each curve, and of each inverted curve.</summary>
    public const int TableSize = IccTables.Size;

    /// <summary>Gets the number of source components.</summary>
    public abstract int InputCount { get; }

    /// <summary>Gets the number of destination components.</summary>
    public abstract int OutputCount { get; }

    /// <summary>Builds the transform from a profile that has tags to convert by to a profile with a BToA tag or matrix/TRC tags.</summary>
    /// <param name="source">The source profile (any <see cref="IccProfileModel"/> but <see cref="IccProfileModel.Other"/>).</param>
    /// <param name="destination">The destination profile: RGB matrix/TRC, or any profile with a BToA tag for the intent.</param>
    /// <param name="intent">The rendering intent.</param>
    /// <param name="blackPointCompensation">Whether black point compensation applies (never under the absolute intent).</param>
    /// <returns>The transform.</returns>
    public static IccTransform Create(IccProfile source, IccProfile destination, RenderingIntent intent, bool blackPointCompensation)
    {
        PcsAdjustment adjustment = PcsAdjustment.Create(source, destination, intent, blackPointCompensation);
        if (destination.SelectBToA(intent) is { } output)
        {
            return new IccToLutTransform(source, destination, output, intent, adjustment);
        }

        IccMatrixOutput matrixOutput = destination.MatrixOutput;
        return source.SelectAToB(intent) is { } lut
            ? new IccLutTransform(source, lut, matrixOutput, adjustment)
            : IccMatrixTransform.Create(source, matrixOutput, adjustment);
    }

    /// <summary>Converts <paramref name="count"/> source colours to destination colours, 0 to 1.</summary>
    /// <param name="source">At least <paramref name="count"/> × <see cref="InputCount"/> components; out-of-range values are clipped to [0, 1].</param>
    /// <param name="destination">At least <paramref name="count"/> × <see cref="OutputCount"/> values.</param>
    /// <param name="count">The number of colours.</param>
    public abstract void Convert(ReadOnlySpan<float> source, Span<float> destination, int count);

    /// <summary>
    /// The 3 × 3 matrix and offset from adjusted PCSXYZ to linear destination values: the destination's inverse colorant matrix times
    /// the adjustment's scale, and the inverse matrix times its offset.
    /// </summary>
    protected static (float[] Matrix, float[] Offset) PcsToLinear(IccMatrixOutput output, in PcsAdjustment adjustment)
    {
        double[] inverse = output.InverseMatrix;
        float[] matrix = new float[9];
        float[] offset = new float[3];
        for (int row = 0; row < 3; row++)
        {
            double sum = 0;
            for (int column = 0; column < 3; column++)
            {
                matrix[(3 * row) + column] = (float)(inverse[(3 * row) + column] * adjustment.Scale(column));
                sum += inverse[(3 * row) + column] * adjustment.Offset(column);
            }

            offset[row] = (float)sum;
        }

        return (matrix, offset);
    }
}

/// <summary>The change made to a colour between the source's PCS and the destination's: a scale and an offset per tristimulus value.</summary>
internal readonly struct PcsAdjustment
{
    private readonly double[]? _scale;
    private readonly double[]? _offset;

    private PcsAdjustment(double[] scale, double[] offset)
    {
        _scale = scale;
        _offset = offset;
    }

    /// <summary>Gets a value indicating whether the adjustment changes nothing.</summary>
    public bool IsIdentity => _scale is null;

    /// <summary>The adjustment for a conversion; see <see cref="IccTransform"/>.</summary>
    public static PcsAdjustment Create(IccProfile source, IccProfile destination, RenderingIntent intent, bool blackPointCompensation)
    {
        if (intent == RenderingIntent.AbsoluteColorimetric)
        {
            CieXyz from = source.ActualWhitePoint;
            CieXyz to = destination.ActualWhitePoint;
            return new PcsAdjustment([from.X / to.X, from.Y / to.Y, from.Z / to.Z], [0, 0, 0]);
        }

        if (!blackPointCompensation)
        {
            return default;
        }

        CieXyz blackIn = IccBlackPoint.OfSource(source, intent);
        CieXyz blackOut = IccBlackPoint.OfDestination(destination, intent);
        if (blackIn == blackOut)
        {
            return default;
        }

        // ISO 18619 as Little CMS computes it: X' = a·X + b with the D50 PCS white fixed and blackIn taken to blackOut.
        double[] white = [CieXyz.D50.X, CieXyz.D50.Y, CieXyz.D50.Z];
        double[] input = [blackIn.X, blackIn.Y, blackIn.Z];
        double[] output = [blackOut.X, blackOut.Y, blackOut.Z];
        double[] scale = new double[3];
        double[] offset = new double[3];
        for (int i = 0; i < 3; i++)
        {
            double t = input[i] - white[i];
            if (!(Math.Abs(t) > 1e-9))
            {
                return default;
            }

            scale[i] = (output[i] - white[i]) / t;
            offset[i] = -white[i] * (output[i] - input[i]) / t;
        }

        return new PcsAdjustment(scale, offset);
    }

    /// <summary>The scale of a tristimulus value (0 X, 1 Y, 2 Z).</summary>
    public double Scale(int index) => _scale?[index] ?? 1;

    /// <summary>The offset of a tristimulus value (0 X, 1 Y, 2 Z).</summary>
    public double Offset(int index) => _offset?[index] ?? 0;
}

/// <summary>The inverse of a destination's matrix/TRC model: the inverse colorant matrix and the inverted curves (ICC.1:2022 Annex F.1, F.3).</summary>
internal sealed class IccMatrixOutput(double[] inverseMatrix, float[][] inverseCurves)
{
    /// <summary>Gets the PCSXYZ to linear device matrix, row-major.</summary>
    public double[] InverseMatrix => inverseMatrix;

    /// <summary>Gets the inverted red, green and blue curves, <see cref="IccTransform.TableSize"/> samples each over linear [0, 1].</summary>
    public float[][] InverseCurves => inverseCurves;
}

/// <summary>
/// A matrix/TRC or monochrome source to a matrix/TRC destination: curves sampled into tables, one 3 × N matrix and offset from
/// device-linear values to linear destination values, and the destination's inverted curves as tables.
/// </summary>
/// <remarks>
/// ICC.1:2022 Annex F.3 (linear = TRC(device); PCSXYZ = [rXYZ gXYZ bXYZ] × linear) and F.2 (PCS = TRC(device) × the PCS white; for
/// a PCSLAB profile the curve gives L* / 100). Matrix/TRC profiles have one transform for every intent (§8.10.2 d)). Each curve is
/// sampled at <see cref="IccTransform.TableSize"/> points and interpolated linearly; both directions stay within 0.01 of 255 of the
/// exact functions.
/// </remarks>
internal sealed class IccMatrixTransform : IccTransform
{
    private readonly float[][] _curves;
    private readonly float[] _matrix;
    private readonly float[] _offset;
    private readonly float[][] _output;

    private IccMatrixTransform(float[][] curves, float[] matrix, float[] offset, float[][] output)
    {
        _curves = curves;
        _matrix = matrix;
        _offset = offset;
        _output = output;
    }

    /// <inheritdoc/>
    public override int InputCount => _curves.Length;

    /// <inheritdoc/>
    public override int OutputCount => 3;

    /// <summary>Builds the transform of a source with matrix/TRC or gray TRC tags.</summary>
    public static IccMatrixTransform Create(IccProfile profile, IccMatrixOutput output, in PcsAdjustment adjustment)
    {
        (float[] toLinear, float[] offset) = PcsToLinear(output, adjustment);

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
                matrix[(row * columns.Length) + column] = (float)((toLinear[3 * row] * x) + (toLinear[(3 * row) + 1] * y) + (toLinear[(3 * row) + 2] * z));
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

        return new IccMatrixTransform(curves, matrix, offset, output.InverseCurves);
    }

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        float[] m = _matrix;
        float[] o = _offset;
        float[] red = _output[0];
        float[] green = _output[1];
        float[] blue = _output[2];
        if (_curves.Length == 1)
        {
            float[] gray = _curves[0];
            for (int i = 0; i < count; i++)
            {
                float y = IccTables.Lookup(gray, source[i]);
                Span<float> rgb = destination.Slice(3 * i, 3);
                rgb[0] = IccTables.Lookup(red, (m[0] * y) + o[0]);
                rgb[1] = IccTables.Lookup(green, (m[1] * y) + o[1]);
                rgb[2] = IccTables.Lookup(blue, (m[2] * y) + o[2]);
            }

            return;
        }

        float[] r0 = _curves[0];
        float[] g0 = _curves[1];
        float[] b0 = _curves[2];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<float> device = source.Slice(3 * i, 3);
            float r = IccTables.Lookup(r0, device[0]);
            float g = IccTables.Lookup(g0, device[1]);
            float b = IccTables.Lookup(b0, device[2]);
            Span<float> rgb = destination.Slice(3 * i, 3);
            rgb[0] = IccTables.Lookup(red, (m[0] * r) + (m[1] * g) + (m[2] * b) + o[0]);
            rgb[1] = IccTables.Lookup(green, (m[3] * r) + (m[4] * g) + (m[5] * b) + o[1]);
            rgb[2] = IccTables.Lookup(blue, (m[6] * r) + (m[7] * g) + (m[8] * b) + o[2]);
        }
    }
}

/// <summary>A lookup-table source to a matrix/TRC destination: the AToB tag, the PCS decoded to XYZ, one matrix and offset, and the inverted curves.</summary>
/// <remarks>
/// ICC.1:2022 §10.10 to §10.12 (the table, see <see cref="IccLut"/>), §6.3.4.2 and §10.10 (the PCS encodings, see <see cref="IccPcs"/>)
/// and CIE L*a*b* to XYZ relative to the D50 PCS white.
/// </remarks>
internal sealed class IccLutTransform : IccTransform
{
    private readonly IccLut _lut;
    private readonly bool _lab;
    private readonly float[] _matrix;
    private readonly float[] _offset;
    private readonly float[][] _output;

    public IccLutTransform(IccProfile profile, IccLut lut, IccMatrixOutput output, in PcsAdjustment adjustment)
    {
        _lut = lut;
        _lab = profile.Header.ConnectionSpace == "Lab ";
        (_matrix, _offset) = PcsToLinear(output, adjustment);
        _output = output.InverseCurves;
    }

    /// <inheritdoc/>
    public override int InputCount => _lut.InputCount;

    /// <inheritdoc/>
    public override int OutputCount => 3;

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        Span<float> scratch = stackalloc float[2 * IccClut.MaxChannels];
        Span<float> pcs = stackalloc float[3];
        float[] m = _matrix;
        float[] o = _offset;
        float[] red = _output[0];
        float[] green = _output[1];
        float[] blue = _output[2];
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
            rgb[0] = IccTables.Lookup(red, (m[0] * x) + (m[1] * y) + (m[2] * z) + o[0]);
            rgb[1] = IccTables.Lookup(green, (m[3] * x) + (m[4] * y) + (m[5] * z) + o[1]);
            rgb[2] = IccTables.Lookup(blue, (m[6] * x) + (m[7] * y) + (m[8] * z) + o[2]);
        }
    }
}

/// <summary>Any source to a destination's BToA tag: the source to PCSXYZ, the adjustment, the PCS encoded for the destination, its table.</summary>
/// <remarks>ICC.1:2022 §10.10, §10.11 and §10.13 (the BToA table), §6.3.4.2 (its PCS encoding).</remarks>
internal sealed class IccToLutTransform : IccTransform
{
    private readonly IccProfile _source;
    private readonly RenderingIntent _sourceIntent;
    private readonly bool _sourceLab;
    private readonly IccLut _output;
    private readonly bool _destinationLab;
    private readonly float[] _scale;
    private readonly float[] _offset;

    public IccToLutTransform(IccProfile source, IccProfile destination, IccLut output, RenderingIntent intent, in PcsAdjustment adjustment)
    {
        _source = source;

        // The absolute intent's white scaling is the adjustment's, so the source converts media-relative.
        _sourceIntent = intent == RenderingIntent.AbsoluteColorimetric ? RenderingIntent.RelativeColorimetric : intent;
        _sourceLab = source.Header.ConnectionSpace == "Lab ";
        _output = output;
        _destinationLab = destination.Header.ConnectionSpace == "Lab ";
        _scale = [(float)adjustment.Scale(0), (float)adjustment.Scale(1), (float)adjustment.Scale(2)];
        _offset = [(float)adjustment.Offset(0), (float)adjustment.Offset(1), (float)adjustment.Offset(2)];
    }

    /// <inheritdoc/>
    public override int InputCount => Math.Max(_source.Header.ComponentCount, 1);

    /// <inheritdoc/>
    public override int OutputCount => _output.OutputCount;

    /// <inheritdoc/>
    public override void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        Span<float> pcs = stackalloc float[3];
        Span<float> normalized = stackalloc float[3];
        Span<float> scratch = stackalloc float[2 * IccClut.MaxChannels];
        int inputs = InputCount;
        int outputs = OutputCount;
        for (int i = 0; i < count; i++)
        {
            _source.TryConvertToConnectionSpace(source.Slice(i * inputs, inputs), pcs, _sourceIntent);
            if (_sourceLab)
            {
                IccPcs.LabToXyz(pcs[0], pcs[1], pcs[2], out pcs[0], out pcs[1], out pcs[2]);
            }

            for (int c = 0; c < 3; c++)
            {
                pcs[c] = (_scale[c] * pcs[c]) + _offset[c];
            }

            if (_destinationLab)
            {
                IccPcs.XyzToLab(pcs[0], pcs[1], pcs[2], out pcs[0], out pcs[1], out pcs[2]);
            }

            IccPcs.Encode(_output.Kind, _destinationLab, pcs, normalized);
            _output.Evaluate(normalized, destination.Slice(i * outputs, outputs), scratch);
        }
    }
}
