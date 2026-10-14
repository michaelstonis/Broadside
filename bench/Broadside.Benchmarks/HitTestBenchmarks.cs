using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The hit-test index (issue #101, viewer capability 4) of a text-heavy page: building it from a cached display list (a fixed number
/// of arrays per page, nothing per glyph) and querying it by point, by a small rectangle (one grid cell) and by a page-wide rectangle
/// (a scan in content order). Queries must read <c>-</c> in <c>Allocated</c>;
/// <c>Broadside.Tests.Rendering.HitTestAllocationTests</c> fails the build if either regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class HitTestBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private HitTestIndex? _index;
    private PathPoint _point;

    /// <summary>The number of glyphs on the page.</summary>
    [Params(2000, 10_000)]
    public int Glyphs { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(Glyphs)));
        _list = _document.Pages[0].CreateDisplayList();
        _index = _list.CreateHitTestIndex();
        PdfRectangle box = _index.GetGlyph(_index.GlyphCount / 2).Quad.Bounds;
        _point = new PathPoint((box.Left + box.Right) / 2, (box.Bottom + box.Top) / 2);
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public HitTestIndex Build() => _list!.CreateHitTestIndex();

    [Benchmark]
    public int FindPoint()
    {
        int found = 0;
        foreach (HitTestItem item in _index!.Find(_point, HitTestKinds.All))
        {
            found += item.Index;
        }

        return found;
    }

    [Benchmark]
    public int FindSmallRectangle()
    {
        int found = 0;
        foreach (HitTestItem item in _index!.Find(new PdfRectangle(_point.X, _point.Y, _point.X + 0.5, _point.Y + 0.5), HitTestKinds.Glyphs))
        {
            found += item.Index;
        }

        return found;
    }

    [Benchmark]
    public int FindPageRectangle()
    {
        int found = 0;
        foreach (HitTestItem item in _index!.Find(new PdfRectangle(0, 0, 612, 792), HitTestKinds.Glyphs))
        {
            found += item.Index;
        }

        return found;
    }
}
