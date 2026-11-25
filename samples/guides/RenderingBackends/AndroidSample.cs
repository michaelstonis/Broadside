using Broadside.Rendering;
using Broadside.Rendering.Android;

namespace Broadside.Samples;

/// <summary>
/// The rendering guide's Android sample (site/articles/rendering.md). It needs an Android device, so it is not a console app under
/// samples/guides: the device app tests/Broadside.Rendering.Android.Device compiles this file and runs it on the emulator of the Android
/// workflow, and tests/Broadside.Rendering.Android.Tests compares its pixels with the page's golden image.
/// </summary>
public static class AndroidSample
{
    /// <summary>Renders the first page of a file into a new bitmap at 72 DPI, and onto a canvas at 144 DPI.</summary>
    /// <param name="pdf">The PDF file's bytes, such as an asset of the app.</param>
    /// <returns>The bitmap; the caller disposes it.</returns>
    public static Android.Graphics.Bitmap Render(byte[] pdf)
    {
        #region android
        using PdfDocument document = PdfDocument.Open(pdf);
        PdfPage page = document.Pages[0];
        using var backend = new AndroidCanvasBackend();

        // A new ARGB_8888 bitmap of the page's pixel size, on white.
        Android.Graphics.Bitmap bitmap = backend.Render(page, new RenderOptions());

        // Or onto a canvas you already have, such as a view's in OnDraw: the page fills its top-left GetPixelSize pixels.
        RenderOptions options = new RenderOptions().WithDpi(144);
        PixelSize size = options.GetPixelSize(page);
        using var surface = Android.Graphics.Bitmap.CreateBitmap(size.Width, size.Height, Android.Graphics.Bitmap.Config.Argb8888!);
        using (var canvas = new Android.Graphics.Canvas(surface))
        {
            backend.Render(page, canvas, options);
        }
        #endregion

        return bitmap;
    }
}
