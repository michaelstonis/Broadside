using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// JBIG2 images through <see cref="PdfImage.Decode"/> and <see cref="PdfDocument.DecodeStream(CosStream)"/>: the default filter,
/// JBIG2Globals resolved through the document, PDF polarity for gray images and image masks. ISO 32000-2 §7.4.7, §8.9.5.2, §8.9.6.2.
/// </summary>
public class Jbig2ImageTests
{
    [Theory]
    [InlineData("jbig2-generic.pdf", false)]
    [InlineData("jbig2-generic-mmr.pdf", true)]
    public void A_corpus_JBIG2_image_decodes_to_the_bitmap_it_was_generated_from(string file, bool stencil)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file));
        PdfImage image = document.Pages[0].GetImage("Im0")!;
        byte[] expected = Jbig2Encoder.PackPdf(CcittEncoder.SampleBitmap(150, 48), 150);

        using DecodedImage decoded = image.Decode()!;
        byte[] bytes = document.DecodeStream(image.Stream!).ToArray();

        Assert.Equal((150, 48, 1, 1, false), (decoded.Width, decoded.Height, decoded.Components, decoded.BitsPerComponent, decoded.SamplesInverted));
        Assert.Equal(expected, decoded.Samples.ToArray());
        Assert.Equal(expected, bytes);
        Assert.Equal(stencil, image.IsStencil);
        Assert.False(image.CreateDecodeMap(decoded).IsInverted);
        Assert.Empty(document.Diagnostics);
    }
}
