using Broadside.GoldenImages;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>Renders a page through <see cref="CoreGraphicsRenderer"/> into a CoreGraphics bitmap context and hands its pixels over.</summary>
internal static class BitmapRendering
{
    /// <summary>Premultiplied BGRA in memory order: 32-bit little-endian words with alpha first, as Apple's own bitmaps.</summary>
    public const CGBitmapFlags PremultipliedBgra = CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little;

    /// <summary>Renders <paramref name="page"/> at <paramref name="options"/> into an sRGB bitmap context of the page's pixel size.</summary>
    public static GoldenImage Render(PdfPage page, RenderOptions options)
    {
        PixelSize size = options.GetPixelSize(page);
        using var context = CreateContext(size.Width, size.Height, out byte[] pixels, out int stride);
        using var renderer = new CoreGraphicsRenderer();
        renderer.Render(page, context, options);
        context.Flush();
        return GoldenImage.FromPixels(pixels, size.Width, size.Height, stride, GoldenPixelFormat.Bgra8Premultiplied);
    }

    /// <summary>Creates a transparent sRGB bitmap context of premultiplied BGRA pixels over a managed buffer.</summary>
    public static CGBitmapContext CreateContext(int width, int height, out byte[] pixels, out int stride)
    {
        stride = ((width * 4) + 63) / 64 * 64;
        pixels = new byte[stride * height];
        using CGColorSpace space = CGColorSpace.CreateSrgb() ?? throw new InvalidOperationException("No sRGB colour space.");
        return new CGBitmapContext(pixels, width, height, 8, stride, space, PremultipliedBgra);
    }
}
