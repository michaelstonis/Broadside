using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Find and replace (issue #641) on <c>text-replace.pdf</c>, rendered by the software rasterizer before and after and compared with
/// reviewed snapshots: the total keeps its subset font, the customer's name its Type 0 font with codes found through the program's
/// "cmap", the invoice heading switches to a fallback font for the letters the subset lacks, squeezed to the old width, and the
/// status in the shared form changes on page 1 only. ISO 32000-2 §9.4.3, §9.3.4, §9.9.2, §9.10.3 and §8.10.
/// </summary>
public sealed class TextReplaceRenderingTests
{
    // The bundled Standard 14 programs, not the platform's fonts, so the snapshots are the same on every operating system.
    private static readonly PdfEngine Engine = new(new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));

    [Fact]
    public async Task The_page_before_the_edit_matches_its_reviewed_snapshot()
    {
        using PdfDocument document = await Engine.OpenAsync(Corpus.Bytes("text-replace.pdf"), TestContext.Current.CancellationToken);
        await VerifyPage(document, 0);
    }

    [Fact]
    public async Task The_edited_pages_match_their_reviewed_snapshots()
    {
        using PdfDocument document = await Engine.OpenAsync(Corpus.Bytes("text-replace.pdf"), TestContext.Current.CancellationToken);
        PdfPage page = document.Pages[0];
        Assert.Equal(1, document.ReplaceText(new TextQuery("99 USD").WithMatchCase(), "19 USD", pages: 0..1).Replaced);
        Assert.Equal(1, document.ReplaceText(new TextQuery("Jane Doe").WithMatchCase(), "John Roe", pages: 0..1).Replaced);
        Assert.Equal(1, document.ReplaceText(new TextQuery("Invoice").WithMatchCase(), "Receipt", new PdfTextReplaceOptions().WithFit(PdfTextFit.Compress), 0..1).UsedFallbackFont);
        Assert.Equal(1, page.ReplaceText(Assert.Single(page.FindText(new TextQuery("Draft").WithMatchCase())), "Final").Replaced);

        using var output = new MemoryStream();
        await document.SaveAsync(output, new PdfSaveOptions().WithIncrementalUpdate(false).WithRemoveUnreachableObjects(true), TestContext.Current.CancellationToken);
        using PdfDocument reopened = await Engine.OpenAsync(output.ToArray(), TestContext.Current.CancellationToken);
        Assert.Equal("Receipt No. 1234Customer: John RoeTotal: 19 USDStatus: Final", reopened.Pages[0].ExtractText().Text.Trim());
        Assert.Equal("Status: Draft", reopened.Pages[1].ExtractText().Text.Trim());
        await VerifyPage(reopened, 0).UseTextForParameters("page1");
        await VerifyPage(reopened, 1).UseTextForParameters("page2");
    }

    private static SettingsTask VerifyPage(PdfDocument document, int index)
    {
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[index]);
        MemoryStream png = PngSnapshots.Encode(bitmap);
        return Verify(png, "png");
    }
}
