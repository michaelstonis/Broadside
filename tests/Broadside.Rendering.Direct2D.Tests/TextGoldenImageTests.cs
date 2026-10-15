using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// The Direct2D backend's renders of the text corpus (issue #131) match the software rasterizer's golden images within the text
/// threshold (<see cref="GoldenThreshold.Text"/>, as every native-glyph backend): glyphs of TrueType and
/// OpenType programs drawn by DirectWrite, all others from their outlines, Type 3 glyphs as the paints of their descriptions
/// (ISO 32000-2 §9.4.3, §9.3.6, §9.6.4, §9.9). Rendered on WARP, so the result does not depend on the graphics hardware.
/// </summary>
public sealed class TextGoldenImageTests
{
    private const string Backend = "direct2d";

    /// <summary>
    /// Text pages that also paint images or stencil masks, which the Direct2D backend draws from issue #130 on; until then they are
    /// skipped with a diagnostic (<c>RendererTests</c>), so their pages cannot match. #130 removes them from this list.
    /// </summary>
    private static readonly HashSet<(string FileName, int Page)> WithImages =
    [
        ("text-render-modes.pdf", 1),
        ("text-render-modes.pdf", 2),
        ("text-render-modes.pdf", 3),
        ("text-render-modes.pdf", 4),
        ("text-tagged-spanning-image.pdf", 0),
        ("text-type3.pdf", 0),
    ];

    [Theory]
    [MemberData(nameof(TextPages))]
    public void Page_matches_the_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap bitmap = renderer.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        Assert.DoesNotContain(renderer.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        GoldenCorpus.AssertMatchesReference(Backend, fileName, page, GoldenImage.FromBitmap(bitmap), GoldenThreshold.Text);
    }

    [Fact]
    public void The_text_pages_left_out_are_those_whose_images_the_backend_skips()
    {
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        foreach ((string fileName, int page) in WithImages)
        {
            using PdfDocument document = GoldenCorpus.Open(fileName);
            using Bitmap bitmap = renderer.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

            Assert.Contains(renderer.Diagnostics, diagnostic => diagnostic.Code == "RenderingOperationSkipped");
        }
    }

    public static TheoryData<string, int> TextPages()
    {
        var pages = new TheoryData<string, int>();
        foreach (string fileName in Corpus.AllFileNames.Where(name => name.StartsWith("text-", StringComparison.Ordinal)))
        {
            using PdfDocument document = GoldenCorpus.Open(fileName);
            for (int page = 0; page < document.Pages.Count; page++)
            {
                if (!WithImages.Contains((fileName, page)))
                {
                    pages.Add(fileName, page);
                }
            }
        }

        return pages;
    }
}
