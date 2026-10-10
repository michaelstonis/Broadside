using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Images;
using Broadside.Parsing;

namespace Broadside.Filters.Jpx;

/// <summary>
/// One codestream component over the whole image at its own resolution: unsigned samples after the DC level shift (G.1.2) and
/// clipping, reduced to 16 bits when deeper. Pooled until <see cref="Dispose"/>.
/// </summary>
internal sealed class JpxPlane : IDisposable
{
    private ushort[] _samples;

    public JpxPlane(JpxImageSize size, int component)
    {
        JpxComponentInfo info = size.Components[component];
        X0 = JpxMath.CeilDivide(size.OriginX, info.Dx);
        Y0 = JpxMath.CeilDivide(size.OriginY, info.Dy);
        Width = (int)(JpxMath.CeilDivide(size.Width, info.Dx) - X0);
        Height = (int)(JpxMath.CeilDivide(size.Height, info.Dy) - Y0);
        Dx = info.Dx;
        Dy = info.Dy;
        SourceDepth = info.Depth;
        Signed = info.Signed;
        Depth = Math.Min(info.Depth, 16);
        _samples = ArrayPool<ushort>.Shared.Rent(Math.Max(1, Width * Height));
        _samples.AsSpan(0, Width * Height).Fill((ushort)(info.Depth > 16 ? 1 << 15 : 1 << (info.Depth - 1)));
    }

    /// <summary>Gets the component's first sample on the reference grid divided by its separations (B-2).</summary>
    public long X0 { get; }

    public long Y0 { get; }

    public int Width { get; }

    public int Height { get; }

    public int Dx { get; }

    public int Dy { get; }

    /// <summary>Gets the component's precision as coded.</summary>
    public int SourceDepth { get; }

    public bool Signed { get; }

    /// <summary>Gets the stored precision, at most 16.</summary>
    public int Depth { get; }

    public ReadOnlySpan<ushort> Samples => _samples.AsSpan(0, Width * Height);

    /// <summary>The number of samples of component <paramref name="component"/> over the image.</summary>
    public static long Area(JpxImageSize size, int component)
    {
        JpxComponentInfo info = size.Components[component];
        long width = JpxMath.CeilDivide(size.Width, info.Dx) - JpxMath.CeilDivide(size.OriginX, info.Dx);
        long height = JpxMath.CeilDivide(size.Height, info.Dy) - JpxMath.CeilDivide(size.OriginY, info.Dy);
        return width * height;
    }

    /// <summary>Writes a decoded tile-component (integers before the DC level shift) at its place.</summary>
    public void Write(JpxTileComponent component, int[] buffer)
    {
        long shift = 1L << (SourceDepth - 1);
        long max = (1L << SourceDepth) - 1;
        int left = (int)(component.X0 - X0);
        int top = (int)(component.Y0 - Y0);
        for (int y = 0; y < component.Height; y++)
        {
            int row = top + y;
            if (row < 0 || row >= Height)
            {
                continue;
            }

            Span<ushort> target = _samples.AsSpan(row * Width, Width);
            ReadOnlySpan<int> source = buffer.AsSpan(y * component.Width, component.Width);
            for (int x = 0; x < source.Length; x++)
            {
                int column = left + x;
                if ((uint)column >= (uint)Width)
                {
                    continue;
                }

                long value = Math.Clamp(source[x] + shift, 0, max);
                if (SourceDepth > 16)
                {
                    value = ((value * 65535) + (max / 2)) / max;
                }

                target[column] = (ushort)value;
            }
        }
    }

    public void Dispose()
    {
        ArrayPool<ushort>.Shared.Return(_samples);
        _samples = [];
    }
}

/// <summary>
/// Composes the image's channels from the component planes, row by row: sub-sampled components replicated over the image grid,
/// palette columns looked up, channels scaled to the output precision, the colour conversion applied, and the opacity channel
/// written to the alpha plane.
/// </summary>
/// <remarks>
/// ITU-T T.800 I.5.3.4 (palette), I.5.3.5 (component mapping) and G.4 (sub-sampled components); the YCbCr equations are G.3.2's
/// inverse ICT with the chroma offset by half the range (sYCC, IEC 61966-2-1 Annex G); CIELab follows the JPX parameters (ISO/IEC
/// 15444-2 Annex M) and converts through XYZ (D50, Bradford-adapted to D65) to sRGB.
/// </remarks>
internal static class JpxComposer
{
    public static void Compose(JpxImageSize size, JpxOutputPlan plan, JpxPlane?[] planes, DecodedImageBuilder builder, DecodedImageBuilder? alpha, JpxReporter reporter)
    {
        int width = builder.Width;
        int height = builder.Height;
        int channels = builder.Components;
        int bits = builder.BitsPerComponent;
        int[] row = ArrayPool<int>.Shared.Rent(Math.Max(1, width * channels));
        int[][] columns = new int[planes.Length][];
        try
        {
            for (int c = 0; c < planes.Length; c++)
            {
                if (planes[c] is { } plane)
                {
                    columns[c] = ArrayPool<int>.Shared.Rent(width);
                    Map(columns[c].AsSpan(0, width), size.OriginX, plane.Dx, plane.X0, plane.Width);
                }
            }

            bool clamped = false;
            for (int y = 0; y < height; y++)
            {
                Span<int> values = row.AsSpan(0, width * channels);
                for (int ch = 0; ch < channels; ch++)
                {
                    JpxChannelSource? source = ch < plan.Colors.Length ? plan.Colors[ch] : null;
                    if (source is not { } channel || planes[channel.Component] is not { } plane)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            values[(x * channels) + ch] = 0;
                        }

                        continue;
                    }

                    clamped |= ReadChannel(plane, channel, plan.Palette, columns[channel.Component], y, size.OriginY, bits, values, ch, channels);
                }

                Convert(plan, values, channels, bits);
                Store(builder.GetRow(y), values, builder.StorageBits);
                if (alpha is not null && plan.Alpha is { } opacity && planes[opacity.Component] is { } alphaPlane)
                {
                    Span<int> alphaValues = row.AsSpan(0, width);
                    clamped |= ReadChannel(alphaPlane, opacity, plan.Palette, columns[opacity.Component], y, size.OriginY, alpha.BitsPerComponent, alphaValues, 0, 1);
                    Store(alpha.GetRow(y), alphaValues, alpha.StorageBits);
                }
            }

            if (clamped)
            {
                reporter.Report(DiagnosticCodes.JpxPaletteIndexOutOfRange, DiagnosticSeverity.Warning, "A JPEG 2000 component holds palette indices past the palette's last entry; they take the last entry.");
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(row);
            foreach (int[]? map in columns)
            {
                if (map is not null)
                {
                    ArrayPool<int>.Shared.Return(map);
                }
            }
        }
    }

    /// <summary>
    /// For each image sample along one axis, the index of the component sample at or before it: the image sample at grid position g
    /// takes component sample max(floor(g / d), first) - first, first = ceil(origin / d).
    /// </summary>
    private static void Map(Span<int> map, long origin, int separation, long first, int count)
    {
        for (int i = 0; i < map.Length; i++)
        {
            long index = Math.Max((origin + i) / separation, first) - first;
            map[i] = (int)Math.Clamp(index, 0, Math.Max(count - 1, 0));
        }
    }

    /// <summary>Reads one channel of image row <paramref name="y"/> into every <paramref name="stride"/>-th value from <paramref name="offset"/>, scaled to <paramref name="bits"/>.</summary>
    /// <returns><see langword="true"/> when a palette index was clamped.</returns>
    private static bool ReadChannel(JpxPlane plane, JpxChannelSource channel, JpxPalette? palette, int[] columns, int y, long originY, int bits, Span<int> values, int offset, int stride)
    {
        if (plane.Width == 0 || plane.Height == 0)
        {
            // A component whose separation exceeds the image area has no samples on it (B-2): its channel is zero.
            for (int i = offset; i < values.Length; i += stride)
            {
                values[i] = 0;
            }

            return false;
        }

        long componentRow = Math.Max((originY + y) / plane.Dy, plane.Y0) - plane.Y0;
        int rowIndex = (int)Math.Clamp(componentRow, 0, Math.Max(plane.Height - 1, 0));
        ReadOnlySpan<ushort> samples = plane.Samples.Slice(rowIndex * plane.Width, plane.Width);
        int width = values.Length / stride;
        bool clamped = false;
        if (channel.Column < 0 || palette is null)
        {
            int depth = plane.Depth;
            long max = (1L << depth) - 1;
            long outMax = (1L << bits) - 1;
            for (int x = 0; x < width; x++)
            {
                long value = samples[columns[x]];
                values[(x * stride) + offset] = depth == bits ? (int)value : (int)(((value * outMax) + (max / 2)) / max);
            }

            return false;
        }

        int column = channel.Column;
        int sourceDepth = palette.Depths[column];
        long paletteShift = palette.Signed[column] ? 1L << (sourceDepth - 1) : 0;
        long paletteMax = (1L << sourceDepth) - 1;
        long target = (1L << bits) - 1;
        long indexOffset = plane.Signed ? 1L << (plane.Depth - 1) : 0;
        for (int x = 0; x < width; x++)
        {
            long index = samples[columns[x]] - indexOffset;
            if (index < 0 || index >= palette.Entries)
            {
                clamped = true;
                index = Math.Clamp(index, 0, palette.Entries - 1);
            }

            long value = Math.Clamp(palette.Values[(index * palette.Columns) + column] + paletteShift, 0, paletteMax);
            values[(x * stride) + offset] = sourceDepth == bits ? (int)value : (int)(((value * target) + (paletteMax / 2)) / paletteMax);
        }

        return clamped;
    }

    private static void Convert(JpxOutputPlan plan, Span<int> values, int channels, int bits)
    {
        if (plan.Conversion == JpxColorConversion.None || channels < 3)
        {
            return;
        }

        int max = (1 << bits) - 1;
        int half = 1 << (bits - 1);
        switch (plan.Conversion)
        {
            case JpxColorConversion.Ycc:
            case JpxColorConversion.Ycck:
                for (int i = 0; i + 2 < values.Length; i += channels)
                {
                    int yy = values[i];
                    int cb = values[i + 1] - half;
                    int cr = values[i + 2] - half;
                    int r = Math.Clamp(yy + (int)(1.402f * cr), 0, max);
                    int g = Math.Clamp(yy - (int)((0.344f * cb) + (0.714f * cr)), 0, max);
                    int b = Math.Clamp(yy + (int)(1.772f * cb), 0, max);
                    bool invert = plan.Conversion == JpxColorConversion.Ycck;
                    values[i] = invert ? max - r : r;
                    values[i + 1] = invert ? max - g : g;
                    values[i + 2] = invert ? max - b : b;
                }

                break;
            case JpxColorConversion.Cmy:
                for (int i = 0; i + 2 < values.Length; i += channels)
                {
                    values[i] = max - values[i];
                    values[i + 1] = max - values[i + 1];
                    values[i + 2] = max - values[i + 2];
                }

                break;
            case JpxColorConversion.Lab:
                ConvertLab(plan.LabParameters, values, channels, bits);
                break;
        }
    }

    /// <summary>CIELab samples (ISO/IEC 15444-2 M.11.7.4: L, a, b with ranges R and offsets O) to sRGB at the same precision.</summary>
    private static void ConvertLab(uint[] parameters, Span<int> values, int channels, int bits)
    {
        double max = (1 << bits) - 1;
        bool given = parameters.Length >= 6;
        double rl = given ? parameters[0] : 100;
        double ol = given ? parameters[1] : 0;
        double ra = given ? parameters[2] : 170;
        double oa = given ? parameters[3] : 1 << (bits - 1);
        double rb = given ? parameters[4] : 200;
        double ob = given ? parameters[5] : (1 << (bits - 2)) + (1 << (bits - 3));
        double minL = -(rl * ol) / max;
        double minA = -(ra * oa) / max;
        double minB = -(rb * ob) / max;
        for (int i = 0; i + 2 < values.Length; i += channels)
        {
            double l = minL + (values[i] * rl / max);
            double a = minA + (values[i + 1] * ra / max);
            double b = minB + (values[i + 2] * rb / max);

            // CIE Lab (D50) to XYZ.
            double fy = (l + 16) / 116;
            double fx = fy + (a / 500);
            double fz = fy - (b / 200);
            double x = 0.9642 * Inverse(fx);
            double yy = Inverse(fy);
            double z = 0.8249 * Inverse(fz);

            // Bradford-adapted D50 XYZ to linear sRGB (D65), then the sRGB transfer function.
            double r = (3.1338561 * x) - (1.6168667 * yy) - (0.4906146 * z);
            double g = (-0.9787684 * x) + (1.9161415 * yy) + (0.0334540 * z);
            double bl = (0.0719453 * x) - (0.2289914 * yy) + (1.4052427 * z);
            values[i] = (int)Math.Round(Encode(r) * max);
            values[i + 1] = (int)Math.Round(Encode(g) * max);
            values[i + 2] = (int)Math.Round(Encode(bl) * max);
        }

        static double Inverse(double t) => t > 6.0 / 29 ? t * t * t : 3 * (6.0 / 29) * (6.0 / 29) * (t - (4.0 / 29));

        static double Encode(double v)
        {
            v = Math.Clamp(v, 0, 1);
            return v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;
        }
    }

    private static void Store(Span<byte> row, ReadOnlySpan<int> values, int storage)
    {
        switch (storage)
        {
            case 8:
                for (int i = 0; i < values.Length; i++)
                {
                    row[i] = (byte)values[i];
                }

                break;
            case 16:
                for (int i = 0; i < values.Length; i++)
                {
                    row[2 * i] = (byte)(values[i] >> 8);
                    row[(2 * i) + 1] = (byte)values[i];
                }

                break;
            default:
                row.Clear();
                for (int i = 0; i < values.Length; i++)
                {
                    int bit = i * storage;
                    row[bit >> 3] |= (byte)(values[i] << (8 - storage - (bit & 7)));
                }

                break;
        }
    }
}
