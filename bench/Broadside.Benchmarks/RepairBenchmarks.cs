using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Lenient repair (ADR 0005, issue #41): opening each deliberately broken corpus file and walking its pages, which runs the repair
/// (object search, nearest section, reconstruction, stream length recovery). <see cref="ReconstructionBenchmarks"/> measures the
/// scan the reconstruction is built on at scale.
/// </summary>
[MemoryDiagnoser]
public class RepairBenchmarks
{
    private byte[] _bytes = [];

    /// <summary>The broken corpus file to open.</summary>
    [Params("broken-xref-offsets.pdf", "missing-endobj.pdf", "wrong-stream-length.pdf", "no-xref.pdf", "startxref-wrong.pdf")]
    public string File { get; set; } = "";

    [GlobalSetup]
    public void Setup() => _bytes = System.IO.File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, File));

    /// <summary>Opens the broken file leniently and walks its pages.</summary>
    [Benchmark]
    public int OpenBrokenFile()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        return document.Pages.Count + document.Diagnostics.Count;
    }
}
