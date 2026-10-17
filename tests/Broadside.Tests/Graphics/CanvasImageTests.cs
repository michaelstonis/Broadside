using Broadside.Content;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Placing images on a canvas (issue #153): PNG files, JPEG files and raw sample buffers become image XObjects that re-read, after
/// a save, with the original pixels. ISO 32000-2 §8.9.5 (image dictionaries), §8.9.6 (masked images), §11.6.5.2 (soft-mask
/// images), §7.4.4 (FlateDecode and predictors), §7.4.8 (DCTDecode).
/// </summary>
public sealed class CanvasImageTests
{
    [Theory]
    [MemberData(nameof(PngVectors.All), MemberType = typeof(PngVectors))]
    public void A_png_placed_on_a_page_decodes_back_to_its_pixels(string name)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImageFromPng(PngVectors.Png(name));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(image, 10, 10, image.Width, image.Height);
        }

        using PdfDocument reopened = PdfDocument.Open(Saved(document), new PdfOptions().UseStrict());
        PdfImage placed = PlacedImages(reopened).Single();

        PngVectors.AssertSamePixels(PngVectors.Reference(name), PngVectors.Read(placed), name);
        Assert.Empty(reopened.Diagnostics);
    }

    /// <summary>The images the first page paints, in order.</summary>
    internal static List<PdfImage> PlacedImages(PdfDocument document, int page = 0)
    {
        var recorder = new ImageRecorder(document);
        document.Pages[page].ProcessContent(recorder);
        return recorder.Images;
    }

    internal static byte[] Saved(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Save(output, options ?? new PdfSaveOptions());
        return output.ToArray();
    }

    /// <summary>Records each image a page paints, with the CTM it is painted under.</summary>
    internal sealed class ImageRecorder(PdfDocument document) : ContentProcessor
    {
        public List<PdfImage> Images { get; } = [];

        public List<Matrix> Transforms { get; } = [];

        public List<string> Names { get; } = [];

        public override void PaintImage(in ImageEvent image, ContentContext context)
        {
            Images.Add(document.GetImage((CosObject?)image.Reference ?? image.Stream)!);
            Transforms.Add(image.Ctm);
            Names.Add(System.Text.Encoding.ASCII.GetString(image.ResourceName));
        }
    }
}
