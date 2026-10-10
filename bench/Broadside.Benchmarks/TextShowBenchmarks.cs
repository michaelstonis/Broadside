using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Shows about 10,000 glyphs in Helvetica (text state operators, <c>TJ</c> arrays with adjustments, <c>'</c> and <c>"</c>) through
/// the content interpreter into a processor that takes every glyph event and does nothing (ISO 32000-2 §9.3, §9.4). A hot path:
/// <c>Allocated</c> must read <c>-</c>.
/// </summary>
[MemoryDiagnoser]
public class TextShowBenchmarks
{
    private readonly GlyphSink _processor = new();
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

    [Benchmark]
    public long ShowGlyphs()
    {
        _processor.Glyphs = 0;
        _page!.ProcessContent(_processor);
        return _processor.Glyphs;
    }

    /// <summary>Takes glyph events and counts them.</summary>
    private sealed class GlyphSink : ContentProcessor
    {
        public long Glyphs { get; set; }

        public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Text;

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context) => Glyphs++;
    }
}
