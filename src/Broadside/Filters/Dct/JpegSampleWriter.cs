using Broadside.Diagnostics;
using Broadside.Images;
using Broadside.Parsing;

namespace Broadside.Filters.Dct;

/// <summary>
/// The output stage of the JPEG decoder: turns MCU rows of coefficients into output rows (inverse DCT per block at the
/// component's scaled size, upsampling by replication, the colour transform, the 12-bit reduction), and chooses the colour
/// transform.
/// </summary>
/// <remarks>
/// ITU-T T.81 §A.3.3 and §A.3.4 (IDCT, dequantization), §A.2.4 (padding removed); Adobe Technical Note #5116 §15 Table 2
/// (upsampling by replication) and §18 (APP14); ISO 32000-2 §7.4.8 Table 13 (colour transform) and §8.9.5.1 Table 87 (8-bit
/// output). The IDCT kernels are <see cref="InverseDct"/>, the colour kernels <see cref="JpegColor"/>.
/// </remarks>
internal static class JpegSampleWriter
{
    /// <summary>Chooses the colour transform from the APP14 segment, the <c>ColorTransform</c> parameter and the component identifiers.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.4.8, Table 13: the APP14 transform flag wins; otherwise the parameter; otherwise 1 for three components and 0
    /// for others; ignored for one or two components. Compatibility fallback, a documented deviation from Table 13's "shall be 1":
    /// three components identified as R, G, B (what libjpeg writes for RGB data, and libjpeg and pdf.js read so) are not transformed
    /// without APP14 or the parameter, and <c>DctColorTransformInferred</c> (Information) says so; the one real-world file of this
    /// kind in the corpora (pdf.js issue11931.pdf) differs from libjpeg-turbo by up to 135 per sample when transformed. An APP14
    /// code that does not fit the
    /// component count (2 or more with three components, 1 or more than 2 with four) is reported and read as YCbCr or YCCK, as
    /// libjpeg, pdf.js and PDFBox do (Adobe Technical Note #5116 §18: 1 = YCbCr, 2 = YCCK).
    /// </remarks>
    /// <param name="frame">The image, after its headers were read.</param>
    /// <param name="colorTransform">The DecodeParms <c>ColorTransform</c> value, or -1 when absent.</param>
    public static void SelectColorTransform(JpegFrame frame, int colorTransform)
    {
        if (frame.Adobe && ((frame.ComponentCount == 3 && frame.AdobeTransform > AdobeColorTransform.YCbCr) || (frame.ComponentCount == 4 && frame.AdobeTransform is AdobeColorTransform.YCbCr or > AdobeColorTransform.YCCK)))
        {
            frame.Report(
                DiagnosticCodes.DctAdobeTransformInvalid,
                DiagnosticSeverity.Warning,
                JpegFrame.Invariant($"The APP14 transform code {(int)frame.AdobeTransform} does not fit {frame.ComponentCount} components; {(frame.ComponentCount == 3 ? "YCbCr" : "YCCK")} is assumed."));
        }

        bool rgbIdentifiers = frame.ComponentCount == 3 && frame.Components[0].Id == 'R' && frame.Components[1].Id == 'G' && frame.Components[2].Id == 'B';
        if (rgbIdentifiers && !frame.Adobe && colorTransform < 0)
        {
            frame.Report(
                DiagnosticCodes.DctColorTransformInferred,
                DiagnosticSeverity.Information,
                "The three DCT components are identified R, G, B and there is neither an APP14 segment nor a ColorTransform parameter; they are read as RGB (as libjpeg does), not with the default ColorTransform 1 of ISO 32000-2 Table 13.");
        }

        bool transform = frame.ComponentCount switch
        {
            3 when frame.Adobe => frame.AdobeTransform != AdobeColorTransform.Unknown,
            3 when colorTransform >= 0 => colorTransform == 1,
            3 => !rgbIdentifiers,
            4 when frame.Adobe => frame.AdobeTransform != AdobeColorTransform.Unknown,
            4 => colorTransform == 1,
            _ => false,
        };

        frame.Output = frame.ComponentCount switch
        {
            1 => JpegOutputKind.Gray,
            3 when transform => JpegOutputKind.YccToRgb,
            4 when transform => JpegOutputKind.YcckToCmyk,
            _ => JpegOutputKind.Interleaved,
        };
    }

    /// <summary>Turns one MCU row of coefficients into output rows: IDCT per block, replication upsampling, colour transform.</summary>
    /// <remarks>
    /// ITU-T T.81 §A.3.3, §A.3.4 (IDCT, dequantization), §A.2.4 (padding removed); Adobe Technical Note #5116 §15 Table 2
    /// (upsampling by replication, which is libjpeg's box filter for integral factors); ISO 32000-2 §7.4.8 Table 13.
    /// </remarks>
    /// <typeparam name="TSink">The row destination.</typeparam>
    /// <param name="frame">The image.</param>
    /// <param name="mcuRow">The MCU row.</param>
    /// <param name="sink">The destination.</param>
    public static void EmitMcuRow<TSink>(JpegFrame frame, int mcuRow, ref TSink sink)
        where TSink : struct, IJpegRowSink
    {
        if (frame.Precision == 12)
        {
            EmitMcuRow12(frame, mcuRow, ref sink);
            return;
        }

        for (int c = 0; c < frame.ComponentCount; c++)
        {
            JpegComponent component = frame.Components[c];
            int stride = component.StripStride;
            int size = component.ScaledSize;
            byte[] strip = component.Strip!;
            for (int v = 0; v < component.V; v++)
            {
                int row = (mcuRow * component.V) + v;
                if (row >= component.BlocksHigh)
                {
                    break;
                }

                for (int column = 0; column < component.BlocksWide; column++)
                {
                    Span<short> block = component.Block(row, column);
                    Span<byte> target = strip.AsSpan((v * size * stride) + (column * size));
                    switch (size)
                    {
                        case 8:
                            InverseDct.Transform(block, component.Quantization, target, stride);
                            break;
                        case 4:
                            InverseDct.Transform4(block, component.Quantization, target, stride);
                            break;
                        case 2:
                            InverseDct.Transform2(block, component.Quantization, target, stride);
                            break;
                        default:
                            InverseDct.Transform1(block, component.Quantization, target);
                            break;
                    }
                }
            }
        }

        int top = mcuRow * frame.MinScaled * frame.VMax;
        int bottom = Math.Min(top + (frame.MinScaled * frame.VMax), frame.OutputHeight);
        int width = frame.OutputWidth;
        for (int y = top; y < bottom; y++)
        {
            int local = y - top;
            Span<byte> output = sink.BeginRow(y);
            ReadOnlySpan<byte> line0 = Line(frame, frame.Components[0], local, width);
            switch (frame.Output)
            {
                case JpegOutputKind.Gray:
                    line0.CopyTo(output);
                    break;
                case JpegOutputKind.YccToRgb:
                    JpegColor.YccToRgb(line0, Line(frame, frame.Components[1], local, width), Line(frame, frame.Components[2], local, width), output[..(width * 3)]);
                    break;
                case JpegOutputKind.YcckToCmyk:
                    JpegColor.YcckToCmyk(line0, Line(frame, frame.Components[1], local, width), Line(frame, frame.Components[2], local, width), Line(frame, frame.Components[3], local, width), output[..(width * 4)]);
                    break;
                default:
                    int count = frame.ComponentCount;
                    for (int c = 0; c < count; c++)
                    {
                        ReadOnlySpan<byte> line = c == 0 ? line0 : Line(frame, frame.Components[c], local, width);
                        for (int x = 0, o = c; x < width; x++, o += count)
                        {
                            output[o] = line[x];
                        }
                    }

                    break;
            }

            sink.EndRow();
        }
    }

    /// <summary>Returns a component's samples for output row <paramref name="local"/> of the MCU row, replicated to the image width.</summary>
    private static ReadOnlySpan<byte> Line(JpegFrame frame, JpegComponent component, int local, int width)
    {
        int stride = component.StripStride;
        int size = component.ScaledSize;
        int full = frame.MinScaled;
        ReadOnlySpan<byte> source = component.Strip.AsSpan(local * component.V * size / (frame.VMax * full) * stride, stride);
        if (component.Upsampled is not { } upsampled)
        {
            return source[..width];
        }

        Span<byte> target = upsampled.AsSpan(0, width);
        int h = component.H * size;
        int hMax = frame.HMax * full;
        if (hMax == 2 * h)
        {
            for (int x = 0; x < width; x++)
            {
                target[x] = source[x >> 1];
            }
        }
        else
        {
            for (int x = 0; x < width; x++)
            {
                target[x] = source[x * h / hMax];
            }
        }

        return target;
    }

    /// <summary>The 12-bit counterpart of <see cref="EmitMcuRow"/>: the transforms run on 12-bit samples, which are then reduced to 8 bits.</summary>
    /// <remarks>ITU-T T.81 §A.3.1 and §F.2.1.5 (12-bit level shift and clamp); ISO 32000-2 §8.9.5.1 Table 87 (8-bit output).</remarks>
    private static void EmitMcuRow12<TSink>(JpegFrame frame, int mcuRow, ref TSink sink)
        where TSink : struct, IJpegRowSink
    {
        for (int c = 0; c < frame.ComponentCount; c++)
        {
            JpegComponent component = frame.Components[c];
            int stride = component.StripStride;
            int size = component.ScaledSize;
            ushort[] strip = component.Strip12!;
            for (int v = 0; v < component.V; v++)
            {
                int row = (mcuRow * component.V) + v;
                if (row >= component.BlocksHigh)
                {
                    break;
                }

                for (int column = 0; column < component.BlocksWide; column++)
                {
                    Span<short> block = component.Block(row, column);
                    Span<ushort> target = strip.AsSpan((v * size * stride) + (column * size));
                    switch (size)
                    {
                        case 8:
                            InverseDct.Transform12(block, component.Quantization, target, stride);
                            break;
                        case 4:
                            InverseDct.Transform4_12(block, component.Quantization, target, stride);
                            break;
                        case 2:
                            InverseDct.Transform2_12(block, component.Quantization, target, stride);
                            break;
                        default:
                            InverseDct.Transform1_12(block, component.Quantization, target);
                            break;
                    }
                }
            }
        }

        int top = mcuRow * frame.MinScaled * frame.VMax;
        int bottom = Math.Min(top + (frame.MinScaled * frame.VMax), frame.OutputHeight);
        int width = frame.OutputWidth;
        for (int y = top; y < bottom; y++)
        {
            int local = y - top;
            Span<byte> output = sink.BeginRow(y);
            ReadOnlySpan<ushort> line0 = Line12(frame, frame.Components[0], local, width);
            switch (frame.Output)
            {
                case JpegOutputKind.Gray:
                    JpegColor.Reduce12(line0, output, 1);
                    break;
                case JpegOutputKind.YccToRgb:
                    JpegColor.YccToRgb12(line0, Line12(frame, frame.Components[1], local, width), Line12(frame, frame.Components[2], local, width), output[..(width * 3)]);
                    break;
                case JpegOutputKind.YcckToCmyk:
                    JpegColor.YcckToCmyk12(line0, Line12(frame, frame.Components[1], local, width), Line12(frame, frame.Components[2], local, width), Line12(frame, frame.Components[3], local, width), output[..(width * 4)]);
                    break;
                default:
                    for (int c = 0; c < frame.ComponentCount; c++)
                    {
                        JpegColor.Reduce12(c == 0 ? line0 : Line12(frame, frame.Components[c], local, width), output[c..], frame.ComponentCount);
                    }

                    break;
            }

            sink.EndRow();
        }
    }

    /// <summary>The 12-bit counterpart of <see cref="Line"/>.</summary>
    private static ReadOnlySpan<ushort> Line12(JpegFrame frame, JpegComponent component, int local, int width)
    {
        int stride = component.StripStride;
        int size = component.ScaledSize;
        int full = frame.MinScaled;
        ReadOnlySpan<ushort> source = component.Strip12.AsSpan(local * component.V * size / (frame.VMax * full) * stride, stride);
        if (component.Upsampled12 is not { } upsampled)
        {
            return source[..width];
        }

        Span<ushort> target = upsampled.AsSpan(0, width);
        int h = component.H * size;
        int hMax = frame.HMax * full;
        for (int x = 0; x < width; x++)
        {
            target[x] = source[x * h / hMax];
        }

        return target;
    }
}
