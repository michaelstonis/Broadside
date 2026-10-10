using Broadside.Images;
using Broadside.TestSupport;
using static Broadside.Tests.Filters.Dct.DctVectors;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// <c>tests/Corpus/dct-baseline.pdf</c> through the document's public image API: both JPEG image XObjects decode within ±1 of
/// libjpeg-turbo's decode of the same streams (ISO 32000-2 §7.4.8, §8.9).
/// </summary>
public class CorpusDctTests
{
    [Theory]
    [InlineData("Im0", "dct-baseline-color", ImageColorModel.Rgb)]
    [InlineData("Im1", "dct-baseline-gray", ImageColorModel.Gray)]
    public void The_images_of_dct_baseline_pdf_decode_within_one_of_the_reference(string name, string golden, ImageColorModel model)
    {
        using PdfDocument document = PdfDocument.Open(Path.Combine(Corpus.Directory, "dct-baseline.pdf"), new PdfOptions().UseStrict());
        PdfImage image = document.Pages[0].GetImage(name)!;

        using DecodedImage decoded = image.Decode()!;

        DctGolden reference = Golden(golden);
        Assert.Equal((reference.Width, reference.Height, reference.Components), (decoded.Width, decoded.Height, decoded.Components));
        AssertWithinOne(reference, decoded.Samples);
        Assert.Equal((model, decoded.Height), (decoded.ColorModel, decoded.DecodedRows));
        Assert.Empty(document.Diagnostics);
    }
}
