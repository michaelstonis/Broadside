using Broadside.GoldenImages;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// The Direct2D backend matches the software rasterizer's golden images (issue #120) for the path corpus: fills with both rules,
/// strokes with every line parameter, and clips (ISO 32000-2 §8.5.3, §8.5.4, §8.4.3, §10.7.5), plus the page mapping of
/// <c>page-rotation.pdf</c>. Rendered on a WARP device, so the result does not depend on the machine's graphics hardware.
/// </summary>
public sealed class GoldenImageTests
{
    private const string Backend = "direct2d";

    [Theory]
    [MemberData(nameof(PathPages))]
    public void Page_matches_the_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap bitmap = renderer.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        GoldenCorpus.AssertMatchesReference(Backend, fileName, page, GoldenImage.FromBitmap(bitmap));
    }

    public static TheoryData<string, int> PathPages() =>
        GoldenCorpus.PagesOf("paths-fill-rules.pdf", "paths-strokes.pdf", "clipping-paths.pdf", "page-rotation.pdf");
}
