using System.Security.Cryptography;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The fixed real-world subset of the open-and-walk baseline (issue #48): files from the corpora <c>tools/CorpusFetcher</c> fetches
/// (pinned to commits in <c>tools/CorpusFetcher/corpora.json</c>), each pinned again here by SHA-256 so that a baseline number
/// always means the same bytes. Picked to cover what the minimal corpus does not: PDF 2.0 files from the PDF Association, real
/// linearized and incrementally updated files, RC4 and AES-256 encryption, a few hundred to a thousand objects, dozens of pages,
/// a megabyte of image data. Every file opens with no Warning or Error diagnostic.
/// </summary>
internal static class RealWorldSubset
{
    /// <summary>Path under the real-world corpus directory, and SHA-256 of the file.</summary>
    private static readonly (string Path, string Sha256)[] Files =
    [
        ("pdf20examples/Simple PDF 2.0 file.pdf", "296d2a0b2ce19b606f29265694f194a754fbc61783982b5b8d730e8637482236"),
        ("pdf20examples/PDF 2.0 via incremental save.pdf", "9b25d59d07034634a990e79db6ef82b9f86769d9b94d8d8635f20d06aaec9d18"),
        ("pdf20examples/pdf20-utf8-test.pdf", "a7ca717ef062df0a65e58487d26358c81424b76084a2000ca1c74b9e5776b7a1"),
        ("qpdf/minimal-linearized.pdf", "75b49e60adac5dd53c07e01c20f488e86c81a12f402c3b1d5b2a5d3e1a7ca883"),
        ("qpdf/lin9.pdf", "fe7ccf236f9710b581d227508dcdb41c023b795532d5715be4fbae14f033341f"),
        ("qpdf/merge-implicit-ranges.pdf", "a0a47d34fe90cdae65c522574c1890d65b199304a50968dc36cf443679a556d5"),
        ("qpdf/deterministic-id-in.pdf", "336eaa8e96c36348358c17faf294469035767f3c8294f3020ac090ed9a1eec97"),
        ("qpdf/large-inline-image.pdf", "7da28b9cb83805404c4c5e8a99184b062175f017a2fb40a2bae08aa3f9763d95"),
        ("pdfjs/issue7665.pdf", "cd1d57895ba287085780a752be15293983b5d3733e2d62428e6083331ca336ec"),
        ("pdfjs/bug900822.pdf", "76ff1ee293cd3a447b737f882840af70e50627f3010170fe3c1581090be8fd61"),
        ("pdfjs/160F-2019.pdf", "f41f0818365ffd14ca4e55c0556355193bd76632b970d60cc2dec554cac6f379"),
        ("pdfjs/issue17808.pdf", "2b082b535fbabae9e066df04d2d68b984783e17559a1fe70258250078f3cf0fa"),
        ("pdfjs/issue18911.pdf", "c62ce23251433be44fe4097cf328554cb436df1b6e78f24d926c4c42f70679b2"),
        ("pdfbox/input/rendering/survey.pdf", "2e8b87ea72de7dd54176354706a5d7ff9e64a12624e603ffac64aa55743bf581"),
    ];

    /// <summary>The paths of the subset files present on this machine; none when the corpora are not fetched (CI's dry run).</summary>
    public static IEnumerable<string> Available()
    {
        string? directory = CorpusLocator.RealWorldCorpusDirectory;
        return directory is null
            ? []
            : Files.Where(file => File.Exists(System.IO.Path.Combine(directory, file.Path))).Select(file => file.Path);
    }

    /// <summary>Reads one subset file and checks it is the pinned one.</summary>
    /// <exception cref="InvalidDataException">The file's SHA-256 is not the pinned one: the baseline would not be comparable.</exception>
    public static byte[] Read(string path)
    {
        string expected = Files.Single(file => file.Path == path).Sha256;
        byte[] bytes = File.ReadAllBytes(System.IO.Path.Combine(CorpusLocator.RealWorldCorpusDirectory!, path));
        string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return actual == expected
            ? bytes
            : throw new InvalidDataException($"{path} has SHA-256 {actual}; the benchmark baseline pins {expected}. Re-fetch the corpus.");
    }
}
