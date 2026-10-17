using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>
/// Converted pixels and the CoreGraphics image over them: premultiplied BGRA (sRGB) or one 8-bit channel (an alpha plane, a DeviceGray
/// image that clips as a mask, ISO 32000-2 §8.9.6, §11.6.5.2). The managed pixels stay readable, so that an image can be resampled to
/// the device (<see cref="ImageReducer"/>) without reading back from CoreGraphics.
/// </summary>
internal sealed class PixelImage : IDisposable
{
    private static readonly CGColorSpace Srgb = CGColorSpace.CreateSrgb() ?? throw new PlatformNotSupportedException("CoreGraphics has no sRGB colour space.");
    private static readonly CGColorSpace Gray = CGColorSpace.CreateDeviceGray();

    private PixelImage(byte[] data, int width, int height, int channels)
    {
        Data = data;
        Width = width;
        Height = height;
        Channels = channels;
        using var provider = new CGDataProvider(data);
        Image = channels == 1
            ? new CGImage(width, height, 8, 8, width, Gray, CGBitmapFlags.None, provider, null, false, CGColorRenderingIntent.Default)
            : new CGImage(width, height, 8, 32, width * 4, Srgb, CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little, provider, null, false, CGColorRenderingIntent.Default);
    }

    /// <summary>Gets the pixels, row by row without padding.</summary>
    public byte[] Data { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the bytes per pixel: 4 (premultiplied BGRA) or 1 (alpha).</summary>
    public int Channels { get; }

    /// <summary>Gets the CoreGraphics image over <see cref="Data"/>.</summary>
    public CGImage Image { get; }

    /// <summary>Copies <paramref name="pixels"/> into a new image.</summary>
    public static PixelImage Copy(ReadOnlySpan<byte> pixels, int width, int height, int channels) => new(pixels.ToArray(), width, height, channels);

    /// <summary>Wraps <paramref name="pixels"/>, which the image owns from now on.</summary>
    public static PixelImage Own(byte[] pixels, int width, int height, int channels) => new(pixels, width, height, channels);

    public void Dispose() => Image.Dispose();
}

/// <summary>A block of device pixels: columns <see cref="Left"/> to <see cref="Right"/>, rows <see cref="Top"/> to <see cref="Bottom"/> (exclusive ends).</summary>
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}
