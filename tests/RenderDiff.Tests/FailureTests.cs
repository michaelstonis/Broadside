namespace Broadside.Tools.RenderDiff.Tests;

/// <summary>What cannot be compared is reported, never thrown: files Broadside cannot open, pages a reference did not draw, engines that are not installed.</summary>
public sealed class FailureTests : IDisposable
{
    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task A_file_Broadside_cannot_open_is_a_failure_and_the_other_files_are_still_compared()
    {
        string reference = _workspace.ReferenceDirectory("fixture");
        Workspace.WriteBroadsideRenders(reference, "paths-fill-rules.pdf");

        RenderDiffReport report = await _workspace.RunAsync(
            ["encrypted-user-password.pdf", "paths-fill-rules.pdf"],
            options => options with { Engines = [ReferenceEngine.FromDirectory("fixture", reference)] });

        PageDiff failure = Assert.Single(report.Failures);
        Assert.Equal("encrypted-user-password.pdf", failure.File);
        Assert.Contains("Password", failure.BroadsideError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("paths-fill-rules.pdf", Assert.Single(report.Pages).File);
    }

    [Fact]
    public async Task A_page_no_reference_drew_is_a_failure_that_says_so()
    {
        string reference = _workspace.ReferenceDirectory("fixture");

        RenderDiffReport report = await _workspace.RunAsync(
            ["paths-fill-rules.pdf"],
            options => options with { Engines = [ReferenceEngine.FromDirectory("fixture", reference)] });

        Assert.Empty(report.Pages);
        PageDiff failure = Assert.Single(report.Failures);
        Assert.Null(failure.BroadsideError);
        Assert.Equal("no image", Assert.Single(failure.Engines).Error);
    }

    [Fact]
    public async Task Pages_Broadside_does_not_draw_within_the_time_budget_are_failures()
    {
        string reference = _workspace.ReferenceDirectory("fixture");
        Workspace.WriteBroadsideRenders(reference, "page-rotation.pdf");

        RenderDiffReport report = await _workspace.RunAsync(
            ["page-rotation.pdf"],
            options => options with { Engines = [ReferenceEngine.FromDirectory("fixture", reference)], Timeout = TimeSpan.FromTicks(1) });

        Assert.Empty(report.Pages);
        Assert.Equal(4, report.Failures.Count);
        Assert.All(report.Failures, page => Assert.Contains("the file took more than", page.BroadsideError, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_engine_that_is_not_installed_is_skipped_with_how_to_install_it()
    {
        string empty = _workspace.ReferenceDirectory("none");
        string fixture = _workspace.ReferenceDirectory("fixture");
        Workspace.WriteBroadsideRenders(fixture, "paths-fill-rules.pdf");

        RenderDiffReport report = await _workspace.RunAsync(
            ["paths-fill-rules.pdf"],
            options => options with
            {
                Engines = [ReferenceEngine.Create("pdfium", empty, Workspace.ScriptDirectory), ReferenceEngine.FromDirectory("fixture", fixture)],
            });

        string skipped = Assert.Single(report.SkippedEngines);
        Assert.StartsWith("Skipping pdfium:", skipped, StringComparison.Ordinal);
        Assert.Contains("acquire-references.sh pdfium", skipped, StringComparison.Ordinal);
        Assert.Contains(skipped, _workspace.Log.ToString(), StringComparison.Ordinal);
        Assert.Equal(["fixture"], report.Engines);
        Assert.Equal("fixture", Assert.Single(Assert.Single(report.Pages).Engines).Engine);
    }
}
