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

    [Theory]
    [InlineData("image-1bpc.pdf")]
    [InlineData("image-2bpc.pdf")]
    [InlineData("image-4bpc-indexed.pdf")]
    [InlineData("image-16bpc.pdf")]
    [InlineData("image-decode-inverted.pdf")]
    [InlineData("inline-image.pdf")]
    [InlineData("inline-image-filters.pdf")]
    [InlineData("dct-baseline.pdf")]
    [InlineData("dct-cmyk.pdf")]
    [InlineData("dct-progressive.pdf")]
    [InlineData("jpx-lossless.pdf")]
    [InlineData("jbig2-generic.pdf")]
    [InlineData("ccitt-g4.pdf")]
    [InlineData("image-stencil-mask.pdf")]
    [InlineData("image-explicit-mask.pdf")]
    [InlineData("image-color-key-mask.pdf")]
    [InlineData("image-smask.pdf")]
    [InlineData("image-smask-matte.pdf")]
    [InlineData("ccitt-blackis1-mask.pdf")]
    [InlineData("jpx-smask-in-data.pdf")]
    public async Task Image_files_match_their_reviewed_snapshots(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);
        using MemoryStream png = PngSnapshots.Encode(bitmap);
        await Verify(png, "png").UseParameters(Path.GetFileNameWithoutExtension(fileName));
    }

    private static async Task VerifyPage(string fileName, int page)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[page]);
        using MemoryStream png = PngSnapshots.Encode(bitmap);
        await Verify(png, "png").UseParameters(page);
    }
}
