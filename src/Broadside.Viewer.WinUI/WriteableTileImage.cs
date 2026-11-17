using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Broadside.Viewer.WinUI;

/// <summary>A tile shown as a <see cref="WriteableBitmap"/> (premultiplied BGRA, the rasterizer's own layout: no conversion).</summary>
internal sealed class WriteableTileImage(WriteableBitmap bitmap) : ITileImage
{
    public WriteableBitmap Bitmap { get; } = bitmap;

    public int Width => Bitmap.PixelWidth;

    public int Height => Bitmap.PixelHeight;
}

/// <summary>Makes <see cref="WriteableTileImage"/>s on the UI thread (a <see cref="WriteableBitmap"/> belongs to the thread that made it).</summary>
internal sealed class WriteableTileImageFactory : ITileImageFactory
{
    public ITileImage Create(int width, int height, ReadOnlySpan<byte> bgra, int stride)
    {
        var bitmap = new WriteableBitmap(width, height);
        using (Stream pixels = bitmap.PixelBuffer.AsStream())
        {
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
            {
                pixels.Write(bgra.Slice(y * stride, rowBytes));
            }
        }

        bitmap.Invalidate();
        return new WriteableTileImage(bitmap);
    }
}
