namespace Broadside.GoldenImages;

/// <summary>The memory layout of the 8-bit, four-channel pixels a backend hands to <see cref="GoldenImage.FromPixels"/>.</summary>
public enum GoldenPixelFormat
{
    /// <summary>Blue, green, red, alpha; colour premultiplied by alpha (Broadside's <c>Bitmap</c>, Skia's N32 on most platforms, Direct2D).</summary>
    Bgra8Premultiplied,

    /// <summary>Red, green, blue, alpha; colour premultiplied by alpha (Skia on some platforms, CoreGraphics, Android's ARGB_8888).</summary>
    Rgba8Premultiplied,

    /// <summary>Blue, green, red, alpha; straight (not premultiplied) colour.</summary>
    Bgra8,

    /// <summary>Red, green, blue, alpha; straight (not premultiplied) colour, the layout of a decoded PNG.</summary>
    Rgba8,
}
