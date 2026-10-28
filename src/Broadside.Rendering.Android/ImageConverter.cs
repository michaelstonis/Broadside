using System.Buffers;
using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Rendering.Android;

/// <summary>
/// Converts display-list images to Android bitmaps: colour samples to premultiplied BGRA through colour management at the image's own
/// resolution or the reduction a viewer draws it at (issue #362), with any mask of the image's size folded into the alpha, and masks
/// to alpha-only images, as the software rasterizer's image source converts them.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9.5.2 (Decode, Table 88), §8.6.6.3 (Indexed: samples are indices), §8.9.3 (sample layout); masks: §8.9.6.2
/// (stencil masks), §8.9.6.3 (explicit masks), §8.9.6.4 (colour key, on the raw samples), §11.6.5.2 (soft masks; Matte removed
/// before conversion, Table 144), §7.4.9 and §8.9.5.1 Table 87 (SMaskInData). Runs only when a backend meets an image it has not
/// kept, so its scratch arrays are rented per conversion.
/// </remarks>
internal static class ImageConverter
{
    /// <summary>Whether the image's soft mask has the image's size, so that its opacity is folded into the converted pixels.</summary>
    public static bool FoldsSoftMask(DisplayImage image) =>
        image.SoftMask is { } soft && soft.Width == image.Width && soft.Height == image.Height;

    /// <summary>Whether the image's explicit mask has the image's size, so that it is folded into the converted pixels.</summary>
    public static bool FoldsMask(DisplayImage image) =>
        image.SoftMask is null && image.Mask is { } mask && mask.Width == image.Width && mask.Height == image.Height;

    /// <summary>
    /// Returns whether the image's soft mask folds into its converted <paramref name="samples"/>, and its samples to fold: at the
    /// image's own resolution a soft mask of its size always folds (<paramref name="softSamples"/> null when the mask cannot be
    /// decoded: its rows read as zero); reduced (issue #362), only when the mask's samples have the same reduction, so that the grids
    /// align (§11.6.5.2).
    /// </summary>
    public static bool FoldsSoftMask(DisplayImage image, DisplayImageSamples samples, out DisplayImageSamples? softSamples)
    {
        softSamples = null;
        if (!FoldsSoftMask(image))
        {
            return false;
        }

        softSamples = image.SoftMask!.GetSamplesAt(samples.Reduction);
        return samples.Reduction == 0 || (softSamples is { } soft && soft.Reduction == samples.Reduction);
    }

    /// <summary>Whether the image's explicit mask folds into its converted <paramref name="samples"/>: of the image's size, at its own resolution.</summary>
    public static bool FoldsMask(DisplayImage image, DisplayImageSamples samples) => samples.Reduction == 0 && FoldsMask(image);

    /// <summary>
    /// Converts a colour image's samples, at its own resolution or a reduction (issue #362), to premultiplied BGRA; null when they
    /// have more pixels than a managed array can hold. The soft mask folds when <paramref name="foldSoft"/> is set, through
    /// <paramref name="softSamples"/> (null at the image's resolution: its rows are read), the explicit mask when
    /// <paramref name="foldMask"/> is.
    /// </summary>
    public static PixelImage? Convert(DisplayImage image, DisplayImageSamples samples, bool foldSoft, DisplayImageSamples? softSamples, bool foldMask, PdfColorConverter converter, PdfColorConverter? baseConverter)
    {
        int width = samples.Width;
        int height = samples.Height;
        if ((long)width * height * 4 > Array.MaxLength)
        {
            return null;
        }

        int components = image.Components;
        int values = width * components;
        PdfColorSpace space = image.ColorSpace;
        var indexed = space as PdfIndexedColorSpace;
        ImageDecodeMap map = image.DecodeMapFor(samples);
        int storageBits = samples.StorageBits;
        Span<ComponentRange> ranges = stackalloc ComponentRange[components];
        for (int c = 0; c < components; c++)
        {
            ranges[c] = space.GetComponentRange(c);
        }

        bool colorKey = !image.ColorKey.IsEmpty;
        bool matte = foldSoft && !image.Matte.IsEmpty
            && (indexed is null ? image.Matte.Length == components : baseConverter is not null && image.Matte.Length == indexed.Base.ComponentCount);
        bool alpha = foldSoft || foldMask || colorKey;
        RowKind kind = matte ? (indexed is null ? RowKind.Values : RowKind.TableValues)
            : indexed is not null ? RowKind.Indices
            : storageBits <= 8 && IsDefault(map, ranges) ? RowKind.Bytes
            : RowKind.Values;
        int colorComponents = kind == RowKind.TableValues ? indexed!.Base.ComponentCount : components;
        int colorValues = width * colorComponents;
        Span<ComponentRange> baseRanges = stackalloc ComponentRange[kind == RowKind.TableValues ? colorComponents : 0];
        for (int c = 0; c < baseRanges.Length; c++)
        {
            baseRanges[c] = indexed!.Base.GetComponentRange(c);
        }

        int maskWidth = foldSoft ? width : 0;
        byte[] pixels = ArrayPool<byte>.Shared.Rent(width * height * 4);
        ushort[] raw = ArrayPool<ushort>.Shared.Rent(Math.Max(values, maskWidth) + 1);
        byte[] bytes = ArrayPool<byte>.Shared.Rent(values + 1);
        byte[] rgb = ArrayPool<byte>.Shared.Rent((width * 3) + 1);
        float[] floats = ArrayPool<float>.Shared.Rent(Math.Max(colorValues, width * 3) + 1);
        float[] rgbValues = ArrayPool<float>.Shared.Rent((width * 3) + 1);
        byte[] opacity = ArrayPool<byte>.Shared.Rent(width + 1);
        float[] alphaValues = ArrayPool<float>.Shared.Rent(Math.Max(width, maskWidth) + 1);
        try
        {
            ReadOnlySpan<byte> table = kind == RowKind.TableValues ? image.ColorTable : default;
            int highValue = indexed?.HighValue ?? 0;
            Span<byte> rgbRow = rgb.AsSpan(0, width * 3);
            Span<ushort> rawRow = raw.AsSpan(0, values);
            Span<byte> byteRow = bytes.AsSpan(0, values);
            Span<byte> opacityRow = opacity.AsSpan(0, width);
            for (int y = 0; y < height; y++)
            {
                ReadOnlySpan<byte> row = samples.GetRow(y);
                if (kind != RowKind.Bytes || colorKey)
                {
                    ImageRows.Unpack(row, storageBits, values, rawRow);
                }

                if (foldSoft && softSamples is { } soft)
                {
                    SoftRow(image.SoftMask!, soft, y, opacityRow, raw, alphaValues);
                }
                else if (foldSoft)
                {
                    SoftRow(image.SoftMask!, y, opacityRow, raw, alphaValues);
                }
                else if (foldMask)
                {
                    ImageRows.Stencil(image.Mask!.GetRow(y), width, image.Mask.IsInverted, opacityRow);
                }
                else if (colorKey)
                {
                    ImageRows.ColorKey(rawRow, components, image.ColorKey, opacityRow);
                }

                switch (kind)
                {
                    case RowKind.Indices:
                        map.MapToIndices(rawRow, byteRow, highValue);
                        converter.Convert(byteRow, rgbRow, width);
                        break;
                    case RowKind.Bytes:
                        ImageRows.UnpackScaled(row, storageBits, samples.BitsPerComponent, values, byteRow);
                        converter.Convert(byteRow, rgbRow, width);
                        break;
                    case RowKind.TableValues:
                        map.MapToIndices(rawRow, byteRow, highValue);
                        TableValues(byteRow, table, baseRanges, floats.AsSpan(0, colorValues));
                        Unmatte(image, opacityRow, baseRanges, floats.AsSpan(0, colorValues), alphaValues.AsSpan(0, width));
                        ToBytes(baseConverter!, floats.AsSpan(0, colorValues), rgbRow, rgbValues.AsSpan(0, width * 3), width);
                        break;
                    default:
                        map.Map(rawRow, floats.AsSpan(0, values), ranges);
                        if (matte)
                        {
                            Unmatte(image, opacityRow, ranges, floats.AsSpan(0, values), alphaValues.AsSpan(0, width));
                        }

                        ToBytes(converter, floats.AsSpan(0, values), rgbRow, rgbValues.AsSpan(0, width * 3), width);
                        break;
                }

                Span<byte> target = pixels.AsSpan(y * width * 4, width * 4);
                for (int x = 0; x < width; x++)
                {
                    int r = rgbRow[3 * x];
                    int g = rgbRow[(3 * x) + 1];
                    int b = rgbRow[(3 * x) + 2];
                    int a = alpha ? opacityRow[x] : 255;
                    if (a != 255)
                    {
                        r = Divide255(r * a);
                        g = Divide255(g * a);
                        b = Divide255(b * a);
                    }

                    target[4 * x] = (byte)b;
                    target[(4 * x) + 1] = (byte)g;
                    target[(4 * x) + 2] = (byte)r;
                    target[(4 * x) + 3] = (byte)a;
                }
            }

            return PixelImage.Copy(pixels.AsSpan(0, width * height * 4), width, height, 4);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
            ArrayPool<ushort>.Shared.Return(raw);
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<byte>.Shared.Return(rgb);
            ArrayPool<float>.Shared.Return(floats);
            ArrayPool<float>.Shared.Return(rgbValues);
            ArrayPool<byte>.Shared.Return(opacity);
            ArrayPool<float>.Shared.Return(alphaValues);
        }
    }

    /// <summary>Converts a soft mask (§11.6.5.2) to an alpha-only image; null when too large.</summary>
    public static PixelImage? ConvertSoftMask(DisplayImage mask)
    {
        int width = mask.Width;
        int height = mask.Height;
        if ((long)width * height > Array.MaxLength)
        {
            return null;
        }

        byte[] pixels = ArrayPool<byte>.Shared.Rent(width * height);
        ushort[] raw = ArrayPool<ushort>.Shared.Rent(width + 1);
        float[] values = ArrayPool<float>.Shared.Rent(width + 1);
        try
        {
            for (int y = 0; y < height; y++)
            {
                SoftRow(mask, y, pixels.AsSpan(y * width, width), raw, values);
            }

            return PixelImage.Copy(pixels.AsSpan(0, width * height), width, height, 1);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
            ArrayPool<ushort>.Shared.Return(raw);
            ArrayPool<float>.Shared.Return(values);
        }
    }

    /// <summary>Converts a soft mask's samples at a reduction (§11.6.5.2, issue #362) to an alpha-only image; null when too large.</summary>
    public static PixelImage? ConvertSoftMask(DisplayImage mask, DisplayImageSamples samples)
    {
        int width = samples.Width;
        int height = samples.Height;
        if ((long)width * height > Array.MaxLength)
        {
            return null;
        }

        byte[] pixels = ArrayPool<byte>.Shared.Rent(width * height);
        ushort[] raw = ArrayPool<ushort>.Shared.Rent(width + 1);
        float[] values = ArrayPool<float>.Shared.Rent(width + 1);
        try
        {
            for (int y = 0; y < height; y++)
            {
                SoftRow(mask, samples, y, pixels.AsSpan(y * width, width), raw, values);
            }

            return PixelImage.Copy(pixels.AsSpan(0, width * height), width, height, 1);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
            ArrayPool<ushort>.Shared.Return(raw);
            ArrayPool<float>.Shared.Return(values);
        }
    }

    /// <summary>Converts a stencil or explicit mask (§8.9.6.2, §8.9.6.3) to an alpha-only image, opaque where it paints; null when too large.</summary>
    public static PixelImage? ConvertStencil(DisplayImageMask mask)
    {
        int width = mask.Width;
        int height = mask.Height;
        if ((long)width * height > Array.MaxLength)
        {
            return null;
        }

        byte[] pixels = ArrayPool<byte>.Shared.Rent(width * height);
        try
        {
            for (int y = 0; y < height; y++)
            {
                ImageRows.Stencil(mask.GetRow(y), width, mask.IsInverted, pixels.AsSpan(y * width, width));
            }

            return PixelImage.Copy(pixels.AsSpan(0, width * height), width, height, 1);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    /// <summary>
    /// Converts a stencil or explicit mask (§8.9.6.2, §8.9.6.3) reduced by 2^<paramref name="reduction"/> (issue #362) to an alpha-only
    /// image: each of the ⌈W / s⌉ × ⌈H / s⌉ values is the share of the s × s samples it covers that paint (the last ones cover fewer),
    /// so a 1-bit mask drawn small becomes 8-bit coverage; null when too large.
    /// </summary>
    public static PixelImage? ConvertStencil(DisplayImageMask mask, int reduction)
    {
        if (reduction == 0)
        {
            return ConvertStencil(mask);
        }

        int width = mask.Width;
        int height = mask.Height;
        int scale = 1 << reduction;
        int reducedWidth = (int)(((long)width + scale - 1) >> reduction);
        int reducedHeight = (int)(((long)height + scale - 1) >> reduction);
        byte[] pixels = ArrayPool<byte>.Shared.Rent(reducedWidth * reducedHeight);
        long[] counts = ArrayPool<long>.Shared.Rent(reducedWidth);
        byte[] line = ArrayPool<byte>.Shared.Rent(width);
        try
        {
            for (int y = 0; y < reducedHeight; y++)
            {
                Span<long> sums = counts.AsSpan(0, reducedWidth);
                sums.Clear();
                int first = y * scale;
                int rows = Math.Min(height, first + scale) - first;
                for (int sy = first; sy < first + rows; sy++)
                {
                    ImageRows.Stencil(mask.GetRow(sy), width, mask.IsInverted, line.AsSpan(0, width));
                    for (int x = 0; x < width; x++)
                    {
                        sums[x >> reduction] += line[x];
                    }
                }

                Span<byte> target = pixels.AsSpan(y * reducedWidth, reducedWidth);
                for (int x = 0; x < reducedWidth; x++)
                {
                    long area = (long)(Math.Min(width, (x + 1) * scale) - (x * scale)) * rows;
                    target[x] = (byte)((sums[x] + (area / 2)) / area);
                }
            }

            return PixelImage.Copy(pixels.AsSpan(0, reducedWidth * reducedHeight), reducedWidth, reducedHeight, 1);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
            ArrayPool<long>.Shared.Return(counts);
            ArrayPool<byte>.Shared.Return(line);
        }
    }

    /// <summary>Writes the opacity of each value of a row of a soft mask's samples at a reduction, 0 to 255, through their Decode map (Table 143).</summary>
    private static void SoftRow(DisplayImage mask, DisplayImageSamples samples, int y, Span<byte> alpha, ushort[] raw, float[] values)
    {
        int width = samples.Width;
        ImageDecodeMap map = mask.DecodeMapFor(samples);
        int top = (1 << map.BitsPerComponent) - 1;
        if (samples.StorageBits <= 8 && map.Map(0, 0) == 0f && map.Map(0, top) == 1f)
        {
            ImageRows.UnpackScaled(samples.GetRow(y), samples.StorageBits, samples.BitsPerComponent, width, alpha);
            return;
        }

        ImageRows.Unpack(samples.GetRow(y), samples.StorageBits, width, raw.AsSpan(0, width));
        map.Map(raw.AsSpan(0, width), values.AsSpan(0, width));
        for (int x = 0; x < width; x++)
        {
            alpha[x] = (byte)((Math.Clamp(values[x], 0f, 1f) * 255f) + 0.5f);
        }
    }

    /// <summary>Writes the opacity of each sample of a soft mask row, 0 to 255: the samples through its Decode array, clamped to 0 to 1 (Table 143).</summary>
    private static void SoftRow(DisplayImage mask, int y, Span<byte> alpha, ushort[] raw, float[] values)
    {
        int width = mask.Width;
        ImageDecodeMap map = mask.DecodeMap;
        int top = (1 << map.BitsPerComponent) - 1;
        if (mask.StorageBits <= 8 && map.Map(0, 0) == 0f && map.Map(0, top) == 1f)
        {
            ImageRows.UnpackScaled(mask.GetRow(y), mask.StorageBits, mask.BitsPerComponent, width, alpha);
            return;
        }

        ImageRows.Unpack(mask.GetRow(y), mask.StorageBits, width, raw.AsSpan(0, width));
        map.Map(raw.AsSpan(0, width), values.AsSpan(0, width));
        for (int x = 0; x < width; x++)
        {
            alpha[x] = (byte)((Math.Clamp(values[x], 0f, 1f) * 255f) + 0.5f);
        }
    }

    private static int Divide255(int value)
    {
        value += 128;
        return (value + (value >> 8)) >> 8;
    }

    private static void ToBytes(PdfColorConverter converter, ReadOnlySpan<float> values, Span<byte> rgb, Span<float> rgbValues, int width)
    {
        converter.Convert(values, rgbValues, width);
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)((Math.Clamp(rgbValues[i], 0f, 1f) * 255f) + 0.5f);
        }
    }

    /// <summary>Removes the matte with the folded soft mask's opacities (§11.6.5.2: c = m + (c′ − m) / α).</summary>
    private static void Unmatte(DisplayImage image, ReadOnlySpan<byte> opacity, ReadOnlySpan<ComponentRange> ranges, Span<float> values, Span<float> alpha)
    {
        for (int x = 0; x < alpha.Length; x++)
        {
            alpha[x] = opacity[x] / 255f;
        }

        ImageRows.Unpremultiply(values, ranges.Length, alpha, image.Matte, ranges);
    }

    /// <summary>Looks up the base-space component values of table indices (§8.6.6.3).</summary>
    private static void TableValues(ReadOnlySpan<byte> indices, ReadOnlySpan<byte> table, ReadOnlySpan<ComponentRange> ranges, Span<float> values)
    {
        int n = ranges.Length;
        for (int i = 0, v = 0; i < indices.Length; i++)
        {
            int entry = indices[i] * n;
            for (int c = 0; c < n; c++, v++)
            {
                int value = entry + c < table.Length ? table[entry + c] : 0;
                values[v] = (float)(ranges[c].Minimum + (value / 255.0 * (ranges[c].Maximum - ranges[c].Minimum)));
            }
        }
    }

    /// <summary>Whether the Decode map is the default of Table 88.</summary>
    private static bool IsDefault(ImageDecodeMap map, ReadOnlySpan<ComponentRange> ranges)
    {
        int top = (1 << map.BitsPerComponent) - 1;
        for (int c = 0; c < ranges.Length; c++)
        {
            if (Math.Abs(map.Map(c, 0) - ranges[c].Minimum) > 1e-6 || Math.Abs(map.Map(c, top) - ranges[c].Maximum) > 1e-6)
            {
                return false;
            }
        }

        return true;
    }

    private enum RowKind
    {
        Indices,
        Bytes,
        Values,
        TableValues,
    }
}
