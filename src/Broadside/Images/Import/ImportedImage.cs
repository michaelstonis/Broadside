using System.Buffers;

namespace Broadside.Images.Import;

/// <summary>The color model of an imported image's color plane.</summary>
internal enum ImportedColor
{
    /// <summary>One component, 0 black.</summary>
    Gray,

    /// <summary>Three components: red, green, blue.</summary>
    Rgb,

    /// <summary>Four components: cyan, magenta, yellow, black.</summary>
    Cmyk,

    /// <summary>One palette index per pixel into <see cref="ImportedImage.Palette"/>.</summary>
    Palette,
}

/// <summary>How an imported image keeps its file's compressed data.</summary>
internal enum ImportedPassThrough
{
    /// <summary>Decoded: the color plane holds the samples.</summary>
    None,

    /// <summary><c>CCITTFaxDecode</c> data with <see cref="ImportedImage.Ccitt"/>.</summary>
    Ccitt,

    /// <summary><c>DCTDecode</c> data, a complete JPEG stream.</summary>
    Dct,
}

/// <summary>The <c>CCITTFaxDecode</c> parameters of passed-through fax data (ISO 32000-2 §7.4.6, Table 11).</summary>
/// <param name="K">Negative: Group 4; 0: Group 3 one-dimensional; positive: Group 3 two-dimensional.</param>
/// <param name="EndOfLine">Whether every line starts with an EOL.</param>
/// <param name="EncodedByteAlign">Whether every line (or the EOL before it) ends on a byte boundary.</param>
/// <param name="BlackIs1">Whether black runs decode to 1 bits.</param>
internal readonly record struct ImportedCcitt(int K, bool EndOfLine, bool EncodedByteAlign, bool BlackIs1);

/// <summary>
/// An image read from a TIFF, GIF or BMP file in the form a PDF image XObject takes: either the file's own compressed data
/// (<see cref="Encoded"/>) or decoded samples (<see cref="Color"/>), an alpha plane separated from the color, and what the file says
/// about its resolution and orientation.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9.3 and §8.9.5: rows top to bottom, byte-aligned, samples MSB first, 16-bit samples high byte first. The planes are
/// pooled; dispose the image to return them.
/// </remarks>
internal sealed class ImportedImage : IDisposable
{
    private byte[]? _color;
    private byte[]? _alpha;

    internal ImportedImage(int width, int height, int bitsPerComponent, ImportedColor color)
    {
        Width = width;
        Height = height;
        BitsPerComponent = bitsPerComponent;
        ColorModel = color;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the bits per color component (or palette index), and of the alpha samples.</summary>
    public int BitsPerComponent { get; }

    /// <summary>Gets the color model.</summary>
    public ImportedColor ColorModel { get; }

    /// <summary>Gets the number of color components per pixel in the color plane.</summary>
    public int Components => ColorModel switch
    {
        ImportedColor.Rgb => 3,
        ImportedColor.Cmyk => 4,
        _ => 1,
    };

    /// <summary>Gets the bytes of one row of the color plane.</summary>
    public int ColorStride => (int)((((long)Width * Components * BitsPerComponent) + 7) / 8);

    /// <summary>Gets the bytes of one row of the alpha plane.</summary>
    public int AlphaStride => (int)((((long)Width * BitsPerComponent) + 7) / 8);

    /// <summary>Gets the palette, three bytes (R, G, B) per entry, for <see cref="ImportedColor.Palette"/>; empty otherwise.</summary>
    public byte[] Palette { get; set; } = [];

    /// <summary>Gets the palette index that is transparent (a color key mask), or -1.</summary>
    public int TransparentIndex { get; set; } = -1;

    /// <summary>Gets the embedded ICC profile, or <see langword="null"/>.</summary>
    public byte[]? IccProfile { get; set; }

    /// <summary>Gets how the compressed data is kept.</summary>
    public ImportedPassThrough PassThrough { get; private set; }

    /// <summary>Gets the passed-through compressed data, or <see langword="null"/>.</summary>
    public byte[]? Encoded { get; private set; }

    /// <summary>Gets the CCITT parameters of passed-through fax data.</summary>
    public ImportedCcitt Ccitt { get; private set; }

    /// <summary>Gets the <c>ColorTransform</c> to write for passed-through JPEG data, or -1 to write none.</summary>
    public int ColorTransform { get; private set; } = -1;

    /// <summary>Gets a value indicating whether the color samples are premultiplied by the alpha (TIFF associated alpha).</summary>
    public bool Premultiplied { get; set; }

    /// <summary>Gets the horizontal resolution in pixels per inch, or 0 when the file does not say.</summary>
    public double DpiX { get; set; }

    /// <summary>Gets the vertical resolution in pixels per inch, or 0 when the file does not say.</summary>
    public double DpiY { get; set; }

    /// <summary>Gets the TIFF/Exif orientation, 1 to 8 (1: rows top to bottom, columns left to right).</summary>
    public int Orientation { get; set; } = 1;

    /// <summary>Gets the repairs made while reading, each a sentence for an <c>ImageImportRepaired</c> diagnostic.</summary>
    public List<string> Repairs { get; } = [];

    /// <summary>Gets the decoded color samples, <see cref="ColorStride"/> bytes per row; empty when the data passes through.</summary>
    public ReadOnlySpan<byte> Color => _color is null ? [] : _color.AsSpan(0, ColorStride * Height);

    /// <summary>Gets the decoded color samples for writing; empty when the data passes through.</summary>
    internal Span<byte> ColorPlane => _color is null ? [] : _color.AsSpan(0, ColorStride * Height);

    /// <summary>Gets a value indicating whether the image has an alpha plane.</summary>
    public bool HasAlpha => _alpha is not null;

    /// <summary>Gets the alpha samples, <see cref="AlphaStride"/> bytes per row; empty when there is none.</summary>
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

    /// <summary>Rents and zeroes the color plane.</summary>
    internal Span<byte> RentColor()
    {
        int length = ColorStride * Height;
        _color ??= ArrayPool<byte>.Shared.Rent(length);
        Span<byte> plane = _color.AsSpan(0, length);
        plane.Clear();
        return plane;
    }

    /// <summary>Takes a pooled buffer that already holds the color plane.</summary>
    internal void AdoptColor(byte[] buffer)
    {
        if (_color is not null)
        {
            ArrayPool<byte>.Shared.Return(_color);
        }

        _color = buffer;
    }

    /// <summary>Rents and zeroes the alpha plane.</summary>
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

    /// <summary>Drops the alpha plane when every sample in it is the maximum (opaque).</summary>
    internal void DropOpaqueAlpha()
    {
        if (_alpha is null)
        {
            return;
        }

        ReadOnlySpan<byte> alpha = Alpha;
        int full = (int)((long)BitsPerComponent * Width / 8);
        int stride = AlphaStride;
        int bits = (int)((long)Width * BitsPerComponent % 8);
        byte last = (byte)(0xFF << (8 - bits));
        for (int y = 0; y < Height; y++)
        {
            ReadOnlySpan<byte> row = alpha.Slice(y * stride, stride);
            if (row[..full].ContainsAnyExcept((byte)0xFF) || (bits != 0 && (row[full] & last) != last))
            {
                return;
            }
        }

        DropAlpha();
    }

    /// <summary>Keeps <paramref name="encoded"/> as fax data.</summary>
    internal void PassCcitt(byte[] encoded, ImportedCcitt parameters)
    {
        PassThrough = ImportedPassThrough.Ccitt;
        Encoded = encoded;
        Ccitt = parameters;
    }

    /// <summary>Keeps <paramref name="encoded"/> as a JPEG stream.</summary>
    internal void PassDct(byte[] encoded, int colorTransform)
    {
        PassThrough = ImportedPassThrough.Dct;
        Encoded = encoded;
        ColorTransform = colorTransform;
    }
}
