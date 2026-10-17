namespace Broadside.Rendering.Tests;

/// <summary>A pixel as premultiplied red, green, blue and alpha.</summary>
internal readonly record struct Rgba(byte R, byte G, byte B, byte A);

/// <summary>Reads single pixels of a bitmap.</summary>
internal static class BitmapProbe
{
    /// <summary>Returns the pixel at (<paramref name="x"/>, <paramref name="y"/>), 0 at the top left.</summary>
    public static Rgba At(this Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<byte> pixel = bitmap.GetRow(y).Slice(x * 4, 4);
        return new Rgba(pixel[2], pixel[1], pixel[0], pixel[3]);
    }
}
