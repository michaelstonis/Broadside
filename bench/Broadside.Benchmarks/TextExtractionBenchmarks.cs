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
    private readonly Broadside.Layout.LayoutOptions _detectAll = new Broadside.Layout.LayoutOptions().WithDetection(Broadside.Layout.LayoutDetection.AllPages);
    private PdfDocument? _document;
    private PdfPage? _page;
    private PdfDocument? _tagged;
    private PdfPage? _taggedPage;
    private PdfDocument? _headings;

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
        _headings = PdfDocument.Open(ContentSamples.HeadingsAndLists(8));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _document?.Dispose();
        _tagged?.Dispose();
        _headings?.Dispose();
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

    /// <summary>
    /// The layout tree of the untagged page of <see cref="ExtractReused"/> detected from its geometry (issue #187, spec story 36):
    /// the extraction plus words and lines, the cut into blocks and paragraphs and the text rebuilt in reading order. Node texts are
    /// not assembled.
    /// </summary>
    /// <returns>The number of paragraphs.</returns>
    [Benchmark]
    public int DetectLayout() => _page!.ExtractLayout().Root.Children.Count;

    /// <summary>The tagged page of <see cref="ExtractTaggedLayout"/> detected from its geometry instead (issue #187), for comparison.</summary>
    /// <returns>The number of paragraphs.</returns>
    [Benchmark]
    public int DetectTaggedLayout() => _taggedPage!.ExtractLayout(_detectAll).Root.Children.Count;

    /// <summary>
    /// The layout tree of eight untagged pages of headings and nested lists (about 9,500 glyphs; issue #188): the detection of
    /// <see cref="DetectLayout"/> plus each paragraph's style, the document's body style and heading levels, and the lists' items,
    /// labels, bodies and nesting. Node texts are not assembled.
    /// </summary>
    /// <returns>The number of nodes under the root.</returns>
    [Benchmark]
    public int DetectHeadingsAndLists() => _headings!.ExtractLayout().Root.Children.Count;
}
