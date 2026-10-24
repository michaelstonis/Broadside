using System.Runtime.InteropServices;
using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Rendering;

/// <summary>
/// A display-list image converted to the rasterizer's pixels: every sample mapped through the Decode array, converted to RGB
/// through colour management and stored as premultiplied BGRA, at the image's own resolution, ready to be resampled. A mask of the
/// image's own size (a colour key, an explicit or soft mask, JPEG 2000 opacity) is folded into the pixels' alpha; a mask of another
/// size is left to <see cref="MaskSource"/> and sampled at its own resolution.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5.2 (Decode, Table 88), §8.6.6.3 (Indexed: samples are indices, rounded and clipped to the table) and §8.9.3
/// (the sample layout). Colours are converted before resampling, so an Indexed image is never interpolated between indices.
/// Masks: §8.9.6.3 (explicit), §8.9.6.4 (colour key, on the raw samples), §11.6.5.2 (soft mask; Matte, removed from the component
/// values with the mask's opacity before conversion, Table 144), §7.4.9 and §8.9.5.1 Table 87 (opacity in JPEG 2000 data, SMaskInData).
/// </para>
/// <para>
/// One per rasterizer, reused for every image: its buffers are pooled and grow to the largest image seen, so converting allocates
/// nothing per row or per image once warm. The last image converted is kept until the next one, across renders, so an image
/// painted several times in a row, or in every tile of a display list, is converted once (the converter is the document's shared
/// one, so a later render of the same list finds the same pair).
/// </para>
/// </remarks>
internal sealed class ImageSource : IDisposable
{
    private uint[] _pixels = [];
    private ushort[] _raw = [];
    private byte[] _bytes = [];
    private float[] _values = [];
    private float[] _rgbValues = [];
    private byte[] _rgb = [];
    private byte[] _alpha = [];
    private float[] _alphaValues = [];
    private DisplayImageSamples _samples;
    private PdfColorConverter? _converter;
    private PixelModel _model;

    // Whether the image's soft mask was folded into the samples converted last (at their reduction, so given by them and the image).
    private bool _folded;

    /// <summary>Gets the converted pixels, <see cref="Width"/> × <see cref="Height"/>, rows top to bottom.</summary>
    public ReadOnlySpan<uint> Pixels => _pixels.AsSpan(0, Width * Height);

    /// <summary>Gets the pooled array behind <see cref="Pixels"/>, possibly longer.</summary>
    public uint[] Buffer => _pixels;

    /// <summary>Gets the width of the image converted last.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the height of the image converted last.</summary>
    public int Height { get; private set; }

    /// <summary>
    /// Whether the image's soft mask has the size of the samples painted, at the image's own resolution, so that its opacity is
    /// folded into the converted pixels; a mask of a reduced image is resampled on its own grid (#362).
    /// </summary>
    public static bool FoldsSoftMask(DisplayImage image, DisplayImageSamples samples, DisplayImageSamples? soft) =>
        soft is { } folded && image.SoftMask is { } mask && mask.Width == image.Width && mask.Height == image.Height && folded.Reduction == samples.Reduction;

    /// <summary>Whether the image's explicit mask has the size of the samples painted, at the image's own resolution, so that it is folded into the converted pixels.</summary>
    public static bool FoldsMask(DisplayImage image, DisplayImageSamples samples) =>
        samples.Reduction == 0 && image.Mask is { } mask && mask.Width == samples.Width && mask.Height == samples.Height;

    /// <summary>Converts <paramref name="samples"/> of <paramref name="image"/> with <paramref name="converter"/>, unless they are the samples and converter of the last call.</summary>
    /// <param name="image">The image: its colour space, Decode mapping and masks.</param>
    /// <param name="samples">The samples to convert, at the image's resolution or a reduction of it; at most <see cref="Array.MaxLength"/> pixels.</param>
    /// <param name="converter">Its converter to RGB.</param>
    /// <param name="baseConverter">
    /// For an Indexed image with a <see cref="DisplayImage.Matte"/>, the converter of its base space (the matte is removed from the
    /// table's colours); otherwise unused.
    /// </param>
    /// <param name="model">What the pixels hold: RGB, or a transparency group's gray or CMYK plane; the converters convert to its target.</param>
    /// <param name="soft">The soft mask's samples when they are folded in: the soft mask has the image's size and these are at the samples' reduction (<see cref="FoldsSoftMask"/>); otherwise <see langword="null"/>.</param>
    public void Prepare(DisplayImage image, DisplayImageSamples samples, DisplayImageSamples? soft, PdfColorConverter converter, PdfColorConverter? baseConverter = null, PixelModel model = default)
    {
        if (samples == _samples && (soft is not null) == _folded && ReferenceEquals(converter, _converter) && model == _model)
        {
            return;
        }

        int outputs = model.Components;

        int width = samples.Width;
        int height = samples.Height;
        int components = samples.Components;
        int values = width * components;
        Pooled.Ensure(ref _pixels, width * height);
        Pooled.Ensure(ref _rgb, width * outputs);

        PdfColorSpace space = image.ColorSpace;
        var indexed = space as PdfIndexedColorSpace;
        ImageDecodeMap map = image.DecodeMapFor(samples);
        Span<ComponentRange> ranges = stackalloc ComponentRange[components];
        for (int c = 0; c < components; c++)
        {
            ranges[c] = space.GetComponentRange(c);
        }

        bool foldSoft = FoldsSoftMask(image, samples, soft);
        bool foldMask = FoldsMask(image, samples);
        bool colorKey = !image.ColorKey.IsEmpty;
        bool matte = foldSoft && !image.Matte.IsEmpty
            && (indexed is null ? image.Matte.Length == components : baseConverter is not null && image.Matte.Length == indexed.Base.ComponentCount);
        bool alpha = foldSoft || foldMask || colorKey;
        ImageDecodeMap? softMap = foldSoft ? image.SoftMask!.DecodeMapFor(soft!.Value) : null;

        RowKind kind = matte ? (indexed is null ? RowKind.Values : RowKind.TableValues)
            : indexed is not null ? RowKind.Indices
            : samples.StorageBits <= 8 && IsDefault(map, ranges) ? RowKind.Bytes
            : RowKind.Values;
        int colorComponents = kind == RowKind.TableValues ? indexed!.Base.ComponentCount : components;
        int colorValues = width * colorComponents;
        Pooled.Ensure(ref _raw, values + width);
        Pooled.Ensure(ref _bytes, values);
        if (kind is RowKind.Values or RowKind.TableValues)
        {
            Pooled.Ensure(ref _values, colorValues);
            Pooled.Ensure(ref _rgbValues, width * outputs);
        }

        if (alpha)
        {
            Pooled.Ensure(ref _alpha, width);
            Pooled.Ensure(ref _alphaValues, width);
        }

        Span<ComponentRange> baseRanges = stackalloc ComponentRange[kind == RowKind.TableValues ? colorComponents : 0];
        for (int c = 0; c < baseRanges.Length; c++)
        {
            baseRanges[c] = indexed!.Base.GetComponentRange(c);
        }

        ReadOnlySpan<byte> table = kind == RowKind.TableValues ? image.ColorTable : default;
        int highValue = indexed?.HighValue ?? 0;
        Span<byte> rgb = _rgb.AsSpan(0, width * outputs);
        Span<ushort> raw = _raw.AsSpan(0, values);
        Span<byte> bytes = _bytes.AsSpan(0, values);
        Span<byte> opacity = alpha ? _alpha.AsSpan(0, width) : default;
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = samples.GetRow(y);
            if (kind != RowKind.Bytes || colorKey)
            {
                ImageRows.Unpack(row, samples.StorageBits, values, raw);
            }

            if (foldSoft)
            {
                MaskSource.SoftRow(soft!.Value, softMap!, y, opacity, _raw.AsSpan(values, width), _alphaValues.AsSpan(0, width));
            }
            else if (foldMask)
            {
                MaskSource.StencilRow(image.Mask!, y, opacity);
            }
            else if (colorKey)
            {
                ImageRows.ColorKey(raw, components, image.ColorKey, opacity);
            }

            switch (kind)
            {
                case RowKind.Indices:
                    map.MapToIndices(raw, bytes, highValue);
                    converter.Convert(bytes, rgb, width);
                    break;
                case RowKind.Bytes:
                    ImageRows.UnpackScaled(row, samples.StorageBits, samples.BitsPerComponent, values, bytes);
                    converter.Convert(bytes, rgb, width);
                    break;
                case RowKind.TableValues:
                    map.MapToIndices(raw, bytes, highValue);
                    TableValues(bytes, table, baseRanges, _values.AsSpan(0, colorValues));
                    Unmatte(image, opacity, baseRanges, _values.AsSpan(0, colorValues));
                    ToBytes(baseConverter!, _values.AsSpan(0, colorValues), rgb, width);
                    break;
                default:
                    map.Map(raw, _values.AsSpan(0, values), ranges);
                    if (matte)
                    {
                        Unmatte(image, opacity, ranges, _values.AsSpan(0, values));
                    }

                    ToBytes(converter, _values.AsSpan(0, values), rgb, width);
                    break;
            }

            Span<uint> pixels = _pixels.AsSpan(y * width, width);
            for (int x = 0; x < width; x++)
            {
                // BGRA in memory order, little-endian uint: B | G << 8 | R << 16 | A << 24, premultiplied by the folded opacity.
                uint packed = model == default ? 0 : model.Pack(rgb.Slice(outputs * x, outputs));
                uint r = model == default ? rgb[3 * x] : (packed >> 16) & 0xFF;
                uint g = model == default ? rgb[(3 * x) + 1] : (packed >> 8) & 0xFF;
                uint b = model == default ? rgb[(3 * x) + 2] : packed & 0xFF;
                uint a = alpha ? opacity[x] : 255u;
                if (a != 255)
                {
                    r = Divide255(r * a);
                    g = Divide255(g * a);
                    b = Divide255(b * a);
                }

                pixels[x] = b | (g << 8) | (r << 16) | (a << 24);
            }
        }

        if (!BitConverter.IsLittleEndian)
        {
            Span<byte> pixelBytes = MemoryMarshal.AsBytes(_pixels.AsSpan(0, width * height));
            for (int i = 0; i < pixelBytes.Length; i += 4)
            {
                pixelBytes.Slice(i, 4).Reverse();
            }
        }

        Width = width;
        Height = height;
        _samples = samples;
        _folded = soft is not null;
        _converter = converter;
        _model = model;
    }

    /// <summary>Forgets the image converted last, so that nothing it belongs to is kept alive; the buffers stay.</summary>
    public void Forget()
    {
        _samples = default;
        _folded = false;
        _converter = null;
    }

    /// <summary>Returns the pooled buffers.</summary>
    public void Dispose()
    {
        Forget();
        Pooled.Return(ref _pixels);
        Pooled.Return(ref _raw);
        Pooled.Return(ref _bytes);
        Pooled.Return(ref _values);
        Pooled.Return(ref _rgbValues);
        Pooled.Return(ref _rgb);
        Pooled.Return(ref _alpha);
        Pooled.Return(ref _alphaValues);
        Width = 0;
        Height = 0;
    }

    /// <summary>Rounds <paramref name="value"/> / 255 for 0 ≤ value ≤ 255 × 255.</summary>
    private static uint Divide255(uint value)
    {
        value += 128;
        return (value + (value >> 8)) >> 8;
    }

    /// <summary>Converts component values to RGB bytes.</summary>
    private void ToBytes(PdfColorConverter converter, ReadOnlySpan<float> values, Span<byte> rgb, int width)
    {
        Span<float> rgbValues = _rgbValues.AsSpan(0, rgb.Length);
        converter.Convert(values, rgbValues, width);
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)((Math.Clamp(rgbValues[i], 0f, 1f) * 255f) + 0.5f);
        }
    }

    /// <summary>Removes the matte from component values with the folded soft mask's opacities (§11.6.5.2: c = m + (c′ − m) / α).</summary>
    private void Unmatte(DisplayImage image, ReadOnlySpan<byte> opacity, ReadOnlySpan<ComponentRange> ranges, Span<float> values)
    {
        Span<float> alpha = _alphaValues.AsSpan(0, opacity.Length);
        for (int x = 0; x < alpha.Length; x++)
        {
            alpha[x] = opacity[x] / 255f;
        }

        ImageRows.Unpremultiply(values, ranges.Length, alpha, image.Matte, ranges);
    }

    /// <summary>Looks up the base-space component values of table indices (§8.6.6.3: each byte maps onto its component's range).</summary>
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

    /// <summary>Whether the Decode map is the default of Table 88: 0 and 2^n − 1 map onto each component's range.</summary>
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

    /// <summary>How a row of samples becomes colours.</summary>
    private enum RowKind
    {
        /// <summary>Indexed: raw values to table indices, then the table's colours (§8.6.6.3).</summary>
        Indices,

        /// <summary>Default Decode at up to 8 bits: samples scaled to bytes, converted by the byte path (Table 88).</summary>
        Bytes,

        /// <summary>Anything else: raw values through the Decode map to component values, clipped to their ranges (§8.9.5.2).</summary>
        Values,

        /// <summary>Indexed with a Matte: the table's colours as base-space component values, so the matte can be removed (§11.6.5.2).</summary>
        TableValues,
    }
}
