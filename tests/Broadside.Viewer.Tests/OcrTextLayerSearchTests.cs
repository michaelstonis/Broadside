using Broadside.Text;

namespace Broadside.Viewer.Tests;

/// <summary>Find in a scanned page that carries an OCR text layer (issue #649): the viewer's search finds the invisible words.</summary>
public sealed class OcrTextLayerSearchTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Document_search_finds_a_word_of_an_ocr_text_layer_over_its_box()
    {
        byte[] file;
        using (PdfDocument document = PdfDocument.Create())
        {
            // A 1224 by 1584 pixel scan of the Letter page, two pixels per point.
            document.Pages[0].AddTextLayer(new OcrPage(1224, 1584,
            [
                new OcrLine([new OcrWord("Quarterly", new PdfRectangle(200, 200, 440, 250)), new OcrWord("invoice", new PdfRectangle(460, 200, 640, 250))]),
            ]));
            using var output = new MemoryStream();
            await document.SaveAsync(output, Token);
            file = output.ToArray();
        }

        await using DocumentSession session = DocumentSession.Open(file, new ViewerOptions().WithThreading(ViewerThreading.Background));
        using var search = new DocumentSearch(session);

        search.Search("INVOICE");
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (search.IsIndexing || !search.IsSearchComplete)
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(5, Token);
        }

        SearchMatch match = Assert.Single(search.Matches);
        Assert.Equal(("invoice", 0), (match.Text, match.PageIndex));
        Assert.Equal(230, match.Bounds.Left, 0.5);
        Assert.Equal(792 - 125, match.Bounds.Bottom, 0.5);
        Assert.Equal(320, match.Bounds.Right, 0.5);
        Assert.Equal(792 - 100, match.Bounds.Top, 0.5);
    }
}
