using Broadside;
using Broadside.GoldenImages;
using Broadside.Rendering;
using Broadside.Rendering.Skia;
using Broadside.Samples;
using SkiaSharp;

Sample.Start("Rendering with SkiaSharp");

#region skia
using PdfDocument document = PdfDocument.Open("paths-fill-rules.pdf");
PdfPage page = document.Pages[0];
using var backend = new SkiaBackend();

// A new SKBitmap of the page's pixel size, on white.
using SKBitmap bitmap = backend.Render(page, new RenderOptions());

// Or onto a canvas you already have, such as a view's surface: the page fills the canvas's top-left GetPixelSize pixels.
RenderOptions options = new RenderOptions().WithDpi(144);
PixelSize size = options.GetPixelSize(page);
using var surface = new SKBitmap(size.Width, size.Height);
using (var canvas = new SKCanvas(surface))
{
    backend.Render(page, canvas, options);
}

foreach (Broadside.Diagnostics.Diagnostic diagnostic in backend.Diagnostics)
{
    Console.WriteLine($"Drawn another way: {diagnostic}"); // what Skia could not draw itself, drawn by the software rasterizer
}
#endregion

Sample.Expect(backend.Diagnostics.Count == 0, "Skia drew the page itself");
GoldenCheck.Expect(
    "skia",
    "paths-fill-rules.pdf",
    0,
    GoldenImage.FromPixels(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes, GoldenPixelFormat.Bgra8Premultiplied));

Console.WriteLine("OK");
