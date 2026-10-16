using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// OpenType substitution (issue #174): measuring about 4,000 characters of Latin text with ligatures and contextual lookups, and of
/// Arabic text with joining forms and <c>rlig</c>, in an embedded program with a <c>GSUB</c> table; and positioning (issue #175):
/// as many characters of kerned Latin text with marks on bases, ligatures and marks, and of Arabic with cursive attachment and
/// marks, through a <c>GPOS</c> table. Flow layout measures every
/// candidate line this way, so shaping must allocate nothing per glyph (<c>Allocated</c> = <c>-</c>).
/// </summary>
[MemoryDiagnoser]
public class ShapingBenchmarks
{
    private PdfDocument? _document;
    private PdfCanvasFont? _font;
    private string _latin = "";
    private string _arabic = "";
    private PdfCanvasFont? _positioned;
    private string _kerned = "";
    private string _cursive = "";

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Create();
        _font = _document.EmbedTrueTypeFont(ShapingTestFont.Build());
        _latin = string.Concat(Enumerable.Repeat("office fifty xy mnp rst uvw zzz kl aeoo d́j ", 100));
        _arabic = string.Concat(Enumerable.Repeat("باب بتب بلا لا ", 250));
        _positioned = _document.EmbedTrueTypeFont(PositioningTestFont.Build());
        _kerned = string.Concat(Enumerable.Repeat("ava to te x yz h kl mnp a\u0301\u0300 f\u0301i\u0301 ", 100));
        _cursive = string.Concat(Enumerable.Repeat("\u0628\u0628 \u0628\u064E\u0628 ", 500));
        _ = Latin();
        _ = Arabic();
        _ = Kerned();
        _ = Cursive();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>The shaped advance of the Latin text.</summary>
    [Benchmark]
    public double Latin() => _font!.MeasureWidth(_latin, 10, TextShaping.Default);

    /// <summary>The shaped advance of the Arabic text.</summary>
    [Benchmark]
    public double Arabic() => _font!.MeasureWidth(_arabic, 10, TextShaping.Default);

    /// <summary>The positioned advance of the kerned Latin text with marks.</summary>
    [Benchmark]
    public double Kerned() => _positioned!.MeasureWidth(_kerned, 10, TextShaping.Default);

    /// <summary>The positioned advance of the Arabic text with cursive attachment and marks.</summary>
    [Benchmark]
    public double Cursive() => _positioned!.MeasureWidth(_cursive, 10, TextShaping.Default);
}
