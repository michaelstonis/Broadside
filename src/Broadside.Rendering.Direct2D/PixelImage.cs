using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// Converted pixels and the Direct2D bitmap over them: premultiplied BGRA (sRGB) or one 8-bit channel (an alpha plane: a mask that
/// clips or fills, ISO 32000-2 §8.9.6, §11.6.5.2). The managed pixels stay readable, so that an image can be resampled to the device
/// (<see cref="ImageReducer"/>) without reading back from the GPU; the bitmap is made on first use on the device context and lives as
/// long as the image (the caches holding images are emptied when the device is lost or the renderer disposed).
/// </summary>
internal sealed unsafe class PixelImage : IDisposable
{
    private ID2D1Bitmap1* _bitmap;

    private PixelImage(byte[] data, int width, int height, int channels)
    {
        Data = data;
        Width = width;
        Height = height;
        Channels = channels;
    }

    /// <summary>Gets the pixels, row by row without padding.</summary>
    public byte[] Data { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the bytes per pixel: 4 (premultiplied BGRA) or 1 (alpha).</summary>
    public int Channels { get; }

    /// <summary>Copies <paramref name="pixels"/> into a new image.</summary>
    public static PixelImage Copy(ReadOnlySpan<byte> pixels, int width, int height, int channels) => new(pixels.ToArray(), width, height, channels);

    /// <summary>Wraps <paramref name="pixels"/>, which the image owns from now on.</summary>
    public static PixelImage Own(byte[] pixels, int width, int height, int channels) => new(pixels, width, height, channels);

    /// <summary>Returns the Direct2D bitmap of the pixels on <paramref name="context"/>'s device, made on the first call.</summary>
    public ID2D1Bitmap* Bitmap(ID2D1DeviceContext* context)
    {
        if (_bitmap is null)
        {
            var size = new D2D_SIZE_U { width = (uint)Width, height = (uint)Height };
            var properties = new D2D1_BITMAP_PROPERTIES1
            {
                pixelFormat = new D2D1_PIXEL_FORMAT
                {
                    format = Channels == 1 ? DXGI_FORMAT.DXGI_FORMAT_A8_UNORM : DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED,
                },
                dpiX = 96,
                dpiY = 96,
                bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE,
            };
            ID2D1Bitmap1* bitmap;
            fixed (byte* pixels = Data)
            {
                context->CreateBitmap(size, pixels, (uint)(Width * Channels), &properties, &bitmap);
            }

            _bitmap = bitmap;
        }

        return (ID2D1Bitmap*)_bitmap;
    }

    public void Dispose()
    {
        if (_bitmap is not null)
        {
            _bitmap->Release();
            _bitmap = null;
        }
    }
}

/// <summary>A block of device pixels: columns <see cref="Left"/> to <see cref="Right"/>, rows <see cref="Top"/> to <see cref="Bottom"/> (exclusive ends).</summary>
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}
