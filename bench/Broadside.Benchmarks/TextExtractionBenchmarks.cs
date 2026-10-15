using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Benchmarks;

/// <summary>
/// Extracts about 10,000 glyphs in Helvetica (the content of <see cref="TextShowBenchmarks"/>) with their Unicode text, quads and
/// flags (ISO 32000-2 §9.4.4, §9.10). <see cref="ExtractReused"/> reuses one <see cref="TextExtractor"/>: a hot path whose
/// <c>Allocated</c> must read <c>-</c>. <see cref="ExtractText"/> is the one-call API, which reuses its thread's extractor and
/// allocates the <see cref="TextPage"/> it returns: its glyph array and text, and little more (issue #284).
/// </summary>
[MemoryDiagnoser]
public class TextExtractionBenchmarks
{
    private readonly TextExtractor _extractor = new();
    private readonly TextExtractionOptions _contentOrder = new TextExtractionOptions().WithReadingOrder(TextReadingOrder.Content);
    private PdfDocument? _document;
    private PdfPage? _page;
    private PdfDocument? _tagged;
    private PdfPage? _taggedPage;

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(ContentSamples.OnePage(
            ContentSamples.TextHeavy(10_000),
            "/Font << /F1 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        _page = _document.Pages[0];
        _tagged = PdfDocument.Open(ContentSamples.TaggedText(500));
        _taggedPage = _tagged.Pages[0];
        _ = _taggedPage.ExtractText();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _document?.Dispose();
        _tagged?.Dispose();
    }

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

    /// <summary>
    /// A tagged page of 500 paragraphs (about 16,000 glyphs) painted in reverse, extracted in logical content order from its
    /// structure tree (issue #185, ISO 32000-2 §14.8.2.5.1); the tree's walk is built once, in setup. Compare with
    /// <see cref="ExtractTaggedInContentOrder"/> for the cost of ordering.
    /// </summary>
    /// <returns>The number of plain text units.</returns>
    [Benchmark]
    public int ExtractTaggedInStructureOrder() => _taggedPage!.ExtractText().PlainText.Length;

    /// <summary>The page of <see cref="ExtractTaggedInStructureOrder"/> in page content order, the structure tree not consulted.</summary>
    /// <returns>The number of plain text units.</returns>
    [Benchmark]
    public int ExtractTaggedInContentOrder() => _taggedPage!.ExtractText(_contentOrder).PlainText.Length;

    /// <summary>
    /// The layout tree of the tagged page of <see cref="ExtractTaggedInStructureOrder"/> (issue #186, ISO 32000-2 §14.8.4): the same
    /// extraction plus the mark geometry pass, one node per paragraph and its extent. Node texts are not assembled.
    /// </summary>
    /// <returns>The number of nodes under the root.</returns>
    [Benchmark]
    public int ExtractTaggedLayout() => _taggedPage!.ExtractLayout().Root.Children.Count;
}
