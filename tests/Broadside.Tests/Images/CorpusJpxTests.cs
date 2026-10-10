using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// The JPEG 2000 corpus file read through the document API with the default filters: JPXDecode is registered by default and
/// decodes <c>jpx-lossless.pdf</c> bit-exactly (ISO 32000-2 §7.4.9). Expected samples are the generator's source image, which
/// OpenJPEG and poppler both return from the file (tests/Corpus/README.md).
/// </summary>
public sealed class CorpusJpxTests
{
    [Fact]
    public void A_lossless_JPEG_2000_image_decodes_to_its_source_samples_through_the_default_filters()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("jpx-lossless.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        using DecodedImage decoded = image.Decode()!;

        Assert.Equal((17, 13, 3, 8), (decoded.Width, decoded.Height, decoded.Components, decoded.BitsPerComponent));
        Assert.Equal(Source(17, 13), decoded.Samples.ToArray());
        Assert.Same(PdfDeviceRgbColorSpace.Instance, image.ColorSpace);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_stream_decodes_to_the_same_samples_as_bytes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("jpx-lossless.pdf"));
        var stream = (CosStream)document.Resolve(new CosReference(5, 0));

        byte[] decoded = document.DecodeStream(stream).ToArray();

        Assert.Equal(Source(17, 13), decoded);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void With_SMaskInData_1_the_opacity_channel_of_the_corpus_file_becomes_the_alpha_plane()
    {
        // jpx-smask-in-data.pdf: four components (R, G, B, opacity) with a cdef box marking the fourth as opacity (Typ 1) for the
        // whole image, no ColorSpace and SMaskInData 1 (ISO 32000-2 §8.9.5.1 Table 87, §11.6.5.3; ITU-T T.800 I.5.3.6).
        using PdfDocument document = PdfDocument.Open(Corpus.Path("jpx-smask-in-data.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        using DecodedImage decoded = image.Decode()!;

        Assert.Equal((PdfImageMaskKind.SoftInData, 1), (image.MaskKind, image.SoftMaskInData));
        Assert.Equal((15, 11, 3, 8), (decoded.Width, decoded.Height, decoded.Components, decoded.BitsPerComponent));
        Assert.Equal(ImageColorModel.Rgb, decoded.ColorModel);
        Assert.Equal(Source(15, 11), decoded.Samples.ToArray());
        DecodedImage alpha = Assert.IsType<DecodedImage>(decoded.Alpha);
        Assert.False(decoded.AlphaPremultiplied);
        Assert.Equal((15, 11, 1, 8), (alpha.Width, alpha.Height, alpha.Components, alpha.BitsPerComponent));
        Assert.Equal(
            Enumerable.Range(0, 11).SelectMany(y => Enumerable.Range(0, 15).Select(x => (byte)JpxSamples.Sample(x, y, 3, 8))),
            alpha.Samples.ToArray());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Sub_sampled_components_are_replicated_over_the_image_grid()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("jpx-subsampled.pdf"));

        using DecodedImage decoded = document.Pages[0].GetImage("Im0")!.Decode()!;

        // T.800 G.4 and B.2: image sample (x, y) of component c lies over component sample (x / XRsiz, y / YRsiz).
        byte[] expected = new byte[21 * 15 * 3];
        for (int y = 0; y < 15; y++)
        {
            for (int x = 0; x < 21; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int separation = c == 0 ? 1 : 2;
                    expected[(((y * 21) + x) * 3) + c] = (byte)JpxSamples.Sample(x / separation, y / separation, c, 8);
                }
            }
        }

        Assert.Equal((21, 15, 3, 8), (decoded.Width, decoded.Height, decoded.Components, decoded.BitsPerComponent));
        Assert.Equal(expected, decoded.Samples.ToArray());
        Assert.Empty(document.Diagnostics);
    }

    private static byte[] Source(int width, int height)
    {
        byte[] samples = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    samples[(((y * width) + x) * 3) + c] = (byte)JpxSamples.Sample(x, y, c, 8);
                }
            }
        }

        return samples;
    }
}
