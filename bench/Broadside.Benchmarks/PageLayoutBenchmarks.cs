using BenchmarkDotNet.Attributes;
using Broadside.Viewer;

namespace Broadside.Benchmarks;

/// <summary>
/// The per-frame queries of the viewer's page layout (issue #210, viewer capability 14) over a 10,000-page continuous layout of mixed
/// sizes: the pages a viewport shows, its tiles with a render-ahead margin, and a hit test. A shell runs them on every scroll event, so
/// each must read <c>-</c> in <c>Allocated</c>; <c>Broadside.Viewer.Tests.PageLayoutAllocationTests</c> fails the build if one regresses.
/// </summary>
[MemoryDiagnoser]
public class PageLayoutBenchmarks
{
    private readonly VisiblePage[] _pages = new VisiblePage[16];
    private readonly Tile[] _tiles = new Tile[256];
    private PageLayout? _layout;
    private Viewport _viewport;

    [GlobalSetup]
    public void Setup()
    {
        var pages = new PageMetrics[10_000];
        for (int i = 0; i < pages.Length; i++)
        {
            PdfRectangle crop = (i % 3) switch
            {
                0 => new PdfRectangle(0, 0, 612, 792),
                1 => new PdfRectangle(0, 0, 595, 842),
                _ => new PdfRectangle(0, 0, 792, 612),
            };
            pages[i] = new PageMetrics(i, crop, i % 7 == 0 ? 90 : 0, 1);
        }

        _layout = PageLayout.Create(pages, new PageLayoutOptions());
        _viewport = _layout.ScrollTo(new Viewport(0, 0, 1600, 1000, 1.5), 7_500);
    }

    [Benchmark]
    public int VisiblePages() => _layout!.GetVisiblePages(_viewport, _pages);

    [Benchmark]
    public int VisibleTiles() => _layout!.GetVisibleTiles(_viewport, 256, _tiles, margin: 256);

    [Benchmark]
    public int HitTest() => _layout!.TryGetPageAt(_viewport, 800, 500, out PagePoint point) ? point.PageIndex : -1;
}
