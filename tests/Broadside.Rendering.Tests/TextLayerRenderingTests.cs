using Broadside.Graphics;
using Broadside.Images;
using Broadside.Text;

namespace Broadside.Rendering.Tests;

/// <summary>
/// An OCR text layer (issue #649) is invisible: a scanned page renders pixel for pixel the same before and after the layer is added,
/// upright and on a rotated page. ISO 32000-2 §9.3.6 (rendering mode 3) and §14.8.2.4.
/// </summary>
public sealed class TextLayerRenderingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public void A_scanned_page_renders_the_same_with_its_text_layer(int rotation)
    {
        using PdfDocument document = Scan(rotation);
        byte[] before = Render(document);

        document.Pages[0].AddTextLayer(new OcrPage(612, 792,
        [
            new OcrLine([new OcrWord("Scanned", new PdfRectangle(72, 72, 240, 110)), new OcrWord("page", new PdfRectangle(260, 72, 360, 110))]),
            new OcrLine([new OcrWord("sideways", new PdfRectangle(500, 300, 530, 500))], BaselineAngle: 90),
        ]));
        byte[] after = Render(document);

        Assert.Equal(before.Length, after.Length);
        Assert.True(before.AsSpan().SequenceEqual(after), "the text layer changed a pixel");
        Assert.Contains("Scanned", document.Pages[0].ExtractText().Text, StringComparison.Ordinal);
    }

    /// <summary>A page covered by a gray image with a dark band, as a scanner writes it.</summary>
    private static PdfDocument Scan(int rotation)
    {
        PdfDocument document = PdfDocument.Create();
        document.Pages[0].Rotation = rotation;
        const int width = 153;
        const int height = 198;
        byte[] samples = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                samples[(y * width) + x] = (byte)(y is > 20 and < 30 && x is > 18 and < 90 ? 30 : 230 - ((x + y) % 17));
            }
        }

        PdfImage scan = document.CreateImage(samples, new PdfImageLayout(width, height, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(scan, 0, 0, 612, 792);
        }

        return document;
    }

    private static byte[] Render(PdfDocument document)
    {
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);
        return bitmap.Pixels.ToArray();
    }
}
