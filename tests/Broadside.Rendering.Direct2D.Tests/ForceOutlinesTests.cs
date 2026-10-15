using Broadside.GoldenImages;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// With <see cref="RenderOptions.ForceOutlines"/>, the Direct2D backend's text is its path rendering of the glyph outlines (issue
/// #131): every page here renders within the tight threshold of the same outlines written as ordinary path objects
/// (<see cref="OutlinePage"/>) unless a <c>direct2d-outlines</c> row of <see cref="GoldenThresholds"/> says otherwise, filled by the non-zero winding rule and stroked with the run's line parameters (ISO 32000-2 §9.2.4,
/// §9.3.6). TrueType outlines (quadratic curves), CFF and Type 1 outlines (cubic curves), a composite font, vertical writing, stroked
/// modes and Standard 14 substitutes are covered.
/// </summary>
public sealed class ForceOutlinesTests
{
    private const string Outlines = "direct2d-outlines";

    [Theory]
    [InlineData("text-truetype-embedded.pdf")]
    [InlineData("text-truetype-composite.pdf")]
    [InlineData("text-opentype-cff-embedded.pdf")]
    [InlineData("text-cff-embedded.pdf")]
    [InlineData("text-type1-embedded.pdf")]
    [InlineData("text-cid-identity-h.pdf")]
    [InlineData("text-cid-identity-v.pdf")]
    [InlineData("text-render-modes.pdf")]
    [InlineData("text-standard14.pdf")]
    [InlineData("text-lines-rotated.pdf")]
    public void Forced_outlines_match_the_path_rendering_of_the_same_outlines(string fileName)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using PdfDocument paths = PdfDocument.Open(OutlinePage.Write(document.Pages[0]));
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        RenderOptions options = GoldenCorpus.CreateRenderOptions().WithForceOutlines(true);

        using Bitmap text = renderer.Render(document.Pages[0], options);
        using Bitmap expected = renderer.Render(paths.Pages[0], options);

        GoldenThreshold threshold = GoldenThresholds.Rules.Any(rule => rule.Backend == Outlines && rule.FileName == fileName)
            ? GoldenThresholds.For(Outlines, fileName, 0)
            : GoldenThreshold.Tight;
        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), GoldenImage.FromBitmap(text), threshold);
        Assert.True(comparison.Matches, comparison.Describe());
        Assert.Contains(text.Pixels.ToArray(), value => value != 255);
    }
}
