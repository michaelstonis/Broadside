using Broadside.GoldenImages;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// The Skia backend's renders of the path corpus files (fills, strokes, clips) compared with the software rasterizer's golden images
/// through the harness (issue #121), under the threshold <see cref="GoldenThresholds"/> gives the <c>skia</c> backend.
/// </summary>
public sealed class PathGoldenImageTests
{
    [Theory]
    [MemberData(nameof(PathPages))]
    public void Page_matches_the_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();
        using SKBitmap bitmap = backend.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        Assert.Empty(backend.Diagnostics);
        GoldenCorpus.AssertMatchesReference(
            "skia",
            fileName,
            page,
            GoldenImage.FromPixels(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes, GoldenPixelFormat.Bgra8Premultiplied));
    }

    public static TheoryData<string, int> PathPages() => GoldenCorpus.PagesOf("paths-fill-rules.pdf", "paths-strokes.pdf", "clipping-paths.pdf");
}
