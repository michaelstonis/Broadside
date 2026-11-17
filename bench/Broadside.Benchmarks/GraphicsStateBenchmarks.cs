using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The cost of copying the graphics state (ISO 32000-2 §8.4.2: <c>q</c> pushes a copy of the whole state; issue #257). Two views:
/// <see cref="CopyState"/> copies one <see cref="GraphicsState"/> (stroking and non-stroking colors set, so the copy carries live
/// values) and nothing else; <see cref="SaveRestore"/> interprets a page made only of nested <c>q</c>/<c>Q</c> (20,000 of each) with a
/// processor that asks for no state events, so the mean is one <c>q</c> plus one <c>Q</c> including reading both operators. The
/// per-glyph copy of a Type 3 description is in <see cref="Type3GlyphBenchmarks"/>. Hot paths: <c>Allocated</c> must read <c>-</c>.
/// </summary>
[MemoryDiagnoser]
public class GraphicsStateBenchmarks
{
    private const int Copies = 1_024;
    private const int Saves = 20_000;

    private readonly CountingSink _processor = new();
    private readonly GraphicsState[] _states = new GraphicsState[2];
    private PdfDocument? _document;
    private PdfPage? _page;

    [GlobalSetup]
    public void Setup()
    {
        GraphicsState state = GraphicsState.CreateInitial();
        state.SetColor(stroke: true, PdfColor.FromRgb(1, 0, 0));
        state.SetColor(stroke: false, PdfColor.FromCmyk(0, 0.5f, 1, 0));
        _states[0] = state;

        string content = string.Concat(Enumerable.Repeat("q q q q Q Q Q Q\n", Saves / 4));
        _document = PdfDocument.Open(ContentSamples.OnePage(System.Text.Encoding.ASCII.GetBytes(content)));
        _page = _document.Pages[0];
        _page.ProcessContent(_processor);
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>One graphics-state copy, as <c>q</c> makes it.</summary>
    /// <returns>A value derived from the copies.</returns>
    [Benchmark(OperationsPerInvoke = Copies)]
    public double CopyState()
    {
        GraphicsState[] states = _states;
        double sum = 0;
        for (int i = 0; i < Copies; i++)
        {
            states[(i + 1) & 1] = states[i & 1];
            sum += states[(i + 1) & 1].LineWidth;
        }

        return sum;
    }

    /// <summary>One <c>q</c> and one <c>Q</c>, interpreted.</summary>
    /// <returns>The paths painted: none, the page has only <c>q</c> and <c>Q</c>.</returns>
    [Benchmark(OperationsPerInvoke = Saves)]
    public long SaveRestore()
    {
        _processor.Paths = 0;
        _page!.ProcessContent(_processor);
        return _processor.Paths;
    }

    /// <summary>Asks for paths only (none on the page), so <c>q</c>/<c>Q</c> report nothing.</summary>
    private sealed class CountingSink : ContentProcessor
    {
        public long Paths { get; set; }

        public override ContentEvents Events => ContentEvents.Paths;

        public override void PaintPath(in PathEvent path, ContentContext context) => Paths++;
    }
}
