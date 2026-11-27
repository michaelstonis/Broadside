using Broadside.GoldenImages;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

public sealed class HeadlessHostTests
{
    public static TheoryData<string> Files => Corpus.AllFiles;

    [Theory]
    [MemberData(nameof(Files))]
    public async Task The_headless_host_opens_every_corpus_file_and_renders_page_1_through_the_surface(string fileName)
    {
        // Cooperative (the browser's mode) with 1 ms slices, so pages with real work render across several slices.
        ViewerOptions options = ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Cooperative).WithSliceBudget(TimeSpan.FromMilliseconds(1));
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes(fileName), options);
        using PdfDocument reference = GoldenCorpus.Open(fileName);
        Assert.Equal(reference.Pages.Count, session.PageCount);
        if (session.PageCount == 0)
        {
            return;
        }

        using var surface = new HeadlessSurface();
        Tile tile = await session.RenderPageAsync(0, 1, surface, TestContext.Current.CancellationToken);

        PresentedTile presented = Assert.Single(surface.Tiles);
        Assert.Equal(tile, presented.Tile);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(reference.Pages[0], GoldenCorpus.CreateRenderOptions());
        // A viewer decodes images at the resolution they are drawn at (issue #362): image-oversampled.pdf's 1200 x 1200 image drawn
        // 72 pixels wide is box-averaged by 8 before it is sampled, which rounds a level away from sampling the full decode.
        int tolerance = fileName == "image-oversampled.pdf" ? 1 : 0;
        Assert.InRange(ViewerTestSupport.MaxDifference(expected, presented.Pixels), 0, tolerance);
        Assert.Equal(new SessionStatistics(0, 0, 1, 0), session.Statistics);
    }

    [Fact]
    public void The_viewer_core_references_no_ui_framework()
    {
        // Framework-neutral: only the BCL and Broadside's own managed packages. A UI framework (ASP.NET Core/Blazor, WPF, WinForms,
        // MAUI, Avalonia, Uno, SkiaSharp) would show up here.
        string[] references = [.. typeof(DocumentSession).Assembly.GetReferencedAssemblies().Select(name => name.Name!)];

        Assert.All(references, name => Assert.True(
            name is "Broadside" or "Broadside.Rendering" or "netstandard" || name.StartsWith("System.", StringComparison.Ordinal) || name == "System",
            $"Broadside.Viewer references {name}."));
        Assert.Contains("Broadside.Rendering", references);
    }
}
