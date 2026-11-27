using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Bates numbers on existing pages (issue #646), rendered by the software rasterizer and compared with reviewed snapshots: the
/// number upright in the bottom-right corner as the page is viewed, on an unrotated and a quarter-turned page. ISO 32000-2
/// §14.8.2.2, §14.11.2.
/// </summary>
public sealed class BatesRenderingTests
{
    // The bundled Standard 14 programs, not the platform's fonts, so the snapshots are the same on every operating system.
    private static readonly PdfEngine Engine = new(new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task A_numbered_page_matches_its_reviewed_snapshot(int page)
    {
        using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Bytes("page-rotation.pdf"), TestContext.Current.CancellationToken);
        var bates = new PdfBatesNumbering(document.GetStandard14Font(Standard14Font.HelveticaBold), 9)
            .WithPrefix("ACME")
            .WithNextNumber(123)
            .WithMargin(6)
            .WithTextColor(PdfColor.FromRgb(0, 0, 0.7f));
        document.AddBatesNumbers(bates);

        using var output = new MemoryStream();
        await document.SaveAsync(output, TestContext.Current.CancellationToken);
        using PdfDocument reopened = await Engine.OpenAsync(output.ToArray(), TestContext.Current.CancellationToken);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(reopened.Pages[page]);
        Assert.Empty(reopened.Diagnostics);
        using MemoryStream png = PngSnapshots.Encode(bitmap);
        await Verify(png, "png").UseParameters(page);
    }
}
