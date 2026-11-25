using Broadside.Rendering;
using Broadside.Rendering.Direct2D;

namespace Broadside.Samples;

/// <summary>
/// The rendering guide's Direct2D sample (site/articles/rendering.md). It needs Windows, so it is not a console app under
/// samples/guides: tests/Broadside.Rendering.Direct2D.Tests compiles this file and runs it on CI's Windows leg, comparing its pixels
/// with the page's golden image.
/// </summary>
public static class Direct2DSample
{
    /// <summary>Renders the first page of a file at 72 DPI with the given Direct3D device.</summary>
    /// <param name="path">The PDF file.</param>
    /// <param name="driver">The device: the graphics hardware (falling back to WARP when there is none), or WARP itself.</param>
    /// <returns>The render; the caller disposes it.</returns>
    public static Bitmap Render(string path, Direct2DDriver driver)
    {
        #region direct2d
        using PdfDocument document = PdfDocument.Open(path);
        using var renderer = new Direct2DRenderer(driver); // Direct2DDriver.Hardware, or Warp for the same pixels on every machine
        Bitmap bitmap = renderer.Render(document.Pages[0], new RenderOptions()); // premultiplied BGRA, on white
        #endregion

        return bitmap;
    }
}
