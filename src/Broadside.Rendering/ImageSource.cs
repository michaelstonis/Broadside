using System.Buffers;
using System.Runtime.InteropServices;
using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Rendering;

/// <summary>
/// A display-list image converted to the rasterizer's pixels: every sample mapped through the Decode array, converted to RGB
/// through colour management and stored as opaque premultiplied BGRA, at the image's own resolution, ready to be resampled.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5.2 (Decode, Table 88), §8.6.6.3 (Indexed: samples are indices, rounded and clipped to the table) and §8.9.3
/// (the sample layout). Colours are converted before resampling, so an Indexed image is never interpolated between indices.
/// </para>
/// <para>
/// One per rasterizer, reused for every image: its buffers are pooled and grow to the largest image seen, so converting allocates
/// nothing per row or per image once warm. The last image converted is kept until the next one or the end of the render, so an
/// image painted several times in a row is converted once.
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
    private DisplayImage? _image;
    private PdfColorConverter? _converter;

    /// <summary>Gets the converted pixels, <see cref="Width"/> × <see cref="Height"/>, rows top to bottom.</summary>
    public ReadOnlySpan<uint> Pixels => _pixels.AsSpan(0, Width * Height);

    /// <summary>Gets the pooled array behind <see cref="Pixels"/>, possibly longer.</summary>
    public uint[] Buffer => _pixels;

    /// <summary>Gets the width of the image converted last.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the height of the image converted last.</summary>
    public int Height { get; private set; }

    /// <summary>Converts <paramref name="image"/> with <paramref name="converter"/>, unless it is the image and converter of the last call.</summary>
    /// <param name="image">The image; at most <see cref="Array.MaxLength"/> pixels.</param>
    /// <param name="converter">Its converter to RGB.</param>
    public void Prepare(DisplayImage image, PdfColorConverter converter)
    {
        if (ReferenceEquals(image, _image) && ReferenceEquals(converter, _converter))
        {
            return;
        }

        int width = image.Width;
        int height = image.Height;
        int components = image.Components;
        int values = width * components;
        Ensure(ref _pixels, width * height);
        Ensure(ref _rgb, width * 3);

        PdfColorSpace space = image.ColorSpace;
        ImageDecodeMap map = image.DecodeMap;
        Span<ComponentRange> ranges = stackalloc ComponentRange[components];
        for (int c = 0; c < components; c++)
        {
            ranges[c] = space.GetComponentRange(c);
        }

        RowKind kind = space is PdfIndexedColorSpace ? RowKind.Indices
            : image.StorageBits <= 8 && IsDefault(map, ranges) ? RowKind.Bytes
            : RowKind.Values;
        if (kind != RowKind.Bytes)
        {
            Ensure(ref _raw, values);
        }

        if (kind != RowKind.Values)
        {
            Ensure(ref _bytes, values);
        }
        else
        {
            Ensure(ref _values, values);
            Ensure(ref _rgbValues, width * 3);
        }

        int highValue = space is PdfIndexedColorSpace indexed ? indexed.HighValue : 0;
        Span<byte> rgb = _rgb.AsSpan(0, width * 3);
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = image.GetRow(y);
            switch (kind)
            {
                case RowKind.Indices:
                    ImageRows.Unpack(row, image.StorageBits, values, _raw.AsSpan(0, values));
                    map.MapToIndices(_raw.AsSpan(0, values), _bytes.AsSpan(0, values), highValue);
                    converter.Convert(_bytes.AsSpan(0, values), rgb, width);
                    break;
                case RowKind.Bytes:
                    ImageRows.UnpackScaled(row, image.StorageBits, image.BitsPerComponent, values, _bytes.AsSpan(0, values));
                    converter.Convert(_bytes.AsSpan(0, values), rgb, width);
                    break;
                default:
                    ImageRows.Unpack(row, image.StorageBits, values, _raw.AsSpan(0, values));
                    map.Map(_raw.AsSpan(0, values), _values.AsSpan(0, values), ranges);
                    converter.Convert(_values.AsSpan(0, values), _rgbValues.AsSpan(0, width * 3), width);
                    for (int i = 0; i < rgb.Length; i++)
                    {
                        rgb[i] = (byte)((Math.Clamp(_rgbValues[i], 0f, 1f) * 255f) + 0.5f);
                    }

                    break;
            }

            Span<uint> pixels = _pixels.AsSpan(y * width, width);
            for (int x = 0; x < width; x++)
            {
                // BGRA in memory order, little-endian uint: B | G << 8 | R << 16 | A << 24.
                pixels[x] = rgb[(3 * x) + 2] | ((uint)rgb[(3 * x) + 1] << 8) | ((uint)rgb[3 * x] << 16) | 0xFF000000u;
            }
        }

        if (!BitConverter.IsLittleEndian)
        {
            Span<byte> bytes = MemoryMarshal.AsBytes(_pixels.AsSpan(0, width * height));
            for (int i = 0; i < bytes.Length; i += 4)
            {
                bytes.Slice(i, 4).Reverse();
            }
        }

        Width = width;
        Height = height;
        _image = image;
        _converter = converter;
    }

    /// <summary>Forgets the image converted last, so that nothing it belongs to is kept alive; the buffers stay.</summary>
    public void Forget()
    {
        _image = null;
        _converter = null;
    }

    /// <summary>Returns the pooled buffers.</summary>
    public void Dispose()
    {
        Forget();
        Return(ref _pixels);
        Return(ref _raw);
        Return(ref _bytes);
        Return(ref _values);
        Return(ref _rgbValues);
        Return(ref _rgb);
        Width = 0;
        Height = 0;
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

    private static void Ensure<T>(ref T[] buffer, int length)
    {
        if (buffer.Length < length)
        {
            Return(ref buffer);
            buffer = ArrayPool<T>.Shared.Rent(length);
        }
    }

    private static void Return<T>(ref T[] buffer)
    {
        if (buffer.Length > 0)
        {
            ArrayPool<T>.Shared.Return(buffer);
        }

        buffer = [];
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
    }
}
