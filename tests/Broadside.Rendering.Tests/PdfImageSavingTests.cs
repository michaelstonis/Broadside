using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;
using StbImageSharp;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Rendering <see cref="PdfImage"/> and <see cref="ExtractedImage"/> to <see cref="Bitmap"/>, and saving them as PNG or JPEG files
/// through <see cref="PdfImageRenderingExtensions"/> and <see cref="Bitmap.SaveJpeg(Stream)"/>.
/// ISO 32000-2 §7.4.8, §8.6.5.6, §8.9, §11.6.5.2.
/// </summary>
public sealed class PdfImageSavingTests
{
    [Fact]
    public void ToBitmap_and_SavePng_render_every_bit_depth_Indexed_palette_and_inverted_Decode_array()
    {
        using (PdfDocument doc2 = PdfDocument.Open(Corpus.Path("image-2bpc.pdf")))
        {
            PdfImage image = doc2.Pages[0].GetImage("Im0")!;
            using Bitmap bitmap = image.ToBitmap()!;
            Assert.Equal((5, 2), (bitmap.Width, bitmap.Height));
            Assert.Equal(new Rgba(0, 0, 0, 255), bitmap.At(0, 0));
            Assert.Equal(new Rgba(85, 85, 85, 255), bitmap.At(1, 0));
            Assert.Equal(new Rgba(170, 170, 170, 255), bitmap.At(2, 0));
            Assert.Equal(new Rgba(255, 255, 255, 255), bitmap.At(3, 0));
        }

        using (PdfDocument docIndexed = PdfDocument.Open(Corpus.Path("image-4bpc-indexed.pdf")))
        {
            ExtractedImage extracted = Assert.Single(docIndexed.Pages[0].ExtractImages());
            using var pngStream = new MemoryStream();
            Assert.True(extracted.SavePng(pngStream));
            pngStream.Position = 0;

            ImageResult decoded = PngSnapshots.Decode(pngStream);
            Assert.Equal((3, 2), (decoded.Width, decoded.Height));
            Assert.Equal((0, 255, 0, 255), (decoded.Data[0], decoded.Data[1], decoded.Data[2], decoded.Data[3]));
            Assert.Equal((85, 170, 255, 255), (decoded.Data[4], decoded.Data[5], decoded.Data[6], decoded.Data[7]));
        }

        using (PdfDocument docInverted = PdfDocument.Open(Corpus.Path("image-decode-inverted.pdf")))
        {
            PdfImage image = docInverted.Pages[0].GetImage("Im0")!;
            using Bitmap bitmap = image.ToBitmap()!;
            Assert.Equal(new Rgba(255, 255, 255, 255), bitmap.At(0, 0));
            Assert.Equal(new Rgba(170, 170, 170, 255), bitmap.At(1, 0));
            Assert.Equal(new Rgba(85, 85, 85, 255), bitmap.At(2, 0));
            Assert.Equal(new Rgba(0, 0, 0, 255), bitmap.At(3, 0));
        }
    }

    [Fact]
    public void An_extracted_image_mask_paints_the_fill_colour_and_leaves_unpainted_samples_transparent()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-stencil-mask.pdf"));
        ExtractedImage extracted = Assert.Single(document.Pages[0].ExtractImages());

        // ExtractedImage uses the page's red fill colour (1, 0, 0); standalone PdfImage.ToBitmap() defaults to black.
        using Bitmap redBitmap = extracted.ToBitmap()!;
        Assert.Equal((8, 8), (redBitmap.Width, redBitmap.Height));
        Assert.Equal(new Rgba(0, 0, 0, 0), redBitmap.At(0, 0));
        Assert.Equal(new Rgba(255, 0, 0, 255), redBitmap.At(3, 0));

        using Bitmap blackBitmap = extracted.Image.ToBitmap()!;
        Assert.Equal(new Rgba(0, 0, 0, 0), blackBitmap.At(0, 0));
        Assert.Equal(new Rgba(0, 0, 0, 255), blackBitmap.At(3, 0));

        using var pngStream = new MemoryStream();
        Assert.True(extracted.SavePng(pngStream));
        pngStream.Position = 0;
        ImageResult png = PngSnapshots.Decode(pngStream);
        Assert.Equal(0, png.Data[3]); // (0, 0) alpha is 0
        int arrowTip = 3 * 4;
        Assert.Equal((255, 0, 0, 255), (png.Data[arrowTip], png.Data[arrowTip + 1], png.Data[arrowTip + 2], png.Data[arrowTip + 3]));
    }

    [Fact]
    public void Explicit_masks_colour_keys_and_soft_masks_with_Matte_fold_into_the_saved_PNG_alpha()
    {
        // Colour key mask: sample (0, 0) is (255, 0, 0), inside [250 255 0 5 0 5] -> alpha 0; sample (2, 0) is (249, 0, 0) -> opaque.
        using (PdfDocument docKey = PdfDocument.Open(Corpus.Path("image-color-key-mask.pdf")))
        {
            PdfImage image = docKey.Pages[0].GetImage("Im0")!;
            using Bitmap bitmap = image.ToBitmap()!;
            Assert.Equal(new Rgba(0, 0, 0, 0), bitmap.At(0, 0));
            Assert.Equal(new Rgba(249, 0, 0, 255), bitmap.At(2, 0));
        }

        // Soft mask with Matte: a 2 x 1 RGB image pre-blended against white Matte [1 1 1] with alpha [128, 255].
        // Pre-blended red (255, 0, 0) at alpha 128/255 against white (255, 255, 255) is (255, 127, 127).
        // Un-matting removes the white halo so premultiplied BGRA has G = 0, B = 0, R = 128, A = 128 (straight PNG pixel: 255, 0, 0, 128).
        using (PdfDocument docSoft = PdfDocument.Create())
        {
            PdfImage image = docSoft.CreateImage(
                [255, 127, 127, 0, 255, 0],
                [128, 255],
                new PdfImageLayout(2, 1, PdfDeviceRgbColorSpace.Instance, 8),
                PdfImageCompression.Flate);
            CosStream smask = (CosStream)docSoft.Resolve(image.Dictionary[new CosName("SMask")]);
            smask.Dictionary[new CosName("Matte")] = new CosArray([new CosInteger(1), new CosInteger(1), new CosInteger(1)]);

            using var pngStream = new MemoryStream();
            Assert.True(image.SavePng(pngStream));
            pngStream.Position = 0;
            ImageResult decoded = PngSnapshots.Decode(pngStream);
            Assert.Equal(255, decoded.Data[0]);
            Assert.InRange(decoded.Data[1], 0, 1);
            Assert.InRange(decoded.Data[2], 0, 1);
            Assert.Equal(128, decoded.Data[3]);
            Assert.Equal((0, 255, 0, 255), (decoded.Data[4], decoded.Data[5], decoded.Data[6], decoded.Data[7]));
        }
    }

    [Fact]
    public void SaveJpeg_passes_through_a_DCTDecode_stream_unchanged_and_reencodes_when_quality_or_Flate_is_used()
    {
        using PdfDocument document = PdfDocument.Create();
        byte[] samples = new byte[8 * 8 * 3];
        for (int i = 0; i < samples.Length; i += 3)
        {
            samples[i] = 220;
            samples[i + 1] = 40;
            samples[i + 2] = 30;
        }

        var layout = new PdfImageLayout(8, 8, PdfDeviceRgbColorSpace.Instance, 8);
        PdfImage dctImage = document.CreateImage(samples, layout, PdfImageCompression.Dct);
        PdfImage flateImage = document.CreateImage(samples, layout, PdfImageCompression.Flate);

        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(dctImage, 10, 10, 20, 20);
            canvas.DrawImage(flateImage, 40, 10, 20, 20);
        }

        IReadOnlyList<ExtractedImage> extracted = document.Pages[0].ExtractImages();
        Assert.Equal(2, extracted.Count);

        // 1. Default SaveJpeg on DCTDecode passes through the exact stream bytes.
        using var passthrough = new MemoryStream();
        Assert.True(extracted[0].SaveJpeg(passthrough));
        Assert.Equal(dctImage.Stream!.EncodedData.ToArray(), passthrough.ToArray());

        // 2. Explicit quality on DCTDecode re-encodes at that quality.
        using var highQuality = new MemoryStream();
        Assert.True(extracted[0].SaveJpeg(highQuality, quality: 95));
        ImageResult decodedHigh = ImageResult.FromMemory(highQuality.ToArray(), ColorComponents.RedGreenBlue);
        Assert.Equal((8, 8), (decodedHigh.Width, decodedHigh.Height));
        Assert.InRange(decodedHigh.Data[0], 210, 230);

        // 3. SaveJpeg on FlateDecode encodes a valid JPEG.
        using var fromFlate = new MemoryStream();
        Assert.True(extracted[1].SaveJpeg(fromFlate));
        ImageResult decodedFlate = ImageResult.FromMemory(fromFlate.ToArray(), ColorComponents.RedGreenBlue);
        Assert.Equal((8, 8), (decodedFlate.Width, decodedFlate.Height));
        Assert.InRange(decodedFlate.Data[0], 210, 230);
        Assert.InRange(decodedFlate.Data[1], 30, 50);
    }

    [Fact]
    public void SavePng_and_SaveJpeg_to_file_paths_write_files_and_create_no_file_when_undecodable()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"broadside-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using PdfDocument document = PdfDocument.Create();
            PdfImage image = document.CreateImage(
                [10, 200, 30, 240, 50, 60],
                new PdfImageLayout(2, 1, PdfDeviceRgbColorSpace.Instance, 8),
                PdfImageCompression.Flate);

            string pngPath = Path.Combine(directory, "image.png");
            string jpgPath = Path.Combine(directory, "image.jpg");
            Assert.True(image.SavePng(pngPath));
            Assert.True(image.SaveJpeg(jpgPath, quality: 90));
            Assert.True(File.Exists(pngPath));
            Assert.True(File.Exists(jpgPath));

            ImageResult png = ImageResult.FromMemory(File.ReadAllBytes(pngPath), ColorComponents.RedGreenBlueAlpha);
            Assert.Equal((2, 1), (png.Width, png.Height));
            Assert.Equal((10, 200, 30, 255), (png.Data[0], png.Data[1], png.Data[2], png.Data[3]));

            ImageResult jpg = ImageResult.FromMemory(File.ReadAllBytes(jpgPath), ColorComponents.RedGreenBlue);
            Assert.Equal((2, 1), (jpg.Width, jpg.Height));

            // Damage the image dictionary so Width is 0 (undecodable): SavePng and SaveJpeg return false and create no file.
            image.Dictionary[new CosName("Width")] = new CosInteger(0);
            string failedPng = Path.Combine(directory, "failed.png");
            string failedJpg = Path.Combine(directory, "failed.jpg");
            Assert.False(image.SavePng(failedPng));
            Assert.False(image.SaveJpeg(failedJpg));
            Assert.False(File.Exists(failedPng));
            Assert.False(File.Exists(failedJpg));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
