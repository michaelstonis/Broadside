using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Search and redact (issue #633) on <c>redact-pii.pdf</c>, rendered by the software rasterizer and compared with a reviewed snapshot:
/// the email address, the Social Security number and both card numbers give way to black boxes with white overlay text, one per line
/// a match covers, and the labels beside them stay. ISO 32000-2 §12.5.6.23 (Table 195), §9.4.3.
/// </summary>
public sealed class SearchRedactionRenderingTests
{
    // The bundled Standard 14 programs, not the platform's fonts, so the snapshot is the same on every operating system.
    private static readonly PdfEngine Engine = new(new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));

    [Fact]
    public async Task The_redacted_page_matches_its_reviewed_snapshot()
    {
        using PdfDocument document = await Engine.OpenAsync(Corpus.Bytes("redact-pii.pdf"), TestContext.Current.CancellationToken);
        PdfRedactionResult result = document.Redact(
            [TextPatterns.EmailAddress, TextPatterns.UsSocialSecurityNumber, TextPatterns.CreditCardNumber],
            new PdfRedactionOptions().WithOverlayText("REDACTED"));

        using var output = new MemoryStream();
        await document.SaveAsync(output, result.CreateSaveOptions(), TestContext.Current.CancellationToken);
        using PdfDocument reopened = await Engine.OpenAsync(output.ToArray(), TestContext.Current.CancellationToken);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(reopened.Pages[0]);
        Assert.Empty(reopened.Diagnostics);
        using MemoryStream png = PngSnapshots.Encode(bitmap);
        await Verify(png, "png");
    }
}
