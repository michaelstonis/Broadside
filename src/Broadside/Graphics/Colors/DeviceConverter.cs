using System.Runtime.InteropServices;

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

    /// <summary>
    /// Whether every colour converts to itself: DeviceRGB to RGB, DeviceCMYK to CMYK and DeviceGray to gray, where conversion only
    /// clips to 0 to 1, so 8-bit samples need no conversion at all (see <see cref="ManagedPipeline"/>).
    /// </summary>
    public bool IsIdentity => (source, conversion.Target) is (PdfColorSpaceFamily.DeviceRgb, DeviceColorModel.Rgb)
        or (PdfColorSpaceFamily.DeviceCmyk, DeviceColorModel.Cmyk) or (PdfColorSpaceFamily.DeviceGray, DeviceColorModel.Gray);

    /// <inheritdoc/>
    public void Convert(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        int inputs = InputCount;
        int outputs = OutputCount;
        if (inputs == 4 && outputs < 4 && cmyk == CmykConversion.Characterized)
        {
            ConvertCharacterized(source, destination, count);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            ConvertOne(source.Slice(i * inputs, inputs), destination.Slice(i * outputs, outputs));
        }
    }

    /// <summary>
    /// CMYK through the SWOP characterisation, the costly conversion (a cubic polynomial per channel): a colour equal to the one
    /// before it (a flat area, a group's backdrop) takes that one's result, bit for bit what converting it again gives.
    /// </summary>
    private void ConvertCharacterized(ReadOnlySpan<float> source, Span<float> destination, int count)
    {
        int outputs = OutputCount;
        ReadOnlySpan<uint> bits = MemoryMarshal.Cast<float, uint>(source[..(count * 4)]);
        (uint c0, uint m0, uint y0, uint k0) = (0, 0, 0, 0);
        (float r0, float g0, float b0) = (0, 0, 0);
        for (int i = 0; i < count; i++)
        {
            int at = i * 4;
            (uint c1, uint m1, uint y1, uint k1) = (bits[at], bits[at + 1], bits[at + 2], bits[at + 3]);
            Span<float> output = destination.Slice(i * outputs, outputs);
            if (i == 0 || c1 != c0 || m1 != m0 || y1 != y0 || k1 != k0)
            {
                ConvertCmyk(source.Slice(at, 4), output);
                (c0, m0, y0, k0) = (c1, m1, y1, k1);
                r0 = output[0];
                (g0, b0) = outputs == 3 ? (output[1], output[2]) : (0, 0);
                continue;
            }

            output[0] = r0;
            if (outputs == 3)
            {
                output[1] = g0;
                output[2] = b0;
            }
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

        CmykCharacterization.ToRgb(c, m, y, k, out double r, out double g, out double b);
        ColorMath.FromRgb(r, g, b, output, conversion);
    }
}
