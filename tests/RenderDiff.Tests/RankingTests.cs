using Broadside.GoldenImages;

namespace Broadside.Tools.RenderDiff.Tests;

/// <summary>Pages are ranked by the golden-image comparison of Broadside's render with each reference render.</summary>
public sealed class RankingTests : IDisposable
{
    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task A_page_that_differs_from_its_reference_ranks_above_pages_that_match()
    {
        string reference = _workspace.ReferenceDirectory("fixture");
        Workspace.WriteBroadsideRenders(reference, "paths-fill-rules.pdf");
        Workspace.WriteBroadsideRenders(reference, "page-rotation.pdf", (page, image) => page == 2 ? Workspace.PaintBlackSquare(image, 4) : image);

        RenderDiffReport report = await _workspace.RunAsync(
            ["paths-fill-rules.pdf", "page-rotation.pdf"],
            options => options with { Engines = [ReferenceEngine.FromDirectory("fixture", reference)], MaxShift = 0 });

        PageDiff worst = report.Pages[0];
        Assert.Equal(("page-rotation.pdf", 2), (worst.File, worst.Page));
        EngineDiff diff = Assert.Single(worst.Engines);
        Assert.Equal("fixture", diff.Engine);
        Assert.Equal(16, diff.DifferingPixels);
        Assert.Equal(5, report.Pages.Count);
        Assert.All(report.Pages.Skip(1), page => Assert.Equal(0, Assert.Single(page.Engines).DifferingPixels));
    }
}
