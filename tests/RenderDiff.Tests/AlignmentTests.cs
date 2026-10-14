namespace Broadside.Tools.RenderDiff.Tests;

/// <summary>Reference images are lined up with Broadside's before they are compared: engines round page sizes and origins differently.</summary>
public sealed class AlignmentTests : IDisposable
{
    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task A_reference_one_pixel_off_lines_up_within_the_shift_limit()
    {
        string reference = _workspace.ReferenceDirectory("shifted");
        Workspace.WriteBroadsideRenders(reference, "paths-fill-rules.pdf", (_, image) => Workspace.Shift(image, 1, 0));

        RenderDiffReport report = await _workspace.RunAsync(
            ["paths-fill-rules.pdf"],
            options => options with { Engines = [ReferenceEngine.FromDirectory("shifted", reference)], MaxShift = 1 });

        EngineDiff diff = Assert.Single(Assert.Single(report.Pages).Engines);
        Assert.Equal((-1, 0), (diff.OffsetX, diff.OffsetY));
        Assert.Equal(0, diff.DifferingPixels);
    }

    [Fact]
    public async Task Without_a_shift_limit_a_reference_one_pixel_off_differs_at_every_edge()
    {
        string reference = _workspace.ReferenceDirectory("shifted");
        Workspace.WriteBroadsideRenders(reference, "paths-fill-rules.pdf", (_, image) => Workspace.Shift(image, 1, 0));

        RenderDiffReport report = await _workspace.RunAsync(
            ["paths-fill-rules.pdf"],
            options => options with { Engines = [ReferenceEngine.FromDirectory("shifted", reference)], MaxShift = 0 });

        EngineDiff diff = Assert.Single(Assert.Single(report.Pages).Engines);
        Assert.Equal((0, 0), (diff.OffsetX, diff.OffsetY));
        Assert.True(diff.DifferingPixels > 100, $"{diff.DifferingPixels} pixels differ.");
    }

    [Fact]
    public async Task A_reference_that_differs_everywhere_is_not_moved_to_shave_pixels_off_its_border()
    {
        string reference = _workspace.ReferenceDirectory("black");
        Workspace.WriteBroadsideRenders(reference, "paths-fill-rules.pdf", (_, image) => Workspace.Fill(image, (0, 0, 0)));

        RenderDiffReport report = await _workspace.RunAsync(
            ["paths-fill-rules.pdf"],
            options => options with { Engines = [ReferenceEngine.FromDirectory("black", reference)], MaxShift = 1 });

        EngineDiff diff = Assert.Single(Assert.Single(report.Pages).Engines);
        Assert.Equal((0, 0), (diff.OffsetX, diff.OffsetY));
    }

    [Fact]
    public async Task A_reference_of_another_size_is_compared_on_the_larger_canvas()
    {
        string reference = _workspace.ReferenceDirectory("wider");
        Workspace.WriteBroadsideRenders(reference, "paths-fill-rules.pdf", (_, image) => Workspace.Widen(image, 1));

        RenderDiffReport report = await _workspace.RunAsync(
            ["paths-fill-rules.pdf"],
            options => options with { Engines = [ReferenceEngine.FromDirectory("wider", reference)] });

        PageDiff page = Assert.Single(report.Pages);
        EngineDiff diff = Assert.Single(page.Engines);
        Assert.Equal((400, 300), (page.Width, page.Height));
        Assert.Equal((401, 300), (diff.ReferenceWidth, diff.ReferenceHeight));
        Assert.Equal(0, diff.DifferingPixels);
    }
}
