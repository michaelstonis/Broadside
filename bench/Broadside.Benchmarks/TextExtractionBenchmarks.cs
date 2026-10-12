using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Benchmarks;

/// <summary>
/// Extracts about 10,000 glyphs in Helvetica (the content of <see cref="TextShowBenchmarks"/>) with their Unicode text, quads and
/// flags (ISO 32000-2 §9.4.4, §9.10). <see cref="ExtractReused"/> reuses one <see cref="TextExtractor"/>: a hot path whose
/// <c>Allocated</c> must read <c>-</c>. <see cref="ExtractText"/> is the one-call API, which allocates the extractor and the
/// <see cref="TextPage"/> it returns.
/// </summary>
[MemoryDiagnoser]
public class TextExtractionBenchmarks
{
    private readonly TextExtractor _extractor = new();
    private PdfDocument? _document;
    private PdfPage? _page;

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(ContentSamples.OnePage(
            ContentSamples.TextHeavy(10_000),
            "/Font << /F1 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        _page = _document.Pages[0];
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark(Baseline = true)]
    public int ExtractReused()
    {
        _extractor.Clear();
        _page!.ProcessContent(_extractor);
        return _extractor.Glyphs.Length;
    }

    [Benchmark]
    public int ExtractText() => _page!.ExtractText().Glyphs.Count;

    /// <summary>
    /// <see cref="ExtractReused"/> followed by assembling words, lines and the plain text (issue #184, ISO 32000-2 §14.8.2.6.2) in
    /// the same reused extractor: a hot path whose <c>Allocated</c> must read <c>-</c>.
    /// </summary>
    /// <returns>The number of plain text units.</returns>
    [Benchmark]
    public int ExtractWordsAndLinesReused()
    {
        _extractor.Clear();
        _page!.ProcessContent(_extractor);
        return _extractor.Words.Length + _extractor.Lines.Length + _extractor.PlainText.Length;
    }
}
