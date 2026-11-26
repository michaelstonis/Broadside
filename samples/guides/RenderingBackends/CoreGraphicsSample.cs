using Broadside.Rendering;
using Broadside.Rendering.CoreGraphics;
using CoreGraphics;

namespace Broadside.Samples;

/// <summary>
/// The rendering guide's CoreGraphics sample (site/articles/rendering.md). It needs an Apple platform, so it is not a console app under
/// samples/guides: tests/Broadside.Rendering.CoreGraphics.Tests compiles this file and runs it on CI's macOS leg, comparing its pixels
/// with the page's golden image.
/// </summary>
public static class CoreGraphicsSample
{
    /// <summary>Renders the first page of a file at 72 DPI and returns its premultiplied BGRA pixels.</summary>
    /// <param name="path">The PDF file.</param>
    /// <returns>The pixels, with the bitmap's width, height and bytes per row.</returns>
    public static (byte[] Pixels, int Width, int Height, int Stride) Render(string path)
    {
        #region coregraphics
        using PdfDocument document = PdfDocument.Open(path);
        PdfPage page = document.Pages[0];
        var options = new RenderOptions(); // 72 DPI

        // A bitmap context over your own memory: premultiplied BGRA (Apple's native layout) in sRGB, the page's pixel size.
        PixelSize size = options.GetPixelSize(page);
        int stride = size.Width * 4;
        byte[] pixels = new byte[stride * size.Height];
        using CGColorSpace srgb = CGColorSpace.CreateSrgb()!;
        using var context = new CGBitmapContext(
            pixels, size.Width, size.Height, 8, stride, srgb, CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little);

        using var renderer = new CoreGraphicsBackend();
        renderer.Render(page, context, options); // on white, like every page render
        context.Flush();
        #endregion

        return (pixels, size.Width, size.Height, stride);
    }
}
