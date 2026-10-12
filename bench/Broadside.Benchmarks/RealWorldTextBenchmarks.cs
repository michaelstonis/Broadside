using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Benchmarks;

/// <summary>
/// Text extraction over the pinned real-world text subset (issue #184; <c>text-subset.json</c>, the files marked <c>bench</c>):
/// every page's glyphs, words, lines and plain text (ISO 32000-2 §9.4.4, §9.10, §14.8.2.6.2). Files absent from <c>corpus/</c> or
/// whose SHA-256 differs are skipped; with none present (CI's <c>--job dry</c>) the <c>text-lines-rotated.pdf</c> corpus file
/// runs, so the benchmark never fails for want of a corpus.
/// </summary>
/// <remarks>
/// <see cref="ExtractWarm"/>: every page of a document opened in <see cref="Setup"/> and extracted once there (fonts and decoded
/// content cached), into a reused <see cref="TextExtractor"/>: the cost of interpretation, extraction and assembly. <see cref="OpenAndExtract"/>:
/// a fresh document from memory, every page through <see cref="PdfPage.ExtractText()"/> and its <see cref="TextPage.PlainText"/>,
/// the one-call path a caller indexing a file takes. These are the Broadside side of the text baseline that #140 compares with
/// PDFium and pdf.js through RenderDiff's reference plumbing.
/// </remarks>
[MemoryDiagnoser]
public class RealWorldTextBenchmarks
{
    private const string Fallback = "text-lines-rotated.pdf";

    private readonly TextExtractor _extractor = new();
    private byte[] _bytes = [];
    private PdfDocument? _document;

    /// <summary>Gets or sets the file: a path under <c>corpus/</c>, or the fallback under <c>tests/Corpus/</c>.</summary>
    [ParamsSource(nameof(Files))]
    public string File { get; set; } = string.Empty;

    /// <summary>The bench entries of the subset present with their pinned hash, else the fallback.</summary>
    /// <returns>The parameter values.</returns>
    public static IEnumerable<string> Files()
    {
        List<string> present = [.. Pinned().Where(static entry => entry.FullPath is not null).Select(static entry => entry.Path)];
        return present.Count > 0 ? present : [Fallback];
    }

    [GlobalSetup]
    public void Setup()
    {
        string path = File == Fallback ? Path.Combine(CorpusLocator.CorpusDirectory, Fallback) : Pinned().First(entry => entry.Path == File).FullPath!;
        _bytes = System.IO.File.ReadAllBytes(path);
        _document = PdfDocument.Open(_bytes);
        ExtractWarm();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark(Baseline = true)]
    public long ExtractWarm()
    {
        long units = 0;
        PdfDocument document = _document!;
        for (int index = 0; index < document.Pages.Count; index++)
        {
            _extractor.Clear();
            document.Pages[index].ProcessContent(_extractor);
            units += _extractor.Words.Length + _extractor.Lines.Length + _extractor.PlainText.Length;
        }

        return units;
    }

    [Benchmark]
    public long OpenAndExtract()
    {
        long units = 0;
        using PdfDocument document = PdfDocument.Open(_bytes);
        foreach (PdfPage page in document.Pages)
        {
            units += page.ExtractText().PlainText.Length;
        }

        return units;
    }

    /// <summary>The <c>bench</c> entries of <c>text-subset.json</c>; <c>FullPath</c> is null when the file is absent or not the pinned one.</summary>
    private static List<(string Path, string? FullPath)> Pinned()
    {
        string json = System.IO.File.ReadAllText(Path.Combine(CorpusLocator.RepositoryRoot, "bench", "Broadside.Benchmarks", "text-subset.json"));
        using JsonDocument document = JsonDocument.Parse(json);
        var entries = new List<(string Path, string? FullPath)>();
        foreach (JsonElement file in document.RootElement.GetProperty("files").EnumerateArray())
        {
            if (!file.GetProperty("bench").GetBoolean())
            {
                continue;
            }

            string path = file.GetProperty("path").GetString()!;
            string? full = CorpusLocator.RealWorldCorpusDirectory is { } root ? Path.Combine(root, path) : null;
            bool pinned = full is not null && System.IO.File.Exists(full) && Sha256(full) == file.GetProperty("sha256").GetString();
            entries.Add((path, pinned ? full : null));
        }

        return entries;
    }

    private static string Sha256(string path)
    {
        using FileStream stream = System.IO.File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
