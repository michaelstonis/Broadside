using System.Text.Json;
using Broadside.GoldenImages;

namespace Broadside.Tools.RenderDiff.Tests;

/// <summary>The report a run writes: a ranked HTML page with side-by-side and diff images, the full results as JSON, and a triage table.</summary>
public sealed class ReportTests : IDisposable
{
    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task The_worst_pages_get_side_by_side_and_diff_images()
    {
        await RunWithOneDifferingPageAsync();

        string pages = Path.Combine(_workspace.Output, "pages", "page-rotation");
        GoldenImage broadside = GoldenImage.LoadPng(Path.Combine(pages, "page2.broadside.png"));
        GoldenImage reference = GoldenImage.LoadPng(Path.Combine(pages, "page2.fixture.png"));
        GoldenImage diff = GoldenImage.LoadPng(Path.Combine(pages, "page2.fixture.diff.png"));
        Assert.Equal((broadside.Width, broadside.Height), (reference.Width, reference.Height));
        Assert.Equal(16, CountPixels(diff, (255, 0, 0)));
        Assert.Equal(16, CountPixels(reference, (0, 0, 0)) - CountPixels(broadside, (0, 0, 0)));
        Assert.False(File.Exists(Path.Combine(_workspace.Output, "pages", "paths-fill-rules", "page0.broadside.png")), "Only the worst page has images.");

        string html = await File.ReadAllTextAsync(Path.Combine(_workspace.Output, "index.html"), TestContext.Current.CancellationToken);
        Assert.Contains("pages/page-rotation/page2.fixture.diff.png", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_triage_table_lists_the_worst_pages_in_rank_order_with_a_verdict_column()
    {
        await RunWithOneDifferingPageAsync();

        string[] rows = [.. (await File.ReadAllLinesAsync(Path.Combine(_workspace.Output, "triage.md"), TestContext.Current.CancellationToken))
            .Where(static line => line.StartsWith("| ", StringComparison.Ordinal))];
        Assert.Contains("Verdict", rows[0], StringComparison.Ordinal);
        Assert.Equal(3, rows.Length);
        Assert.StartsWith("| 1 | page-rotation.pdf | 2 |", rows[1], StringComparison.Ordinal);
        Assert.StartsWith("| 2 | page-rotation.pdf | 0 |", rows[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_JSON_report_holds_every_page_worst_first()
    {
        await RunWithOneDifferingPageAsync();

        using JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(_workspace.Output, "report.json"), TestContext.Current.CancellationToken));
        JsonElement[] pages = [.. json.RootElement.GetProperty("pages").EnumerateArray()];
        Assert.Equal(5, pages.Length);
        Assert.Equal("page-rotation.pdf", pages[0].GetProperty("file").GetString());
        Assert.Equal(2, pages[0].GetProperty("page").GetInt32());
        Assert.Equal(16, pages[0].GetProperty("engines")[0].GetProperty("differingPixels").GetInt32());
    }

    private static int CountPixels(GoldenImage image, (byte R, byte G, byte B) colour)
    {
        int count = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                count += image[x, y] == colour ? 1 : 0;
            }
        }

        return count;
    }

    private async Task RunWithOneDifferingPageAsync()
    {
        string reference = _workspace.ReferenceDirectory("fixture");
        Workspace.WriteBroadsideRenders(reference, "paths-fill-rules.pdf");
        Workspace.WriteBroadsideRenders(reference, "page-rotation.pdf", (page, image) => page == 2 ? Workspace.PaintBlackSquare(image, 4) : image);
        await _workspace.RunAsync(
            ["paths-fill-rules.pdf", "page-rotation.pdf"],
            options => options with { Engines = [ReferenceEngine.FromDirectory("fixture", reference)], MaxShift = 0, ReportedPages = 1, TriagePages = 2 });
    }
}
