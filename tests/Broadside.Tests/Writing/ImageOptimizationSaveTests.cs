using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Tests.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Saving with <see cref="PdfSaveOptions.ImageOptimization"/> (issue #625): the file is smaller, every placement of an image changes
/// at once, the colour space is kept, an incremental update refuses the option, and strict mode throws for a decoding deviation.
/// The pixel comparison of the saved file is in <c>Broadside.Rendering.Tests</c> (<c>ImageOptimizationRenderTests</c>), which has
/// the rasterizer. ISO 32000-2 §7.5.6, §8.9.5.3, §7.4.4 and §7.4.8.
/// </summary>
public sealed class ImageOptimizationSaveTests
{
    [Fact]
    public void The_oversampled_corpus_file_saves_smaller_with_the_image_resampled_in_place()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-oversampled.pdf"));
        CosReference reference = document.Pages[0].ExtractImages()[0].Image.Reference!;
        byte[] original = File.ReadAllBytes(Corpus.Path("image-oversampled.pdf"));

        byte[] saved = Save(document, new PdfSaveOptions().WithImageOptimization(new PdfImageOptimization().WithMaxResolution(150).WithJpegQuality(80).WithRecompressLossless()));

        Assert.True(saved.Length < original.Length, $"{saved.Length} bytes saved, {original.Length} before.");
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfImage image = reopened.Pages[0].ExtractImages()[0].Image;
        Assert.Equal(reference.ObjectNumber, image.Reference!.ObjectNumber);
        Assert.Equal((150, 150), (image.Width, image.Height));
        Assert.Equal(FilterNames.DctDecode, image.Dictionary[FilterNames.Filter]);
        using DecodedImage decoded = image.Decode()!;

        // Red ramps left to right, green top to bottom, blue is 128: the averages keep them, within JPEG's error.
        ReadOnlySpan<byte> middle = decoded.GetRow(75);
        Assert.InRange(middle[0], 0, 12);
        Assert.InRange(middle[(149 * 3) + 0], 243, 255);
        Assert.InRange(middle[(75 * 3) + 1], 115, 140);
        Assert.InRange(middle[(75 * 3) + 2], 118, 138);
    }

    [Fact]
    public void Every_placement_of_a_resampled_image_shows_the_new_stream()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        PdfImage image = document.CreateImage(ImageOptimizationTests.Gradient(800, 800, 3), new PdfImageLayout(800, 800, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(image, 72, 72, 144, 144);
        }

        using (PdfCanvas canvas = document.Pages[1].CreateCanvas())
        {
            canvas.DrawImage(image, 72, 72, 72, 72);
        }

        using PdfDocument reopened = PdfDocument.Open(Save(document, new PdfSaveOptions().WithImageOptimization(new PdfImageOptimization().WithMaxResolution(100))));

        PdfImage first = reopened.Pages[0].ExtractImages()[0].Image;
        PdfImage second = reopened.Pages[1].ExtractImages()[0].Image;
        Assert.Equal(first.Reference, second.Reference);
        Assert.Equal((200, 200), (first.Width, first.Height));
    }

    [Fact]
    public void An_incremental_update_refuses_image_optimization()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-oversampled.pdf"));
        using var stream = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => document.Save(stream, new PdfSaveOptions().WithIncrementalUpdate(true).WithImageOptimization(new PdfImageOptimization())));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void A_save_without_the_option_leaves_images_alone()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-oversampled.pdf"));

        using PdfDocument reopened = PdfDocument.Open(Save(document, new PdfSaveOptions()));

        Assert.Equal(1200, reopened.Pages[0].ExtractImages()[0].Image.Width);
    }

    [Fact]
    public void Strict_mode_throws_for_an_image_that_fails_to_decode_and_changes_nothing()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict());
        PdfImage image = document.CreateImage(ImageOptimizationTests.Gradient(600, 600, 1), new PdfImageLayout(600, 600, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        image.Stream!.EncodedData = "not deflate data at all"u8.ToArray();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(image, 72, 72, 72, 72);
        }

        using var stream = new MemoryStream();
        Assert.Throws<DiagnosticException>(() => document.Save(stream, new PdfSaveOptions().WithImageOptimization(new PdfImageOptimization().WithMaxResolution(150))));
        Assert.Equal(600, image.Width);
    }

    [Fact]
    public void A_pdf_a_2b_save_keeps_the_image_colour_space()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage(ImageOptimizationTests.Noise(600, 600, 3), new PdfImageLayout(600, 600, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(image, 72, 72, 72, 72);
        }

        byte[] saved = Save(document, new PdfSaveOptions()
            .WithPdfAConformance(PdfAConformance.PdfA2b)
            .WithImageOptimization(new PdfImageOptimization().WithMaxResolution(150).WithJpegQuality(75).WithRecompressLossless()));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "PdfANonConformant" or "PdfANotIdentified");
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfImage written = reopened.Pages[0].ExtractImages()[0].Image;
        Assert.Equal((150, 150), (written.Width, written.Height));
        Assert.Equal(PdfColorSpaceFamily.DeviceRgb, written.ColorSpace!.Family);
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options);
        return stream.ToArray();
    }
}
