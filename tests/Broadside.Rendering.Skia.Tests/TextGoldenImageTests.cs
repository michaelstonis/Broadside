using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// The Skia backend's renders of the text corpus files (issue #123) compared with the software rasterizer's golden images through the
/// harness: glyphs of TrueType and OpenType programs drawn natively under the text threshold, every glyph drawn from its outline
/// (forced outlines, and every other program) under the backend's path threshold, Type 3 glyphs as the paths their descriptions
/// paint. With forced outlines the render matches an independent Skia path rendering of the same outlines under the tight threshold.
/// </summary>
public sealed class TextGoldenImageTests
{
    [Theory]
    [MemberData(nameof(TextPages))]
    public void Page_drawn_with_native_glyphs_matches_the_golden_image_within_the_text_threshold(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();
        using SKBitmap bitmap = backend.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        // A platform whose font engine refuses a program draws it from its outlines and says so; nothing else may be skipped.
        Assert.All(backend.Diagnostics, diagnostic => Assert.Equal("BackendNativeGlyphsUnavailable", diagnostic.Code));
        GoldenCorpus.AssertMatchesReference("skia", fileName, page, Image(bitmap), GoldenThreshold.Text);
    }

    [Theory]
    [MemberData(nameof(TextPages))]
    public void Page_drawn_from_outlines_matches_the_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();
        using SKBitmap bitmap = backend.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions().WithForceOutlines(true));

        Assert.Empty(backend.Diagnostics);
        GoldenCorpus.AssertMatchesReference("skia", fileName, page, Image(bitmap));
    }

    [Theory]
    [InlineData("text-truetype-embedded.pdf")]
    [InlineData("text-truetype-composite.pdf")]
    [InlineData("text-cff-embedded.pdf")]
    [InlineData("text-opentype-cff-embedded.pdf")]
    [InlineData("text-type1-embedded.pdf")]
    [InlineData("text-cid-identity-h.pdf")]
    [InlineData("text-cid-identity-v.pdf")]
    [InlineData("text-standard14.pdf")]
    [InlineData("text-standard14-symbol.pdf")]
    [InlineData("text-standard14-zapfdingbats.pdf")]
    [InlineData("text-nonembedded-substitute.pdf")]
    [InlineData("text-lines-rotated.pdf")]
    [InlineData("text-type3-recursive.pdf")]
    public void Forced_outlines_match_the_skia_path_rendering_of_the_same_outlines_within_the_tight_threshold(string fileName)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        RenderOptions options = GoldenCorpus.CreateRenderOptions().WithForceOutlines(true);
        using var backend = new SkiaBackend();
        using SKBitmap outlines = backend.Render(document.Pages[0], options);
        using SKBitmap paths = SkiaPathReference.Render(document.Pages[0], document, options);

        GoldenComparison comparison = GoldenComparison.Compare(Image(paths), Image(outlines), GoldenThreshold.Tight);
        Assert.True(comparison.Matches, comparison.Describe());
    }

    public static TheoryData<string, int> TextPages() => GoldenCorpus.PagesOf([.. TextFiles()]);

    private static IEnumerable<string> TextFiles() => Corpus.AllFileNames.Where(name => name.StartsWith("text-", StringComparison.Ordinal));

    private static GoldenImage Image(SKBitmap bitmap) =>
        GoldenImage.FromPixels(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes, GoldenPixelFormat.Bgra8Premultiplied);
}
