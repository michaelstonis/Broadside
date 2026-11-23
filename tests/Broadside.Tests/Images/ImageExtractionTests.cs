using Broadside.Content;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// Extracting painted images through <see cref="PdfPage.ExtractImages()"/>, <see cref="ImageExtractor"/> and
/// <see cref="PdfImage.TryGetJpegData"/>. ISO 32000-2 §7.4.8, §8.6.5.6, §8.9.4, §8.9.5, §8.9.6.2, §8.9.7, §8.11.3.1, §14.8.2.2.
/// </summary>
public sealed class ImageExtractionTests
{
    [Fact]
    public void ExtractImages_finds_an_image_XObject_with_its_name_bounds_quad_and_rendering_intent()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-1bpc.pdf"));

        IReadOnlyList<ExtractedImage> images = document.Pages[0].ExtractImages();

        ExtractedImage extracted = Assert.Single(images);
        Assert.Equal(new CosName("Im0"), extracted.Name);
        Assert.Same(document, extracted.Image.Document);
        Assert.Equal((10, 3), (extracted.Image.Width, extracted.Image.Height));
        Assert.Equal(new PdfRectangle(72, 600, 172, 700), extracted.Bounds);
        Assert.Equal(new PathPoint(72, 600), extracted.Quad.BottomLeft);
        Assert.Equal(new PathPoint(172, 600), extracted.Quad.BottomRight);
        Assert.Equal(new PathPoint(172, 700), extracted.Quad.TopRight);
        Assert.Equal(new PathPoint(72, 700), extracted.Quad.TopLeft);
        Assert.Equal(RenderingIntent.Perceptual, extracted.RenderingIntent);
        Assert.Equal(ExtractedImageFlags.None, extracted.Flags);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void ExtractImages_finds_an_inline_image_with_its_inline_flag_and_no_resource_name()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("inline-image.pdf"));

        IReadOnlyList<ExtractedImage> images = document.Pages[0].ExtractImages();

        ExtractedImage extracted = Assert.Single(images);
        Assert.Null(extracted.Name);
        Assert.True(extracted.Image.IsInline);
        Assert.Equal(ExtractedImageFlags.Inline, extracted.Flags);
        Assert.Equal((2, 2), (extracted.Image.Width, extracted.Image.Height));
        Assert.Equal(new PdfRectangle(72, 600, 172, 700), extracted.Bounds);
    }

    [Fact]
    public void ExtractImages_captures_fill_colour_for_image_masks_and_omits_them_when_WithImageMasks_is_false()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-stencil-mask.pdf"));

        ExtractedImage extracted = Assert.Single(document.Pages[0].ExtractImages());
        Assert.True(extracted.Image.IsImageMask);
        Assert.Equal(ExtractedImageFlags.ImageMask, extracted.Flags);
        Assert.Equal(PdfColor.FromRgb(1f, 0f, 0f), extracted.FillColor);

        IReadOnlyList<ExtractedImage> withoutMasks = document.Pages[0].ExtractImages(new ImageExtractionOptions().WithImageMasks(false));
        Assert.Empty(withoutMasks);
    }

    [Fact]
    public void ExtractImages_walks_into_form_XObjects_and_excludes_artifacts_unless_requested()
    {
        string image = ContentPdf.Stream("\u0000\u00FF", "/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8");
        string form = ContentPdf.Stream("10 0 0 20 5 6 cm /Im0 Do", "/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Matrix [2 0 0 2 10 20] /Resources << /XObject << /Im0 5 0 R >> >>");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/XObject << /Fm 6 0 R >>",
            "1 0 0 1 100 200 cm /Fm Do /Artifact BMC /Fm Do EMC",
            image,
            form));

        IReadOnlyList<ExtractedImage> defaultImages = document.Pages[0].ExtractImages();
        ExtractedImage visible = Assert.Single(defaultImages);
        Assert.Equal(ExtractedImageFlags.None, visible.Flags);
        // CTM = [10 0 0 20 5 6] * [2 0 0 2 10 20] * [1 0 0 1 100 200] = [20 0 0 40 120 232]
        Assert.Equal(new PdfRectangle(120, 232, 140, 272), visible.Bounds);

        IReadOnlyList<ExtractedImage> withArtifacts = document.Pages[0].ExtractImages(new ImageExtractionOptions().WithArtifacts());
        Assert.Equal(2, withArtifacts.Count);
        Assert.Equal(ExtractedImageFlags.None, withArtifacts[0].Flags);
        Assert.Equal(ExtractedImageFlags.Artifact, withArtifacts[1].Flags);
    }

    [Fact]
    public void ExtractImages_excludes_optional_content_hidden_images_unless_WithHiddenContent_is_enabled()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfOptionalContentGroup hiddenGroup = document.CreateOptionalContentGroup("HiddenLayer", visible: false);

        PdfImage visible = document.CreateImage([0, 255], new PdfImageLayout(2, 1, PdfDeviceGrayColorSpace.Instance, 8), PdfImageCompression.Flate);
        PdfImage hidden = document.CreateImage([255, 0], new PdfImageLayout(2, 1, PdfDeviceGrayColorSpace.Instance, 8), PdfImageCompression.Flate);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(visible, 10, 20, 30, 40);
            canvas.BeginOptionalContent(hiddenGroup);
            canvas.DrawImage(hidden, 50, 60, 70, 80);
            canvas.EndMarkedContent();
        }

        IReadOnlyList<ExtractedImage> defaultImages = document.Pages[0].ExtractImages();
        ExtractedImage onlyVisible = Assert.Single(defaultImages);
        Assert.Equal(new PdfRectangle(10, 20, 40, 60), onlyVisible.Bounds);
        Assert.Equal(ExtractedImageFlags.None, onlyVisible.Flags);

        IReadOnlyList<ExtractedImage> allImages = document.Pages[0].ExtractImages(new ImageExtractionOptions().WithHiddenContent());
        Assert.Equal(2, allImages.Count);
        Assert.Equal(ExtractedImageFlags.None, allImages[0].Flags);
        Assert.Equal(ExtractedImageFlags.Hidden, allImages[1].Flags);
        Assert.Equal(new PdfRectangle(50, 60, 120, 140), allImages[1].Bounds);
    }

    [Fact]
    public void ImageExtractor_runs_alongside_TextExtractor_in_CompositeContentProcessor_and_Clear_resets_it()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfImage image = document.CreateImage([128], new PdfImageLayout(1, 1, PdfDeviceGrayColorSpace.Instance, 8), PdfImageCompression.Flate);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(image, 12, 34, 56, 78);
        }

        var imageExtractor = new ImageExtractor();
        var textExtractor = new Broadside.Text.TextExtractor();
        document.Pages[0].ProcessContent(new CompositeContentProcessor(imageExtractor, textExtractor));

        Assert.Equal(1, imageExtractor.Images.Length);
        Assert.Equal(new PdfRectangle(12, 34, 68, 112), imageExtractor.Images[0].Bounds);

        imageExtractor.Clear();
        Assert.True(imageExtractor.Images.IsEmpty);
    }

    [Fact]
    public void TryGetJpegData_returns_raw_JPEG_bytes_for_DCTDecode_and_false_for_masked_inverted_or_Flate_images()
    {
        using PdfDocument document = PdfDocument.Create();
        byte[] samples = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0];
        var layout = new PdfImageLayout(2, 2, PdfDeviceRgbColorSpace.Instance, 8);

        PdfImage dctImage = document.CreateImage(samples, layout, PdfImageCompression.Dct);
        Assert.True(dctImage.TryGetJpegData(out ReadOnlyMemory<byte> jpeg));
        Assert.True(jpeg.Length > 4 && jpeg.Span[0] == 0xFF && jpeg.Span[1] == 0xD8);
        Assert.Equal(dctImage.Stream!.EncodedData.ToArray(), jpeg.ToArray());

        PdfImage flateImage = document.CreateImage(samples, layout, PdfImageCompression.Flate);
        Assert.False(flateImage.TryGetJpegData(out _));

        PdfImage maskedDct = document.CreateImage(samples, [255, 128, 64, 0], layout, PdfImageCompression.Dct);
        Assert.False(maskedDct.TryGetJpegData(out _));

        // Non-default Decode array on a DCT image cannot pass through raw JPEG bytes.
        PdfImage invertedDct = document.CreateImage([10, 20, 30, 40], new PdfImageLayout(2, 2, PdfDeviceGrayColorSpace.Instance, 8), PdfImageCompression.Dct);
        invertedDct.Dictionary[new CosName("Decode")] = new CosArray([new CosInteger(1), new CosInteger(0)]);
        Assert.False(invertedDct.TryGetJpegData(out _));
    }
}
