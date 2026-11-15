using Broadside;
using Broadside.Rendering;

// Open a file and render its first page with the software rasterizer.
using PdfDocument document = PdfDocument.Open(args[0]);
using var rasterizer = new SoftwareRasterizer();
using Bitmap bitmap = rasterizer.Render(document.Pages[0]);
Console.WriteLine($"bitmap: {bitmap.Width} x {bitmap.Height}");
return bitmap.Width > 0 && bitmap.Pixels.IndexOfAnyExcept((byte)0xFF) >= 0 ? 0 : 1;
