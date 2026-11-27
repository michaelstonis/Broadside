using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Stamps and headers and footers on existing pages (issue #627), rendered by the software rasterizer and compared with reviewed
/// snapshots: a translucent rotated watermark centered as the page is viewed, a background band under the content, and footers with
/// page labels. ISO 32000-2 §14.8.2.2, §12.4.2, §14.11.2, §11.6.4.4.
/// </summary>
public sealed class StampRenderingTests
{
    // The bundled Standard 14 programs, not the platform's fonts, so the snapshots are the same on every operating system.
    private static readonly PdfEngine Engine = new(new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));

    [Theory]
    [InlineData("page-rotation.pdf", 0)]
    [InlineData("page-rotation.pdf", 1)]
    [InlineData("page-rotation.pdf", 2)]
    [InlineData("page-rotation.pdf", 3)]
    [InlineData("page-labels.pdf", 0)]
    [InlineData("page-labels.pdf", 3)]
    public async Task Stamped_pages_match_their_reviewed_snapshots(string fileName, int page)
    {
        using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Bytes(fileName), TestContext.Current.CancellationToken);
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.HelveticaBold);
        double size = Math.Min(document.Pages[page].CropBox.Width, document.Pages[page].CropBox.Height) / 612;
        document.Stamp(PdfStamp.FromText("DRAFT", font, 120 * size).WithRotation(45).WithOpacity(0.35).WithTextColor(PdfColor.FromRgb(0.8f, 0, 0)));
        document.Stamp(PdfStamp.FromText("{label} ({page} of {pages})", font, 20 * size)
            .WithLayer(PdfStampLayer.Background)
            .WithHorizontalAlignment(PdfStampAlignment.Start)
            .WithVerticalAlignment(PdfStampAlignment.Start)
            .WithOffsetX(40 * size)
            .WithOffsetY(-40 * size)
            .WithTextColor(PdfColor.FromRgb(0, 0, 0.7f)));
        document.AddHeaderFooter(new PdfHeaderFooter(document.GetStandard14Font(Standard14Font.Helvetica), 12 * size)
        {
            Margin = 36 * size,
            HeaderRight = "Broadside",
            FooterCenter = "Page {label}",
        });

        using var output = new MemoryStream();
        await document.SaveAsync(output, TestContext.Current.CancellationToken);
        using PdfDocument reopened = await Engine.OpenAsync(output.ToArray(), TestContext.Current.CancellationToken);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(reopened.Pages[page]);
        Assert.Empty(reopened.Diagnostics);
        using MemoryStream png = PngSnapshots.Encode(bitmap);
        await Verify(png, "png").UseParameters(Path.GetFileNameWithoutExtension(fileName), page);
    }
}
