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
public class CorpusJpxTests
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
