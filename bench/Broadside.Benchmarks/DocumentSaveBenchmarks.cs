using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Opens a corpus file from memory and saves it, unchanged, to a null stream in each cross-reference layout (ISO 32000-2 §7.5): every
/// object loaded, unchanged objects copied from their source bytes, and a new cross-reference table or stream (with object streams,
/// Flate-encoded). Saving builds output by design, so <c>Allocated</c> is a baseline to watch, not a zero to assert.
/// </summary>
[MemoryDiagnoser]
public class DocumentSaveBenchmarks
{
    private byte[] _bytes = [];

    /// <summary>The corpus file to save: one page, an embedded font, and a file with an object stream.</summary>
    [Params("empty-page.pdf", "text-truetype-embedded.pdf", "object-stream.pdf")]
    public string File { get; set; } = "";

    /// <summary>The layout to write.</summary>
    [Params(PdfCrossReferenceLayout.Table, PdfCrossReferenceLayout.StreamWithObjectStreams)]
    public PdfCrossReferenceLayout Layout { get; set; }

    [GlobalSetup]
    public void Setup() => _bytes = System.IO.File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, File));

    [Benchmark]
    public void OpenAndSave()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        document.Save(Stream.Null, new PdfSaveOptions().WithCrossReferenceLayout(Layout));
    }
}
