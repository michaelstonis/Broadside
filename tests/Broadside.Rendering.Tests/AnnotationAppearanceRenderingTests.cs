using System.Diagnostics;
using Broadside.Annotations;
using Broadside.GoldenImages;
using Broadside.Graphics;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The appearance streams generated for created annotations (issue #165) render the same in the software rasterizer as in
/// poppler (<c>pdftoppm -r 72</c>), which draws the streams as written. ISO 32000-2 §12.5.5.
/// </summary>
public sealed class AnnotationAppearanceRenderingTests
{
    private static readonly PdfQuadrilateral Words = new(new PathPoint(100, 520), new PathPoint(260, 520), new PathPoint(100, 500), new PathPoint(260, 500));

    private static readonly PdfEngine Engine = new(new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));

    public static TheoryData<string> Kinds => [.. Creations.Keys];

    private static Dictionary<string, Action<PdfPage>> Creations { get; } = new()
    {
        ["Text"] = page => page.AddTextAnnotation(new PdfRectangle(50, 700, 70, 720), "A note"),
        // A link is borderless; poppler draws a link's border itself, centred on the rectangle's edge, besides its appearance.
        ["Link"] = page => page.AddLinkAnnotation(new PdfRectangle(50, 650, 200, 670), new Uri("https://example.com/")),
        ["Line"] = page => page.AddLineAnnotation(new PathPoint(50, 600), new PathPoint(250, 560), new PdfAnnotationOptions().WithBorderWidth(3)),
        ["Square"] = page => page.AddSquareAnnotation(new PdfRectangle(300, 540, 400, 620), new PdfAnnotationOptions().WithBorderWidth(4).WithInteriorColor(new PdfDeviceColor(0, 0.5, 1))),
        ["Circle"] = page => page.AddCircleAnnotation(new PdfRectangle(420, 540, 540, 620), new PdfAnnotationOptions().WithColor(new PdfDeviceColor(0, 0.5, 0)).WithBorderWidth(3)),
        ["Polygon"] = page => page.AddPolygonAnnotation([new PathPoint(50, 400), new PathPoint(150, 400), new PathPoint(100, 480)], new PdfAnnotationOptions().WithInteriorColor(new PdfDeviceColor(0.8))),
        ["PolyLine"] = page => page.AddPolyLineAnnotation([new PathPoint(200, 400), new PathPoint(250, 480), new PathPoint(300, 400)], new PdfAnnotationOptions().WithBorderWidth(2)),
        ["Ink"] = page => page.AddInkAnnotation([[new PathPoint(350, 400), new PathPoint(400, 450), new PathPoint(450, 400)], [new PathPoint(350, 470), new PathPoint(450, 470)]], new PdfAnnotationOptions().WithBorderWidth(4)),
        ["Highlight"] = page => page.AddHighlightAnnotation([Words]),
        ["Underline"] = page => page.AddUnderlineAnnotation([Words]),
        ["Squiggly"] = page => page.AddSquigglyAnnotation([Words]),
        ["StrikeOut"] = page => page.AddStrikeOutAnnotation([Words]),
        ["FreeText"] = page => page.AddFreeTextAnnotation(new PdfRectangle(300, 650, 500, 720), "Free text that wraps over more than one line", new PdfAnnotationOptions().WithColor(new PdfDeviceColor(0, 0, 1))),
        ["Stamp"] = page => page.AddStampAnnotation(new PdfRectangle(300, 250, 500, 300), "NotApproved"),
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_created_annotation_renders_as_poppler_renders_it(string kind)
    {
        byte[] file;
        using (PdfDocument document = PdfDocument.Create())
        {
            PdfPage page = document.Pages[0];
            using (PdfCanvas canvas = page.CreateCanvas())
            {
                // Something under the markups, so Multiply shows.
                canvas.SetFillColor(PdfColor.FromGray(0.5f));
                canvas.Rectangle(120, 505, 60, 10);
                canvas.Fill();
            }

            Creations[kind](page);
            using var output = new MemoryStream();
            document.Save(output);
            file = output.ToArray();
        }

        GoldenImage poppler = Poppler(file);
        using PdfDocument reopened = Engine.Open(file);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(reopened.Pages[0], GoldenCorpus.CreateRenderOptions());
        GoldenImage ours = GoldenImage.FromBitmap(bitmap);

        // Coordinates and widths are whole units so axis-aligned edges fall on pixel edges at 72 dpi: poppler's stroke
        // adjustment snaps such edges where the rasterizer's exact-area coverage would draw them half covered.
        // Text is drawn in a Helvetica the two engines take from different font programs (the bundled Standard 14 here,
        // the system's substitute in poppler), so glyph edges differ; geometry matches at the default threshold.
        GoldenThreshold threshold = kind is "FreeText" or "Stamp" ? new GoldenThreshold(0.8, 0.05, 600) : GoldenThreshold.Default;
        GoldenComparison comparison = GoldenComparison.Compare(poppler, ours, threshold);
        Assert.True(comparison.Matches, comparison.Describe());
        Assert.True(kind == "Link" ? Ink(ours) == 0 : Ink(ours) > 0, "The annotation drew nothing, or a link drew a border.");
    }

    /// <summary>Counts the pixels that are not white outside the grey block the page itself draws.</summary>
    private static int Ink(GoldenImage image)
    {
        int count = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                (byte r, byte g, byte b) = image[x, y];
                bool block = x >= 120 && x < 180 && y >= 792 - 515 && y < 792 - 505;
                if (!block && (r < 250 || g < 250 || b < 250))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static GoldenImage Poppler(byte[] file)
    {
        string? executable = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "pdftoppm"))
            .FirstOrDefault(File.Exists);
        if (executable is null)
        {
            Assert.Skip("pdftoppm is not on the PATH.");
        }

        string path = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, file);
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in (string[])["-r", "72", "-png", "-singlefile", path])
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start)!;
            using var png = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(png);
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            png.Position = 0;
            return GoldenImage.FromPng(png);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
