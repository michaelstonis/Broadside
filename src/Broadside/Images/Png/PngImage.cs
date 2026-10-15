using System.Buffers;

namespace Broadside.Images.Png;

/// <summary>The PNG colour types (ISO/IEC 15948 §6.1, Table 11.1).</summary>
internal enum PngColorType : byte
{
    /// <summary>Greyscale, 1 to 16 bits.</summary>
    Gray = 0,

    /// <summary>Truecolour, 8 or 16 bits.</summary>
    Rgb = 2,

    /// <summary>Indexed-colour, 1 to 8 bits.</summary>
    Palette = 3,

    /// <summary>Greyscale with alpha, 8 or 16 bits.</summary>
    GrayAlpha = 4,

    /// <summary>Truecolour with alpha, 8 or 16 bits.</summary>
    Rgba = 6,
}

/// <summary>
/// A PNG file read by <see cref="PngReader"/>, in the form a PDF image takes: the colour samples, either as the file's own zlib data
/// (<see cref="PassThrough"/>) or decoded (<see cref="Color"/>), and the alpha samples separated into a plane of their own.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9.5 and §11.6.5.2. The planes are pooled; dispose the image to return them. Rows are byte-aligned and samples
/// MSB first, 16-bit samples big-endian: the layout of PNG and of PDF image data alike (§8.9.3; ISO/IEC 15948 §7.2).
/// </remarks>
internal sealed class PngImage : IDisposable
{
    private byte[]? _color;
    private byte[]? _alpha;

    internal PngImage(int width, int height, int bitDepth, PngColorType colorType, bool interlaced)
    {
        Width = width;
        Height = height;
        BitDepth = bitDepth;
        ColorType = colorType;
        Interlaced = interlaced;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the bits per sample (of a colour sample, an alpha sample, or a palette index).</summary>
    public int BitDepth { get; }

    /// <summary>Gets the colour type.</summary>
    public PngColorType ColorType { get; }

    /// <summary>Gets a value indicating whether the file is Adam7 interlaced.</summary>
    public bool Interlaced { get; }

    /// <summary>Gets the number of colour samples per pixel in the colour plane: 1 (gray, palette index) or 3.</summary>
    public int ColorComponents => ColorType is PngColorType.Rgb or PngColorType.Rgba ? 3 : 1;

    /// <summary>Gets the bytes of a row of the colour plane.</summary>
    public int ColorStride => (int)((((long)Width * ColorComponents * BitDepth) + 7) / 8);

    /// <summary>Gets the palette, three bytes (R, G, B) per entry, at least as many entries as the largest index used; empty for other colour types.</summary>
    public byte[] Palette { get; internal set; } = [];

    /// <summary>Gets the tRNS colour of a gray or truecolour image, one raw sample value per colour component; <see langword="null"/> when there is none.</summary>
    public ushort[]? ColorKey { get; internal set; }

    /// <summary>Gets the decompressed iCCP profile, or <see langword="null"/>.</summary>
    public byte[]? IccProfile { get; internal set; }

    /// <summary>
    /// Gets the concatenated IDAT data when it is, as it stands, the <c>FlateDecode</c> data of the colour samples with the PNG
    /// predictors (§7.4.4.4): not interlaced, no alpha channel, nothing after the image data; otherwise <see langword="null"/>.
    /// </summary>
    public byte[]? PassThrough { get; internal set; }

    /// <summary>Gets the decoded colour samples, <see cref="ColorStride"/> bytes per row; empty when only <see cref="PassThrough"/> is kept.</summary>
    public ReadOnlySpan<byte> Color => _color is null ? [] : _color.AsSpan(0, ColorStride * Height);

    /// <summary>Gets the bits per alpha sample: the bit depth for an alpha channel, 8 for a palette's tRNS.</summary>
    public int AlphaBits => ColorType == PngColorType.Palette ? 8 : BitDepth;

    /// <summary>Gets the bytes of a row of the alpha plane.</summary>
    public int AlphaStride => (int)((((long)Width * AlphaBits) + 7) / 8);

    /// <summary>Gets a value indicating whether the image has an alpha plane, which it has only when some pixel is not opaque.</summary>
    public bool HasAlpha => _alpha is not null;

    /// <summary>Gets the alpha samples, <see cref="AlphaStride"/> bytes per row; empty when every pixel is opaque.</summary>
    public ReadOnlySpan<byte> Alpha => _alpha is null ? [] : _alpha.AsSpan(0, AlphaStride * Height);

    /// <summary>Returns the pooled planes.</summary>
    public void Dispose()
    {
        if (_color is not null)
        {
            ArrayPool<byte>.Shared.Return(_color);
            _color = null;
        }

        DropAlpha();
    }

    /// <summary>Rents and zeroes the colour plane.</summary>
    internal Span<byte> RentColor()
    {
        int length = ColorStride * Height;
        _color ??= ArrayPool<byte>.Shared.Rent(length);
        Span<byte> plane = _color.AsSpan(0, length);
        plane.Clear();
        return plane;
    }

    /// <summary>Rents the alpha plane.</summary>
    internal Span<byte> RentAlpha()
    {
        int length = AlphaStride * Height;
        _alpha ??= ArrayPool<byte>.Shared.Rent(length);
        Span<byte> plane = _alpha.AsSpan(0, length);
        plane.Clear();
        return plane;
    }

    /// <summary>Returns the alpha plane: every pixel turned out opaque.</summary>
    internal void DropAlpha()
    {
        if (_alpha is not null)
        {
            ArrayPool<byte>.Shared.Return(_alpha);
            _alpha = null;
        }
    }
}
