using System.Runtime.CompilerServices;
using Broadside;
using Broadside.Layout.Flow;
using Broadside.Rendering;
using Broadside.TestSupport;
using StbImageSharp;

// The AOT smoke host (issue #95): renders the rendering corpus files with the software rasterizer, compares every pixel with the
// reviewed snapshots of Broadside.Rendering.Tests, round-trips the PNG output, and exits 1 on any difference. Pass --require-aot to
// fail unless it runs natively compiled (no dynamic code), which is how CI runs the published executable.
bool requireAot = args.Contains("--require-aot");
var failures = new List<string>();
string snapshots = Path.Combine(CorpusLocator.RepositoryRoot, "tests", "Broadside.Rendering.Tests", "Snapshots");

Console.WriteLine($"dynamic code supported: {RuntimeFeature.IsDynamicCodeSupported}");
if (requireAot && RuntimeFeature.IsDynamicCodeSupported)
{
    failures.Add("--require-aot: the host is not running natively compiled.");
}

using var rasterizer = new SoftwareRasterizer();

foreach ((double dpi, int width, int height) in new[] { (72.0, 612, 792), (150.0, 1275, 1650) })
{
    using PdfDocument document = PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, "empty-page.pdf"));
    using Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(dpi));
    bool white = bitmap.Width == width && bitmap.Height == height && bitmap.Pixels.IndexOfAnyExcept((byte)0xFF) < 0;
    Report($"empty-page.pdf at {dpi} DPI: {bitmap.Width} x {bitmap.Height}, white", white);
}

Compare("paths-fill-rules.pdf", 0, "RenderingSnapshotTests.Paths_fill_rules_pdf_matches_its_reviewed_snapshot.verified.png");
Compare("paths-strokes.pdf", 0, "RenderingSnapshotTests.Paths_strokes_pdf_matches_its_reviewed_snapshot.verified.png");
for (int page = 0; page < 4; page++)
{
    Compare("page-rotation.pdf", page, $"RenderingSnapshotTests.Page_rotation_pdf_matches_its_reviewed_snapshots_page={page}.verified.png");
}

Compare("clipping-paths.pdf", 0, "ClippingTests.Clipping_paths_pdf_matches_its_reviewed_snapshot.verified.png");

// The document builder (issue #397): lambdas, ref struct handles, records and C# 14 extension members must stay trimmable and AOT-clean.
using (PdfDocument built = PdfDocument.Create(doc => doc
    .WithTitle("Hello")
    .WithLanguage("en-US")
    .Content(c =>
    {
        c.Heading(1, "Hello, Broadside");
        c.Paragraph("This page is tagged, embeds its font and opens in any reader.");
        c.Table(t =>
        {
            t.Header(h =>
            {
                h.Cell("Region");
                h.Cell("Units").AlignRight();
            });
            t.Row("North", "135");
        }).Striped();
    })))
{
    using var saved = new MemoryStream();
    built.Save(saved);
    using PdfDocument reopened = PdfDocument.Open(saved.ToArray());
    Report("the document builder writes a tagged page", reopened.Pages.Count == 1 && reopened.StructureTree is not null && reopened.Language == "en-US");
}

Console.WriteLine(failures.Count == 0 ? "all checks passed" : $"{failures.Count} check(s) failed");
return failures.Count == 0 ? 0 : 1;

void Compare(string fileName, int page, string snapshot)
{
    using PdfDocument document = PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, fileName));
    using Bitmap bitmap = rasterizer.Render(document.Pages[page]);
    using var png = new MemoryStream();
    bitmap.SavePng(png);
    png.Position = 0;
    ImageResult written = ImageResult.FromStream(png, ColorComponents.RedGreenBlueAlpha);
    using FileStream file = File.OpenRead(Path.Combine(snapshots, snapshot));
    ImageResult expected = ImageResult.FromStream(file, ColorComponents.RedGreenBlueAlpha);
    Report($"{fileName} page {page + 1} matches its snapshot", SamePixels(bitmap, expected));
    Report($"{fileName} page {page + 1} round-trips through PNG", SamePixels(bitmap, written));
}

// Opaque pages: the straight RGBA of the decoder equals the bitmap's premultiplied BGRA channel for channel.
static bool SamePixels(Bitmap bitmap, ImageResult image)
{
    if (image.Width != bitmap.Width || image.Height != bitmap.Height)
    {
        return false;
    }

    for (int y = 0; y < bitmap.Height; y++)
    {
        ReadOnlySpan<byte> row = bitmap.GetRow(y);
        for (int x = 0; x < bitmap.Width; x++)
        {
            int i = ((y * bitmap.Width) + x) * 4;
            if (row[(x * 4) + 2] != image.Data[i] || row[(x * 4) + 1] != image.Data[i + 1] || row[x * 4] != image.Data[i + 2] || row[(x * 4) + 3] != image.Data[i + 3])
            {
                return false;
            }
        }
    }

    return true;
}

void Report(string check, bool passed)
{
    Console.WriteLine($"{(passed ? "pass" : "FAIL")}: {check}");
    if (!passed)
    {
        failures.Add(check);
    }
}
