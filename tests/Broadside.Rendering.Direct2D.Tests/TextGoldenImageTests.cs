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

    public static TheoryData<string, int> TextPages()
    {
        var pages = new TheoryData<string, int>();
        foreach (string fileName in Corpus.AllFileNames.Where(name => name.StartsWith("text-", StringComparison.Ordinal)))
        {
            using PdfDocument document = GoldenCorpus.Open(fileName);
            for (int page = 0; page < document.Pages.Count; page++)
            {
                pages.Add(fileName, page);
            }
        }

        return pages;
    }
}
