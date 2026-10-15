using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// OpenType substitution (issue #174): measuring about 4,000 characters of Latin text with ligatures and contextual lookups, and of
/// Arabic text with joining forms and <c>rlig</c>, in an embedded program with a <c>GSUB</c> table. Flow layout measures every
/// candidate line this way, so shaping must allocate nothing per glyph (<c>Allocated</c> = <c>-</c>).
/// </summary>
[MemoryDiagnoser]
public class ShapingBenchmarks
{
    private PdfDocument? _document;
    private PdfCanvasFont? _font;
    private string _latin = "";
    private string _arabic = "";

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Create();
        _font = _document.EmbedTrueTypeFont(ShapingTestFont.Build());
        _latin = string.Concat(Enumerable.Repeat("office fifty xy mnp rst uvw zzz kl aeoo d́j ", 100));
        _arabic = string.Concat(Enumerable.Repeat("باب بتب بلا لا ", 250));
        _ = Latin();
        _ = Arabic();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>The shaped advance of the Latin text.</summary>
    [Benchmark]
    public double Latin() => _font!.MeasureWidth(_latin, 10, TextShaping.Default);

    /// <summary>The shaped advance of the Arabic text.</summary>
    [Benchmark]
    public double Arabic() => _font!.MeasureWidth(_arabic, 10, TextShaping.Default);
}
