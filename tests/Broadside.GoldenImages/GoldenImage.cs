using Broadside.Rendering;
using StbImageSharp;

namespace Broadside.GoldenImages;

/// <summary>
/// A rendered page as the golden-image harness compares it: opaque red, green and blue, each pixel as it appears on a white page.
/// </summary>
/// <remarks>
/// Every backend's output is reduced to this one form, whatever its pixel layout and whether it painted the white page background or
/// left the page transparent: each pixel is composited over white (source-over, ISO 32000-2 §11.3.3 with an opaque white backdrop).
/// Immutable; safe to share between threads.
/// </remarks>
public sealed class GoldenImage
{
    private readonly byte[] _rgb;

    private GoldenImage(int width, int height, byte[] rgb)
    {
        Width = width;
        Height = height;
        _rgb = rgb;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the colour of the pixel at (<paramref name="x"/>, <paramref name="y"/>), 0 at the top left.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    public (byte R, byte G, byte B) this[int x, int y]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(x);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
            ArgumentOutOfRangeException.ThrowIfNegative(y);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
            int offset = ((y * Width) + x) * 3;
            return (_rgb[offset], _rgb[offset + 1], _rgb[offset + 2]);
        }
    }

    /// <summary>The pixels as red, green, blue triplets, rows top to bottom with no padding.</summary>
    internal ReadOnlySpan<byte> Rgb => _rgb;

    /// <summary>Reads a backend's pixels, composited over white.</summary>
    /// <param name="pixels">At least <c>(height − 1) × stride + width × 4</c> bytes.</param>
    /// <param name="width">The width in pixels, at least 1.</param>
    /// <param name="height">The height in pixels, at least 1.</param>
    /// <param name="stride">The bytes from the start of one row to the start of the next, at least <c>width × 4</c>.</param>
    /// <param name="format">The layout of each pixel.</param>
    /// <returns>The image.</returns>
    public static GoldenImage FromPixels(ReadOnlySpan<byte> pixels, int width, int height, int stride, GoldenPixelFormat format)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan((long)stride, (long)width * 4, nameof(stride));
        ArgumentOutOfRangeException.ThrowIfLessThan((long)pixels.Length, ((long)(height - 1) * stride) + ((long)width * 4), nameof(pixels));
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format.");
        }

        bool bgr = format is GoldenPixelFormat.Bgra8Premultiplied or GoldenPixelFormat.Bgra8;
        bool premultiplied = format is GoldenPixelFormat.Bgra8Premultiplied or GoldenPixelFormat.Rgba8Premultiplied;
        byte[] rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = pixels.Slice(y * stride, width * 4);
            Span<byte> target = rgb.AsSpan(y * width * 3, width * 3);
            for (int x = 0; x < width; x++)
            {
                ReadOnlySpan<byte> pixel = row.Slice(x * 4, 4);
                byte a = pixel[3];
                byte r = bgr ? pixel[2] : pixel[0];
                byte g = pixel[1];
                byte b = bgr ? pixel[0] : pixel[2];
                target[x * 3] = OverWhite(r, a, premultiplied);
                target[(x * 3) + 1] = OverWhite(g, a, premultiplied);
                target[(x * 3) + 2] = OverWhite(b, a, premultiplied);
            }
        }

        return new GoldenImage(width, height, rgb);
    }

    /// <summary>Reads a bitmap the software rasterizer (or any caller) drew, composited over white.</summary>
    /// <param name="bitmap">The bitmap.</param>
    /// <returns>The image.</returns>
    public static GoldenImage FromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return FromPixels(bitmap.Pixels, bitmap.Width, bitmap.Height, bitmap.Stride, GoldenPixelFormat.Bgra8Premultiplied);
    }

    /// <summary>Decodes a PNG (any colour type and bit depth StbImageSharp reads), composited over white.</summary>
    /// <param name="png">The PNG stream, read from its current position.</param>
    /// <returns>The image.</returns>
    public static GoldenImage FromPng(Stream png)
    {
        ArgumentNullException.ThrowIfNull(png);
        ImageResult decoded = ImageResult.FromStream(png, ColorComponents.RedGreenBlueAlpha);
        return FromPixels(decoded.Data, decoded.Width, decoded.Height, decoded.Width * 4, GoldenPixelFormat.Rgba8);
    }

    /// <summary>Decodes a PNG file, composited over white.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The image.</returns>
    public static GoldenImage LoadPng(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return FromPng(stream);
    }

    /// <summary>Writes the image as an opaque RGB PNG.</summary>
    /// <param name="stream">The stream to write to.</param>
    public void SavePng(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var bitmap = new Bitmap(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            ReadOnlySpan<byte> source = _rgb.AsSpan(y * Width * 3, Width * 3);
            for (int x = 0; x < Width; x++)
            {
                row[x * 4] = source[(x * 3) + 2];
                row[(x * 4) + 1] = source[(x * 3) + 1];
                row[(x * 4) + 2] = source[x * 3];
                row[(x * 4) + 3] = 255;
            }
        }

        bitmap.SavePng(stream);
    }

    /// <summary>Writes the image as an opaque RGB PNG file, creating its directory if needed.</summary>
    /// <param name="path">The file.</param>
    public void SavePng(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Create(path);
        SavePng(stream);
    }

    /// <summary>Creates an image from red, green, blue triplets the harness built.</summary>
    internal static GoldenImage FromRgb(int width, int height, byte[] rgb) => new(width, height, rgb);

    private static byte OverWhite(byte channel, byte alpha, bool premultiplied)
    {
        // Premultiplied: c + (1 − a) × white. Straight: c × a + (1 − a) × white. Both rounded to the nearest level.
        int value = premultiplied ? channel + (255 - alpha) : (((channel * alpha) + (255 * (255 - alpha)) + 127) / 255);
        return (byte)Math.Min(value, 255);
    }
}
