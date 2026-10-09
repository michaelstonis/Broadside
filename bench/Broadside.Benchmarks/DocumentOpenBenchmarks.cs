using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Opens a corpus file from memory and walks its pages: header, <c>startxref</c>, cross-reference table, trailer, catalog, page tree
/// and every page's boxes (ISO 32000-2 §7.5, §7.7). Opening allocates the document model by design, so <c>Allocated</c> is a
/// baseline to watch, not a zero to assert; the per-token hot path underneath is <see cref="LexerBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
public class DocumentOpenBenchmarks
{
    private byte[] _bytes = [];

    /// <summary>The corpus file to open: one page, a two-level page tree with inheritance, an updated file and a linearized file.</summary>
    [Params("empty-page.pdf", "page-tree-inherited.pdf", "incremental-update.pdf", "linearized.pdf")]
    public string File { get; set; } = "";

    [GlobalSetup]
    public void Setup() => _bytes = System.IO.File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, File));

    [Benchmark]
    public double OpenAndWalkPages()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        double area = 0;
        foreach (PdfPage page in document.Pages)
        {
            area += page.CropBox.Width * page.CropBox.Height;
        }

        return area;
    }

    /// <summary>
    /// Opens the file and reads its file structure: the revisions (§7.5.6) and, for a linearized file, the parameter dictionary and
    /// the page offset and shared object hint tables (Annex F, F.4).
    /// </summary>
    [Benchmark]
    public int OpenAndReadFileStructure()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        return document.Revisions.Count + (document.Linearization?.Hints?.Pages.Count ?? 0);
    }
}
