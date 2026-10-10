using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Shows 4,000 glyphs of a Type 3 font (ISO 32000-2 §9.6.4) whose descriptions run nested: a <c>d1</c> square that paints a form
/// from the font's resources and a <c>d0</c> triangle that sets its colour, into a processor that enters every glyph and takes its
/// paths. A hot path (one nested run per glyph): <c>Allocated</c> must read <c>-</c>.
/// </summary>
[MemoryDiagnoser]
public class Type3GlyphBenchmarks
{
    private readonly Type3Sink _processor = new();
    private PdfDocument? _document;
    private PdfPage? _page;

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(OnePage());
        _page = _document.Pages[0];
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public long RunGlyphDescriptions()
    {
        _processor.Paths = 0;
        _page!.ProcessContent(_processor);
        return _processor.Paths;
    }

    private static byte[] OnePage()
    {
        string content = "BT /T3 12 Tf " + string.Concat(Enumerable.Repeat("(abab) Tj\n", 1_000)) + "ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /T3 5 0 R >> >> /Contents 4 0 R >>",
            ContentSamples.Stream(content),
            "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 6 0 R /b 7 0 R >> "
                + "/Encoding << /Type /Encoding /Differences [97 /a /b] >> /FirstChar 97 /LastChar 98 /Widths [1000 500] "
                + "/Resources << /XObject << /Fm 8 0 R >> >> >>",
            ContentSamples.Stream("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f /Fm Do"),
            ContentSamples.Stream("500 0 d0 1 0 0 rg 0 0 m 500 1000 l 500 0 l f"),
            ContentSamples.Stream("0 0 10 10 re f", "/Type /XObject /Subtype /Form /BBox [0 0 100 100]"),
        ];
        return ContentSamples.File(objects);
    }

    /// <summary>Enters every Type 3 glyph and counts the paths it paints.</summary>
    private sealed class Type3Sink : ContentProcessor
    {
        public long Paths { get; set; }

        public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Paths | ContentEvents.Forms | ContentEvents.Type3GlyphContent;

        public override ContentVisit BeginType3Glyph(in GlyphEvent glyph, ContentContext context) => ContentVisit.Enter;

        public override void PaintPath(in PathEvent path, ContentContext context) => Paths++;
    }
}
