using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Builds a page's display list and replays it into a backend that draws nothing (ADR 0006; ISO 32000-2 §8.5.3, §8.6). Building
/// allocates the list itself, a fixed number of arrays per page; <c>Replay</c> must read <c>-</c> in <c>Allocated</c>.
/// <c>Broadside.Tests.Rendering.DisplayListAllocationTests</c> fails the build if either regresses.
/// </summary>
[MemoryDiagnoser]
public class DisplayListBenchmarks
{
    private readonly NullBackend _backend = new();
    private PdfDocument? _document;
    private PdfPage? _page;
    private DisplayList? _list;

    /// <summary>The page: a synthetic 256 KB path-heavy content stream, or a corpus file.</summary>
    [Params("path-heavy-256kb", "colorspace-families.pdf")]
    public string Page { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _document = Page == "path-heavy-256kb"
            ? PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(256 * 1024)))
            : PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, Page));
        _page = _document.Pages[0];
        _list = _page.CreateDisplayList();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public DisplayList Build() => _page!.CreateDisplayList();

    [Benchmark]
    public int Replay()
    {
        _backend.Calls = 0;
        _list!.Replay(_backend);
        return _backend.Calls;
    }

    /// <summary>Counts calls and draws nothing.</summary>
    private sealed class NullBackend : IRenderingBackend
    {
        public int Calls { get; set; }

        public void SaveState() => Calls++;

        public void RestoreState() => Calls++;

        public void SetTransform(in Matrix transform) => Calls++;

        public void FillPath(PathView path, FillRule rule, in DisplayColor color) => Calls += path.Verbs.Length + color.Components.Length;

        public void IntersectClip(PathView path, FillRule rule) => Calls += path.Verbs.Length;
    }
}
