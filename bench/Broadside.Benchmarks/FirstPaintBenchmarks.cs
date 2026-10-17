using BenchmarkDotNet.Attributes;
using Broadside.Viewer;

namespace Broadside.Benchmarks;

/// <summary>
/// First paint of a page in the viewer (issue #212, viewer capability 2): a pinned real-world page of the rendering gate
/// (<see cref="RealWorldPages"/>) opened in a cooperative session (the browser's one thread) and scrolled to in a 1200x900 viewport at
/// 1.25 pixels per point; frames of 250 ms run until the page's first tile (its low-resolution placeholder) reaches the surface. Returns
/// the frames it took (1 = painted within the first 250 ms slice budget); the time is the open, the display-list build and the
/// placeholder's raster. Recorded (Release, Apple M-series, <c>--job short</c>, 2026-10-16) over the 21 pinned pages: every page
/// painted within its first 250 ms frame; means 0.15 to 120 ms (slowest: the Ghent CMYK test page 120 ms, personwithdog.pdf 84 ms,
/// function_based_shading.pdf 78 ms; median 4.2 ms).
/// </summary>
[MemoryDiagnoser]
public class FirstPaintBenchmarks
{
    private static readonly TimeSpan FrameBudget = TimeSpan.FromMilliseconds(250);

    private byte[] _bytes = [];
    private int _index;

    /// <summary>Gets or sets the page: <c>corpus/path#page</c>, or the fallback under <c>tests/Corpus/</c>.</summary>
    [ParamsSource(nameof(Pages))]
    public string Page { get; set; } = string.Empty;

    /// <summary>The pinned pages present on this machine, else the fallback.</summary>
    /// <returns>The parameter values.</returns>
    public static IEnumerable<string> Pages() => RealWorldPages.Available();

    [GlobalSetup]
    public void Setup() => (_bytes, _index) = RealWorldPages.Read(Page);

    /// <summary>
    /// Runs first paint with an infinite slice budget (<see cref="ViewerOptions.WithSliceBudget"/>), so a draw never splits at a
    /// wall-clock deadline: the slice count, and with it the allocation the gate compares exactly, does not depend on the runner's speed.
    /// </summary>
    /// <returns>The frames it took.</returns>
    [Benchmark]
    public int FirstPaint()
    {
        using DocumentSession session = DocumentSession.Open(_bytes, new ViewerOptions().UseEngine(RealWorldPages.Engine).WithThreading(ViewerThreading.Cooperative).WithSliceBudget(Timeout.InfiniteTimeSpan));
        PageLayout layout = PageLayout.CreateAsync(session).AsTask().GetAwaiter().GetResult();
        var surface = new FirstPaintSurface();
        using var scheduler = new TileScheduler(session, layout, surface);
        scheduler.Update(layout.ScrollTo(new Viewport(0, 0, 1200, 900, 1.25), _index));
        int frames = 0;
        bool more;
        do
        {
            frames++;
            more = scheduler.RunFrame(FrameBudget);
        }
        while (!surface.Painted && more);

        return surface.Painted ? frames : -1;
    }

    private sealed class FirstPaintSurface : IRenderSurface
    {
        public bool Painted { get; private set; }

        public void Present(Tile tile, Rendering.Bitmap pixels) => Painted = true;
    }
}
