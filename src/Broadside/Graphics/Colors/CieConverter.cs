namespace Broadside.Graphics.Colors;

/// <summary>Converts CalGray, CalRGB and Lab colours (and Lab-based ICC profiles) through CIE XYZ to sRGB, then to the target model.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.5.2 to §8.6.5.4 give XYZ relative to the space's white point; §10.3 leaves the CIE to device step to colour
/// management. The pipeline: clip the components into range; compute XYZ; adapt from the white point to D65 with the Bradford
/// transform (none for the absolute colorimetric intent); take XYZ to linear sRGB and encode it (IEC 61966-2-1); RGB then goes to
/// gray or CMYK by §10.4.2.
/// </para>
/// <para>
/// The <c>BlackPoint</c> entry does not enter the formulas, which give XYZ = 0 for the darkest components whatever it says. Black
/// point compensation (§8.6.5.9, ISO 18619) maps the darkest colour a source produces, here that zero, to the destination's black,
/// sRGB's zero, so it changes nothing for these spaces; scaling by the entry instead would clip every colour darker than it to
/// black (#279). PDFium, pdf.js, Ghostscript and Poppler render it this way too.
/// </para>
/// <para>
/// pdf.js approximates CalGray and Lab differently, so renders differ slightly from it; this is the colorimetric result.
/// </para>
/// </remarks>
internal sealed class CieConverter : IColorConverter
{
    private readonly PdfColorSpaceFamily _family;
    private readonly ColorConversion _conversion;
    private readonly CieXyz _white;
    private readonly double[] _gamma;
    private readonly double[] _matrix;
    private readonly double[] _ranges;
    private readonly double[] _toSrgb;

    private CieConverter(PdfColorSpaceFamily family, ColorConversion conversion, CieXyz white, double[] gamma, double[] matrix, double[] ranges)
    {
        _family = family;
        _conversion = conversion;
        _white = white;
        _gamma = gamma;
        _matrix = matrix;
        _ranges = ranges;
        _toSrgb = ColorMath.XyzToLinearSrgb(conversion.Intent == RenderingIntent.AbsoluteColorimetric ? null : white);
        InputCount = family == PdfColorSpaceFamily.CalGray ? 1 : 3;
        OutputCount = DeviceConverter.Outputs(conversion.Target);
    }

    /// <inheritdoc/>
    public int InputCount { get; }

    /// <inheritdoc/>
    public int OutputCount { get; }

    /// <summary>Creates the converter for a CalGray space.</summary>
    public static CieConverter ForCalGray(PdfCalGrayColorSpace space, ColorConversion conversion) =>
        new(PdfColorSpaceFamily.CalGray, conversion, space.WhitePoint, [space.Gamma], [], []);

    /// <summary>Creates the converter for a CalRGB space.</summary>
    public static CieConverter ForCalRgb(PdfCalRgbColorSpace space, ColorConversion conversion) =>
        new(PdfColorSpaceFamily.CalRgb, conversion, space.WhitePoint, [.. space.Gamma], [.. space.Matrix], []);

    /// <summary>Creates the converter for a Lab space.</summary>
    public static CieConverter ForLab(PdfLabColorSpace space, ColorConversion conversion) =>
        new(PdfColorSpaceFamily.Lab, conversion, space.WhitePoint, [], [], [0, 100, .. space.Range]);

    /// <summary>Creates the converter for an ICC profile whose data colour space is Lab: D50 white (the PCS), ranges from the space.</summary>
    public static CieConverter ForIccLab(PdfIccBasedColorSpace space, ColorConversion conversion) =>
        new(PdfColorSpaceFamily.Lab, conversion, CieXyz.D50, [], [], [.. space.Range]);

    /// <inheritdoc/>
    public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        int inputs = InputCount;
        int outputs = OutputCount;
        Span<double> xyz = stackalloc double[3];
        Span<double> rgb = stackalloc double[3];
        for (int i = 0; i < count; i++)
        {
            ToXyz(source.Slice(i * inputs, inputs), xyz);
            ColorMath.Apply(_toSrgb, xyz[0], xyz[1], xyz[2], rgb);
            ColorMath.FromRgb(
                ColorMath.EncodeSrgb(rgb[0]),
                ColorMath.EncodeSrgb(rgb[1]),
                ColorMath.EncodeSrgb(rgb[2]),
                destination.Slice(i * outputs, outputs),
                _conversion);
        }
    }

    private void ToXyz(ReadOnlySpan<float> input, Span<double> xyz)
    {
        switch (_family)
        {
            case PdfColorSpaceFamily.CalGray:
                // §8.6.5.2: X = XW·A^G, Y = YW·A^G, Z = ZW·A^G.
                double a = Math.Pow(ColorMath.Clamp01(input[0]), _gamma[0]);
                xyz[0] = _white.X * a;
                xyz[1] = _white.Y * a;
                xyz[2] = _white.Z * a;
                break;
            case PdfColorSpaceFamily.CalRgb:
                // §8.6.5.3: X = XA·A^GR + XB·B^GG + XC·C^GB, likewise Y and Z; Matrix = [XA YA ZA XB YB ZB XC YC ZC].
                double ag = Math.Pow(ColorMath.Clamp01(input[0]), _gamma[0]);
                double bg = Math.Pow(ColorMath.Clamp01(input[1]), _gamma[1]);
                double cg = Math.Pow(ColorMath.Clamp01(input[2]), _gamma[2]);
                xyz[0] = (_matrix[0] * ag) + (_matrix[3] * bg) + (_matrix[6] * cg);
                xyz[1] = (_matrix[1] * ag) + (_matrix[4] * bg) + (_matrix[7] * cg);
                xyz[2] = (_matrix[2] * ag) + (_matrix[5] * bg) + (_matrix[8] * cg);
                break;
            default:
                // §8.6.5.4: M = (L* + 16) / 116, L = M + a* / 500, N = M − b* / 200; X = XW·g(L), Y = YW·g(M), Z = ZW·g(N).
                double lightness = Clip(input[0], _ranges[0], _ranges[1]);
                double astar = Clip(input[1], _ranges[2], _ranges[3]);
                double bstar = Clip(input[2], _ranges[4], _ranges[5]);
                double mid = (lightness + 16) / 116;
                xyz[0] = _white.X * ColorMath.LabG(mid + (astar / 500));
                xyz[1] = _white.Y * ColorMath.LabG(mid);
                xyz[2] = _white.Z * ColorMath.LabG(mid - (bstar / 200));
                break;
        }
    }

    private static double Clip(double value, double minimum, double maximum) => value >= minimum ? Math.Min(value, maximum) : minimum;
}
