namespace Broadside.Graphics.Colors;

/// <summary>Converts DeviceGray, DeviceRGB or DeviceCMYK colours to a device colour model.</summary>
/// <remarks>
/// ISO 32000-2 §10.4.2: gray to RGB r = g = b = gray (§10.4.2.2); RGB to gray 0.3 r + 0.59 g + 0.11 b; gray to CMYK (0, 0, 0,
/// 1 − gray) and CMYK to gray 1 − min(1, 0.3 c + 0.59 m + 0.11 y + k) (§10.4.2.4); RGB to CMYK with black generation and
/// undercolour removal (§10.4.2.3); CMYK to RGB either by §10.4.2.5 or by the SWOP characterisation (<see cref="CmykConversion"/>).
/// </remarks>
internal sealed class DeviceConverter(PdfColorSpaceFamily source, ColorConversion conversion, CmykConversion cmyk) : IColorConverter
{
    /// <inheritdoc/>
    public int InputCount { get; } = source switch
    {
        PdfColorSpaceFamily.DeviceRgb => 3,
        PdfColorSpaceFamily.DeviceCmyk => 4,
        _ => 1,
    };

    /// <inheritdoc/>
    public int OutputCount { get; } = Outputs(conversion.Target);

    /// <summary>The number of components of a device colour model.</summary>
    /// <param name="target">The model.</param>
    /// <returns>1, 3 or 4.</returns>
    public static int Outputs(DeviceColorModel target) => target switch
    {
        DeviceColorModel.Gray => 1,
        DeviceColorModel.Cmyk => 4,
        _ => 3,
    };

    /// <inheritdoc/>
    public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        int inputs = InputCount;
        int outputs = OutputCount;
        for (int i = 0; i < count; i++)
        {
            ConvertOne(source.Slice(i * inputs, inputs), destination.Slice(i * outputs, outputs));
        }
    }

    private void ConvertOne(ReadOnlySpan<float> input, Span<float> output)
    {
        switch (source)
        {
            case PdfColorSpaceFamily.DeviceGray:
                double gray = ColorMath.Clamp01(input[0]);
                if (conversion.Target == DeviceColorModel.Cmyk)
                {
                    output[0] = output[1] = output[2] = 0;
                    output[3] = (float)(1 - gray);
                }
                else
                {
                    ColorMath.FromRgb(gray, gray, gray, output, conversion);
                }

                break;
            case PdfColorSpaceFamily.DeviceRgb:
                ColorMath.FromRgb(input[0], input[1], input[2], output, conversion);
                break;
            default:
                ConvertCmyk(input, output);
                break;
        }
    }

    private void ConvertCmyk(ReadOnlySpan<float> input, Span<float> output)
    {
        double c = ColorMath.Clamp01(input[0]);
        double m = ColorMath.Clamp01(input[1]);
        double y = ColorMath.Clamp01(input[2]);
        double k = ColorMath.Clamp01(input[3]);
        if (conversion.Target == DeviceColorModel.Cmyk)
        {
            output[0] = (float)c;
            output[1] = (float)m;
            output[2] = (float)y;
            output[3] = (float)k;
            return;
        }

        if (cmyk == CmykConversion.Classic)
        {
            if (conversion.Target == DeviceColorModel.Gray)
            {
                output[0] = (float)(1 - Math.Min(1, (0.3 * c) + (0.59 * m) + (0.11 * y) + k));
            }
            else
            {
                output[0] = (float)(1 - Math.Min(1, c + k));
                output[1] = (float)(1 - Math.Min(1, m + k));
                output[2] = (float)(1 - Math.Min(1, y + k));
            }

            return;
        }

        Span<double> rgb = stackalloc double[3];
        CmykCharacterization.ToRgb(c, m, y, k, rgb);
        ColorMath.FromRgb(rgb[0], rgb[1], rgb[2], output, conversion);
    }
}
