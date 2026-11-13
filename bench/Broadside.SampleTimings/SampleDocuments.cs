using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The representative sample documents of #363 (samples/documents/pdf, described in samples/documents/MANIFEST.md) for the
/// realistic benchmark tier and the per-page timings. Shared as source by bench/Broadside.Benchmarks.Realistic and
/// bench/Broadside.SampleTimings.
/// </summary>
internal static class SampleDocuments
{
    /// <summary>
    /// The heaviest page of each document (zero-based), the slowest to render in docs/benchmarks/sample-documents-baseline.md; the
    /// realistic tier benchmarks it next to page 0. Documents not listed have one representative page only.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> HeavyPages = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["brochure.pdf"] = 2,
        ["catalogue.pdf"] = 9,
        ["dictionary.pdf"] = 7,
        ["newsletter.pdf"] = 3,
        ["novel-hardback.pdf"] = 124,
        ["paper.pdf"] = 1,
        ["textbook.pdf"] = 6,
    };

    /// <summary>
    /// The engine every sample-document measurement opens with, the same as the rendering gate's: Standard 14 fonts and predefined
    /// CMaps bundled, no system fonts (the same glyphs on every OS), and no display-list cache, so a warm render interprets the page.
    /// </summary>
    public static PdfEngine Engine { get; } = new(new PdfOptions()
        .UseStandard14Fonts()
        .UsePredefinedCMaps()
        .UseSystemFontResolver(null)
        .WithDisplayListCacheBudget(0));

    /// <summary>
    /// Opens a sample with <see cref="Engine"/>, with the test-only credentials the encrypted samples need (samples/documents/MANIFEST.md):
    /// the user password of the AES-256 invoice and the client's test certificate for the certificate-encrypted one.
    /// </summary>
    /// <param name="file">The sample's file name.</param>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>The open document.</returns>
    public static PdfDocument Open(string file, byte[] bytes) =>
        Credentials(file) is { } credentials ? Engine.Open(bytes, credentials) : Engine.Open(bytes);

    private static PdfCredentials? Credentials(string file) => file switch
    {
        "invoice-conservative-aes256.pdf" => new PdfPassword("broadside-user"),
        "invoice-colourful-certificate.pdf" => new PdfCertificateCredentials(X509CertificateLoader.LoadPkcs12FromFile(
            Path.Combine(CorpusLocator.RepositoryRoot, "samples", "documents", "contract", "test-pki", "client.p12"), "broadside-test")),
        _ => null,
    };

    /// <summary>Gets the folder of the sample PDFs.</summary>
    public static string Directory => Path.Combine(CorpusLocator.RepositoryRoot, "samples", "documents", "pdf");

    /// <summary>Every sample PDF, by file name.</summary>
    /// <returns>The file names, sorted.</returns>
    public static IEnumerable<string> Files() =>
        System.IO.Directory.GetFiles(Directory, "*.pdf").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal);

    /// <summary>The benchmark pages: page 0 of every document and its heavy page, written <c>file.pdf#page</c>.</summary>
    /// <returns>The parameter values.</returns>
    public static IEnumerable<string> BenchmarkPages()
    {
        foreach (string file in Files())
        {
            yield return $"{file}#0";
            if (HeavyPages.TryGetValue(file, out int heavy) && heavy != 0)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"{file}#{heavy}");
            }
        }
    }

    /// <summary>Reads the file of a parameter value and returns its bytes and zero-based page.</summary>
    /// <param name="value">A value from <see cref="BenchmarkPages"/>.</param>
    /// <returns>The file's name and bytes and the page index.</returns>
    public static (string File, byte[] Bytes, int Page) Read(string value)
    {
        int hash = value.LastIndexOf('#');
        return (value[..hash], File.ReadAllBytes(Path.Combine(Directory, value[..hash])), int.Parse(value[(hash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture));
    }
}
