using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Imposed sheets and a resized page (issue #647) rendered with the software rasterizer: a 2-up sheet, the first booklet sheet and a
/// Letter page resized to A4. ISO 32000-2 §8.10.1, §14.11.2, §8.3.4.
/// </summary>
public sealed class ImpositionRenderingTests
{
    [Fact]
    public async Task A_two_up_sheet_renders()
    {
        using PdfDocument source = Source(2);
        using PdfDocument sheets = source.CreateNUp(2, 1, new PdfImpositionOptions().WithMargin(18).WithGutter(18).WithCellFrames());

        await VerifyPage(sheets, 0);
    }

    [Fact]
    public async Task A_booklet_sheet_renders()
    {
        using PdfDocument source = Source(4);
        using PdfDocument booklet = source.CreateBooklet();

        await VerifyPage(booklet, 0);
    }

    [Fact]
    public async Task A_resized_page_renders()
    {
        using PdfDocument document = Source(1);
        document.Pages[0].AddSquareAnnotation(new PdfRectangle(100, 600, 250, 700));
        document.Pages[0].Resize(595, 842);

        await VerifyPage(document, 0);
    }

    private static async Task VerifyPage(PdfDocument document, int page)
    {
        using var output = new MemoryStream();
        await document.SaveAsync(output, TestContext.Current.CancellationToken);
        using PdfDocument reopened = await PdfDocument.OpenAsync(output.ToArray(), TestContext.Current.CancellationToken);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(reopened.Pages[page], new RenderOptions { Scale = 0.5 });
        Assert.Empty(reopened.Diagnostics);
        using MemoryStream png = PngSnapshots.Encode(bitmap);
        await Verify(png, "png");
    }

    /// <summary>
    /// Letter pages, each with a filled band of its own color, a triangle in its upper-left corner (so turning shows) and one bar per
    /// page number. Vector marks only: the Standard 14 fonts resolve to different system fonts on each platform.
    /// </summary>
    private static PdfDocument Source(int count)
    {
        PdfDocument document = PdfDocument.Create();
        for (int index = 0; index < count; index++)
        {
            PdfPage page = index == 0 ? document.Pages[0] : document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
            using PdfCanvas canvas = page.CreateCanvas();
            canvas.SetFillColor(PdfColor.FromRgb(0.2f + (0.2f * index), 0.4f, 0.8f - (0.15f * index)));
            canvas.Rectangle(36, 36, 540, 200);
            canvas.Fill();
            canvas.MoveTo(36, 756);
            canvas.LineTo(196, 756);
            canvas.LineTo(36, 596);
            canvas.ClosePath();
            canvas.Fill();
            for (int bar = 0; bar <= index; bar++)
            {
                canvas.Rectangle(240 + (bar * 60), 400, 36, 160);
            }

            canvas.Fill();
        }

        return document;
    }
}
