using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Reviewed renders of the rendering corpus files at 72 DPI, compared pixel for pixel (<see cref="PngSnapshots"/>). Review a changed
/// snapshot as carefully as code: these images are what the other backends will be compared with.
/// </summary>
public sealed class RenderingSnapshotTests
{
    [Fact]
    public Task Paths_fill_rules_pdf_matches_its_reviewed_snapshot() => VerifyPage("paths-fill-rules.pdf", 0);

    [Fact]
    public Task Paths_strokes_pdf_matches_its_reviewed_snapshot() => VerifyPage("paths-strokes.pdf", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public Task Page_rotation_pdf_matches_its_reviewed_snapshots(int page) => VerifyPage("page-rotation.pdf", page);

    private static async Task VerifyPage(string fileName, int page)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[page]);
        using MemoryStream png = PngSnapshots.Encode(bitmap);
        await Verify(png, "png").UseParameters(page);
    }
}
