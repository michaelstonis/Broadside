using Android.Runtime;
using AndroidBitmap = Android.Graphics.Bitmap;
using ByteBuffer = Java.Nio.ByteBuffer;

namespace Broadside.Rendering.Android;

/// <summary>
/// Converted pixels and the Android bitmap made from them: premultiplied BGRA (sRGB, stored by the bitmap as ARGB_8888) or one 8-bit
/// channel (an alpha plane, an ALPHA_8 bitmap the canvas paints in the paint's colour, ISO 32000-2 §8.9.6, §11.6.5.2). The managed
/// pixels stay readable, so that an image can be resampled to the device (<see cref="ImageReducer"/>) without reading back from the
/// bitmap.
/// </summary>
internal sealed class PixelImage : IDisposable
{
    private static readonly AndroidBitmap.Config Argb = AndroidBitmap.Config.Argb8888!;
    private static readonly AndroidBitmap.Config Alpha = AndroidBitmap.Config.Alpha8!;

    private PixelImage(byte[] data, int width, int height, int channels)
    {
        Data = data;
        Width = width;
        Height = height;
        Channels = channels;
        Bitmap = AndroidBitmap.CreateBitmap(width, height, channels == 1 ? Alpha : Argb);
        try
        {
            Upload(Bitmap, data, width, height, channels);
        }
        catch
        {
            Bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Gets the pixels, row by row without padding.</summary>
    public byte[] Data { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the bytes per pixel: 4 (premultiplied BGRA) or 1 (alpha).</summary>
    public int Channels { get; }

    /// <summary>Gets the Android bitmap holding a copy of <see cref="Data"/>.</summary>
    public AndroidBitmap Bitmap { get; }

    /// <summary>Copies <paramref name="pixels"/> into a new image.</summary>
    public static PixelImage Copy(ReadOnlySpan<byte> pixels, int width, int height, int channels) => new(pixels.ToArray(), width, height, channels);

    /// <summary>Wraps <paramref name="pixels"/>, which the image owns from now on.</summary>
    public static PixelImage Own(byte[] pixels, int width, int height, int channels) => new(pixels, width, height, channels);

    public void Dispose()
    {
        Bitmap.Recycle();
        Bitmap.Dispose();
    }

    /// <summary>
    /// Copies the pixels into the bitmap through a direct buffer over pinned memory (no Java heap copy): rows padded to the bitmap's
    /// row bytes, BGRA reordered to the RGBA bytes an ARGB_8888 bitmap stores.
    /// </summary>
    private static unsafe void Upload(AndroidBitmap bitmap, byte[] data, int width, int height, int channels)
    {
        int rowBytes = bitmap.RowBytes;
        int row = width * channels;
        byte[] staged = new byte[(long)rowBytes * height <= int.MaxValue ? rowBytes * height : throw new ArgumentOutOfRangeException(nameof(height))];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> source = data.AsSpan(y * row, row);
            Span<byte> target = staged.AsSpan(y * rowBytes, row);
            if (channels == 1)
            {
                source.CopyTo(target);
                continue;
            }

            for (int x = 0; x < row; x += 4)
            {
                target[x] = source[x + 2];
                target[x + 1] = source[x + 1];
                target[x + 2] = source[x];
                target[x + 3] = source[x + 3];
            }
        }

        fixed (byte* pointer = staged)
        {
            IntPtr handle = JNIEnv.NewDirectByteBuffer((IntPtr)pointer, staged.Length);
            using ByteBuffer buffer = Java.Lang.Object.GetObject<ByteBuffer>(handle, JniHandleOwnership.TransferLocalRef)!;
            bitmap.CopyPixelsFromBuffer(buffer);
        }
    }
}

/// <summary>A block of device pixels: columns <see cref="Left"/> to <see cref="Right"/>, rows <see cref="Top"/> to <see cref="Bottom"/> (exclusive ends).</summary>
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}
