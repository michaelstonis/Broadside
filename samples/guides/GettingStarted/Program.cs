using Broadside;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Samples;

Sample.Start("Getting started");

#region open
using PdfDocument document = PdfDocument.Open("text-standard14.pdf");
#endregion

#region inspect
Console.WriteLine($"PDF {document.Version}, {document.Pages.Count} page(s)");
Console.WriteLine($"Title: {document.Properties.Title ?? "(none)"}");

PdfPage first = document.Pages[0];
Console.WriteLine($"Page 1 is {first.MediaBox.Width} x {first.MediaBox.Height} points, rotated {first.Rotation} degrees");
Console.WriteLine($"Page 1 says: {first.ExtractText().Text}");

foreach (Diagnostic diagnostic in document.Diagnostics)
{
    Console.WriteLine($"Repaired: {diagnostic}");
}
#endregion

Sample.Expect(document.Pages.Count == 1, "one page");
Sample.Expect(first.ExtractText().Text.Contains("Hello, Broadside", StringComparison.Ordinal), "the page's text");
Sample.Expect(document.Diagnostics.Count == 0, "no repairs");

#region save
document.Properties.Title = "Hello from Broadside";
document.Save("text-standard14-titled.pdf");
#endregion

using (PdfDocument saved = PdfDocument.Open("text-standard14-titled.pdf"))
{
    Sample.Expect(saved.Properties.Title == "Hello from Broadside", "the saved title");
}

#region create
using (PdfDocument created = PdfDocument.Create())
{
    PdfPage page = created.Pages[0]; // Create() starts with one empty US Letter page
    PdfCanvasFont helvetica = created.GetStandard14Font(Standard14Font.Helvetica);
    using (PdfCanvas canvas = page.CreateCanvas())
    {
        canvas.BeginText();
        canvas.SetFont(helvetica, 24);
        canvas.MoveText(72, 700);
        canvas.ShowText("Hello from Broadside");
        canvas.EndText();
    }

    created.Save("hello.pdf");
}
#endregion

using (PdfDocument hello = PdfDocument.Open("hello.pdf"))
{
    Sample.Expect(hello.Pages[0].ExtractText().Text.Contains("Hello from Broadside", StringComparison.Ordinal), "the created page's text");
}

#region render
PdfOptions options = new PdfOptions().UseStandard14Fonts(); // glyphs for fonts a file uses without embedding them
using (PdfDocument hello = PdfDocument.Open("hello.pdf", options))
using (var rasterizer = new SoftwareRasterizer())
using (Bitmap bitmap = rasterizer.Render(hello.Pages[0], new RenderOptions().WithDpi(150)))
{
    bitmap.SavePng("hello.png");
}
#endregion

Sample.Expect(new FileInfo("hello.png").Length > 0, "the rendered page");

Sample.Finish();
