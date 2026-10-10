using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// Real-world JBIG2 images built only from generic regions (pdf.js's test files, from the SerenityOS JBIG2 suite) decode bit for bit
/// to their reference through <see cref="PdfImage.Decode"/> and <see cref="PdfDocument.DecodeStream(CosStream)"/>. Skips when the
/// corpora are not fetched. ISO 32000-2 §7.4.7; ITU-T T.88 §6.2, §7, §8, Annex D.
/// </summary>
/// <remarks>
/// Every <c>bitmap-*</c> file encodes the same 399 x 400 picture, SerenityOS's <c>Tests/LibGfx/test-inputs/bmp/bitmap.bmp</c>; its
/// packed raster (1 = black, padding 0) has the MD5 cf9fa4d02110fdaead640de90c2f57aa, which jbig2dec 0.20 reproduces for each file
/// (<c>pdfimages -all</c>, then <c>jbig2dec -e -o out.pbm globals page</c>). The hashes below are SHA-256s of the PDF samples (that
/// raster inverted, padding 0). <c>jbig2_file_header.pdf</c> was decoded with jbig2dec in its file-header mode.
/// </remarks>
[Trait("Category", "Corpus")]
public class RealWorldJbig2Tests
{
    private const string Golden = "68729cd515c668992c596df83a7437956a7f5a9df0d603e26b43aa3fc4a88f28";

    public static TheoryData<string, int, string, string[]> GenericOnly => new()
    {
        { "pdfjs/bitmap-customat.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-customat-tpgdon.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-tpgdon.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-mmr.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-p32-eof.pdf", 6, Golden, [] },
        { "pdfjs/bitmap-randomaccess.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-initially-unknown-size.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-stripe.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-stripe-initially-unknown-height.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-stripe-last-implicit.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-stripe-single.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-stripe-single-no-end-of-stripe.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-trailing-7fff-stripped.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-trailing-7fff-stripped-harder.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-composite-and-xnor.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-composite-or-xor-replace.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template1.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template1-customat.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template1-tpgdon.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template1-customat-tpgdon.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template2.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template2-customat.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template2-tpgdon.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template2-customat-tpgdon.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template3.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template3-customat.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template3-tpgdon.pdf", 5, Golden, [] },
        { "pdfjs/bitmap-template3-customat-tpgdon.pdf", 5, Golden, [] },
        { "pdfjs/jbig2_file_header.pdf", 4, "e774b45e142bc9dd40db2cee035428ce009133e6b2bcb50e9d49185a2f0872dc", ["Jbig2FileHeaderPresent", "Jbig2EndOfPagePresent"] },
    };

    [Theory]
    [MemberData(nameof(GenericOnly))]
    public void A_generic_region_image_decodes_to_its_reference(string file, int objectNumber, string sha256, string[] codes)
    {
        string corpus = file[..file.IndexOf('/', StringComparison.Ordinal)];
        if (!RealWorldCorpus.Files(corpus).Contains(file))
        {
            Assert.Skip($"{file} not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        using PdfDocument document = PdfDocument.Open(RealWorldCorpus.PathOf(file));
        CosObject stream = document.Resolve(new CosReference(objectNumber, 0));
        PdfImage image = document.GetImage(stream)!;

        using DecodedImage decoded = image.Decode()!;
        byte[] bytes = document.DecodeStream((CosStream)stream).ToArray();

        Assert.Equal((1, 1, false), (decoded.Components, decoded.BitsPerComponent, decoded.SamplesInverted));
        Assert.Equal(sha256, Jbig2Sha256(decoded.Samples));
        Assert.Equal(sha256, Jbig2Sha256(bytes));
        Assert.Equal(codes, document.Diagnostics.Select(diagnostic => diagnostic.Code).Where(code => code.StartsWith("Jbig2", StringComparison.Ordinal)).Distinct());
    }

    public static TheoryData<string, int> NotYetDecodable => new()
    {
        // Symbol dictionaries with text regions, halftone regions, refinement regions (issue #65).
        { "pdfjs/bitmap-symbol.pdf", 5 },
        { "pdfjs/bitmap-halftone.pdf", 5 },
        { "pdfjs/bitmap-refine.pdf", 5 },
        { "pdfjs/issue17871_top_right.pdf", 5 },
    };

    [Theory]
    [MemberData(nameof(NotYetDecodable))]
    public void An_image_with_regions_not_decoded_yet_declines_with_information(string file, int objectNumber)
    {
        string corpus = file[..file.IndexOf('/', StringComparison.Ordinal)];
        if (!RealWorldCorpus.Files(corpus).Contains(file))
        {
            Assert.Skip($"{file} not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        using PdfDocument document = PdfDocument.Open(RealWorldCorpus.PathOf(file));
        PdfImage image = document.GetImage(document.Resolve(new CosReference(objectNumber, 0)))!;

        Assert.Null(image.Decode());
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "Jbig2UnsupportedFeature");
    }

    private static string Jbig2Sha256(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data));
}
