using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// The pinned real-world text subset (<c>bench/Broadside.Benchmarks/text-subset.json</c>), snapshotted: for each file its page,
/// glyph, word and line counts and the SHA-256 of its plain text (pages separated by a form feed), so a change to extraction,
/// word or line assembly shows up as a reviewed snapshot diff. ISO 32000-2 §9.4.4, §9.10, §14.8.2.6.2. Skips when the corpora are
/// not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldTextSubsetTests
{
    [Fact]
    public async Task Every_file_of_the_pinned_subset_extracts_the_snapshotted_counts_and_plain_text()
    {
        if (RealWorldCorpus.Directory("pdfjs") is null)
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var output = new StringBuilder();
        var extractor = new TextExtractor();
        foreach ((string path, string sha256) in Subset())
        {
            string full = RealWorldCorpus.PathOf(path);
            if (!File.Exists(full))
            {
                output.Append(CultureInfo.InvariantCulture, $"{path}: absent\n");
                continue;
            }

            byte[] bytes = await File.ReadAllBytesAsync(full, TestContext.Current.CancellationToken);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != sha256)
            {
                output.Append(CultureInfo.InvariantCulture, $"{path}: SHA-256 differs from the pin; re-fetch the corpus\n");
                continue;
            }

            using PdfDocument document = PdfDocument.Open(bytes);
            long glyphs = 0;
            long words = 0;
            long lines = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int index = 0; index < document.Pages.Count; index++)
            {
                extractor.Clear();
                document.Pages[index].ProcessContent(extractor);
                glyphs += extractor.Glyphs.Length;
                words += extractor.Words.Length;
                lines += extractor.Lines.Length;
                hash.AppendData(Encoding.UTF8.GetBytes(extractor.PlainText.ToString() + "\f"));
            }

            output.Append(CultureInfo.InvariantCulture, $"{path}: pages {document.Pages.Count}, glyphs {glyphs}, words {words}, lines {lines}, plain text {Convert.ToHexStringLower(hash.GetHashAndReset())}\n");
        }

        await Verify(output.ToString());
    }

    private static List<(string Path, string Sha256)> Subset()
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Corpus.Directory, "..", "..", "bench", "Broadside.Benchmarks", "text-subset.json")));
        return
        [
            .. json.RootElement.GetProperty("files").EnumerateArray()
                .Select(static file => (file.GetProperty("path").GetString()!, file.GetProperty("sha256").GetString()!)),
        ];
    }
}
