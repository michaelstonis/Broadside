using System.Security.Cryptography;
using Broadside.Diagnostics;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// The JPEG 2000 images of the fetched real-world corpora that #67's profile covers decode exactly as OpenJPEG decodes them, and the
/// ones it does not cover decline with <c>JpxUnsupportedFeature</c> (ISO 32000-2 §7.4.9). Goldens are SHA-256 hashes of the §8.9.3
/// samples built from <c>opj_decompress</c> 2.5.4's component output (for issue12213.pdf, whose PDF colour space is Indexed, the
/// codestream's palette indices, checked by mapping them through the file's pclr box to OpenJPEG's output). Skips when the corpora
/// are not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public class RealWorldJpxTests
{
    public static TheoryData<string, string, string, string> Decodable => new()
    {
        { "pdfjs/bug_jpx.pdf", "Im0", "128x64x1@8", "9f1dcbc35c350d6027f98be0f5c8b43b42ca52b7604459c0c42be3aa88913d47" },
        { "pdfjs/issue12213.pdf", "Im0", "684x74x1@4", "eb13096ade467b9a8d02b9e649aa1b5882782d82b556cd9590e9f45cda45580b" },
        { "pdfjs/issue19326.pdf", "Im0", "551x337x1@16", "278c960c2bdf924c11a242461c89e55b0c95c46870455d027b29273839961783" },
        { "pdfjs/issue5567.pdf", "Im0", "1500x1125x3@8", "9b1867ce271f845f2554889155bc72eb8887bcf2198a9fc45cdd334b165675b6" },
        { "pdfjs/jp2k-resetprob.pdf", "Im0", "40x27x3@8", "1afd5fc16ce16dab36523f06c290fc5e9a8506f1eab1ba28f3ac67b898b79ade" },
        { "pdfjs/jpx_smaskindata.pdf", "Im5", "2x1x3@8", "fc22de0cf91e158746409661edf4752b6190dd1ae7133be5436786d584b0c76f" },
        { "pdfjs/jpx_smaskindata.pdf", "Im7", "2x1x4@8", "ffd98cd576a41dabf3db573d4e1d87f14659904a80aef3a520bbe8c128358766" },
        { "verapdf-corpus/PDF_A-2b/6.2 Graphics/6.2.8 Images/6.2.8.3 JPEG2000/veraPDF test suite 6-2-8-3-t01-pass-a.pdf", "Im1", "640x480x3@8", "7b1d653ae545066152e5fcab24d405229cd84de0ff230a448879bbf278ebe03c" },
    };

    public static TheoryData<string> NotYetDecodable => new() { "pdfjs/S2.pdf", "pdfjs/issue5475.pdf", "pdfjs/issue5481.pdf", "pdfjs/issue5549.pdf" };

    [Theory]
    [MemberData(nameof(Decodable))]
    public void A_real_world_JPEG_2000_image_decodes_to_the_samples_OpenJPEG_gives(string file, string name, string shape, string sha256)
    {
        using PdfDocument document = Open(file);

        using DecodedImage decoded = document.Pages[0].GetImage(name)!.Decode()!;

        Assert.Equal(shape, $"{decoded.Width}x{decoded.Height}x{decoded.Components}@{decoded.BitsPerComponent}");
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(decoded.Samples)));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Jpx", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(NotYetDecodable))]
    public void A_real_world_JPEG_2000_image_outside_the_profile_declines_with_information(string file)
    {
        using PdfDocument document = Open(file);

        Assert.Null(document.Pages[0].GetImage("Im0")!.Decode());
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Jpx", StringComparison.Ordinal));
        Assert.Equal(("JpxUnsupportedFeature", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
    }

    private static PdfDocument Open(string file)
    {
        if (RealWorldCorpus.Directory(file.Split('/')[0]) is null || !File.Exists(RealWorldCorpus.PathOf(file)))
        {
            Assert.Skip($"{file} is not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
    }
}
