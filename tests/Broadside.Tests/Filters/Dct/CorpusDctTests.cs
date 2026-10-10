using Broadside.Images;
using Broadside.TestSupport;
using static Broadside.Tests.Filters.Dct.DctVectors;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// <c>tests/Corpus/dct-*.pdf</c> through the document's public image API: every JPEG image XObject (baseline, progressive, CMYK
/// and Adobe-inverted YCCK) decodes within ±1 of libjpeg-turbo's decode of the same stream (ISO 32000-2 §7.4.8, §8.9); the
/// inverted one's Decode array, not the codec, undoes the inversion (§8.9.5.2).
/// </summary>
public class CorpusDctTests
{
    [Theory]
    [InlineData("dct-baseline.pdf", "Im0", "dct-baseline-color", ImageColorModel.Rgb)]
    [InlineData("dct-baseline.pdf", "Im1", "dct-baseline-gray", ImageColorModel.Gray)]
    [InlineData("dct-progressive.pdf", "Im0", "dct-progressive", ImageColorModel.Rgb)]
    [InlineData("dct-cmyk.pdf", "Im0", "dct-cmyk-cmyk", ImageColorModel.Cmyk)]
    [InlineData("dct-cmyk.pdf", "Im1", "dct-cmyk-ycck", ImageColorModel.Cmyk)]
    public void The_JPEG_images_of_the_corpus_decode_within_one_of_the_reference(string file, string name, string golden, ImageColorModel model)
    {
        using PdfDocument document = PdfDocument.Open(Path.Combine(Corpus.Directory, file), new PdfOptions().UseStrict());
        PdfImage image = document.Pages[0].GetImage(name)!;

        using DecodedImage decoded = image.Decode()!;

        DctGolden reference = Golden(golden);
        Assert.Equal((reference.Width, reference.Height, reference.Components), (decoded.Width, decoded.Height, decoded.Components));
        AssertWithinOne(reference, decoded.Samples);
        Assert.Equal((model, decoded.Height), (decoded.ColorModel, decoded.DecodedRows));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_inverted_YCCK_image_of_dct_cmyk_pdf_maps_through_its_Decode_array_to_the_ink_of_the_plain_CMYK_image()
    {
        using PdfDocument document = PdfDocument.Open(Path.Combine(Corpus.Directory, "dct-cmyk.pdf"), new PdfOptions().UseStrict());
        PdfImage plain = document.Pages[0].GetImage("Im0")!;
        PdfImage inverted = document.Pages[0].GetImage("Im1")!;
        using DecodedImage plainSamples = plain.Decode()!;
        using DecodedImage invertedSamples = inverted.Decode()!;
        float[] plainInk = new float[plainSamples.Samples.Length];
        float[] invertedInk = new float[invertedSamples.Samples.Length];

        plain.CreateDecodeMap(plainSamples).Map(plainSamples.Samples, plainInk);
        inverted.CreateDecodeMap(invertedSamples).Map(invertedSamples.Samples, invertedInk);

        // Same picture, lossy coding in different colour spaces and samplings: the mean ink difference is small.
        Assert.Equal((false, true), (plain.CreateDecodeMap(plainSamples).IsInverted, inverted.CreateDecodeMap(invertedSamples).IsInverted));
        double difference = plainInk.Zip(invertedInk, (a, b) => Math.Abs(a - b)).Average();
        Assert.InRange(difference, 0, 0.05);
    }
}
