using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Content interpretation over a pinned real-world subset (issue #80, the Phase 2 baseline; <c>content-subset.json</c>, picked by
/// measurement from the content corpus gate's per-file detail): text-, vector-, image- and inline-image-heavy pages, CJK text, the
/// largest page count, shadings and tiling patterns. ISO 32000-2 §7.8, §8, §9. Files absent from <c>corpus/</c> or whose SHA-256
/// differs are skipped; with none present (CI's <c>--job dry</c>) the minimal-corpus fallback runs, so the benchmark never fails
/// for want of a corpus.
/// </summary>
/// <remarks>
/// <see cref="InterpretWarm"/>: every listed page of a document opened from memory in <see cref="Setup"/> and interpreted once there,
/// so fonts, colour spaces, shadings, decoded forms and image views are cached; the processor requests every event and does
/// nothing (the interpreter's own cost). <see cref="OpenAndInterpretFirstPage"/>: page 1 of a freshly opened document, the cost of
/// resolving resources on first use.
/// </remarks>
[MemoryDiagnoser]
public class ContentInterpretationBenchmarks
{
    private readonly ContentProcessor _processor = new EverythingSink();
    private byte[] _bytes = [];
    private PdfDocument? _document;
    private PdfPage[] _pages = [];

    /// <summary>Gets or sets the file, as "category: path" (a path under <c>corpus/</c>, or under <c>tests/Corpus/</c> for the fallback).</summary>
    [ParamsSource(nameof(Files))]
    public string File { get; set; } = string.Empty;

    /// <summary>The subset entries present with their pinned hash, else the minimal-corpus fallback.</summary>
    /// <returns>The parameter values.</returns>
    public static IEnumerable<string> Files() => Subset.Load().Select(static entry => entry.Name);

    [GlobalSetup]
    public void Setup()
    {
        Subset entry = Subset.Load().First(candidate => candidate.Name == File);
        _bytes = System.IO.File.ReadAllBytes(entry.FullPath);
        _document = PdfDocument.Open(_bytes);
        _pages = [.. _document.Pages.Take(entry.Pages)];
        foreach (PdfPage page in _pages)
        {
            page.ProcessContent(_processor);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public int InterpretWarm()
    {
        foreach (PdfPage page in _pages)
        {
            page.ProcessContent(_processor);
        }

        return _pages.Length;
    }

    [Benchmark]
    public int OpenAndInterpretFirstPage()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        document.Pages[0].ProcessContent(_processor);
        return document.Pages.Count;
    }

    /// <summary>Requests every event and ignores it.</summary>
    private sealed class EverythingSink : ContentProcessor
    {
        public override ContentEvents Events => ContentEvents.All;
    }

    /// <summary>One file of <c>content-subset.json</c>.</summary>
    private sealed record Subset(string Name, string FullPath, int Pages)
    {
        public static List<Subset> Load()
        {
            string json = System.IO.File.ReadAllText(Path.Combine(CorpusLocator.RepositoryRoot, "bench", "Broadside.Benchmarks", "content-subset.json"));
            using JsonDocument document = JsonDocument.Parse(json);
            var pinned = new List<Subset>();
            if (CorpusLocator.RealWorldCorpusDirectory is { } root)
            {
                foreach (JsonElement file in document.RootElement.GetProperty("files").EnumerateArray())
                {
                    string path = file.GetProperty("path").GetString()!;
                    string full = Path.Combine(root, path);
                    if (System.IO.File.Exists(full) && Sha256(full) == file.GetProperty("sha256").GetString())
                    {
                        pinned.Add(new Subset($"{file.GetProperty("category").GetString()}: {path}", full, file.GetProperty("pages").GetInt32()));
                    }
                }
            }

            if (pinned.Count > 0)
            {
                return pinned;
            }

            return
            [
                .. document.RootElement.GetProperty("fallback").EnumerateArray().Select(static file =>
                {
                    string path = file.GetProperty("path").GetString()!;
                    return new Subset($"{file.GetProperty("category").GetString()}: {path}", Path.Combine(CorpusLocator.CorpusDirectory, path), int.MaxValue);
                }),
            ];
        }

        private static string Sha256(string path)
        {
            using FileStream stream = System.IO.File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
    }
}
