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
    /// <summary>
    /// The pages of the text corpus files that also paint an image or a stencil mask, which this backend does not draw yet (#122):
    /// they cannot match their golden images until it does.
    /// </summary>
    private static readonly (string File, int Page)[] PagesWithImages =
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

    [Theory]
    [MemberData(nameof(TextPagesWithImages))]
    public void Text_pages_with_images_skip_only_the_images(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();
        using SKBitmap bitmap = backend.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        Diagnostic diagnostic = Assert.Single(backend.Diagnostics);
        Assert.Contains(diagnostic.Code, (string[])["BackendImageUnsupported", "BackendImageMaskUnsupported"]);
    }

    public static TheoryData<string, int> TextPages()
    {
        var pages = new TheoryData<string, int>();
        foreach (TheoryDataRow<string, int> row in GoldenCorpus.PagesOf([.. TextFiles()]))
        {
            if (!PagesWithImages.Contains(row.Data))
            {
                pages.Add(row.Data.Item1, row.Data.Item2);
            }
        }

        return pages;
    }

    public static TheoryData<string, int> TextPagesWithImages()
    {
        var pages = new TheoryData<string, int>();
        foreach ((string file, int page) in PagesWithImages)
        {
            pages.Add(file, page);
        }

        return pages;
    }

    private static IEnumerable<string> TextFiles() => Corpus.AllFileNames.Where(name => name.StartsWith("text-", StringComparison.Ordinal));

    private static GoldenImage Image(SKBitmap bitmap) =>
        GoldenImage.FromPixels(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes, GoldenPixelFormat.Bgra8Premultiplied);
}
