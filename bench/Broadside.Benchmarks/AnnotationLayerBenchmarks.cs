using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;
using Broadside.Viewer;

namespace Broadside.Benchmarks;

/// <summary>
/// One pointer move over the annotation layer (issue #217, viewer capability 6) of a page of 80 annotations: hovering (the hit test
/// and the hover update) and dragging a selected annotation, each followed by the screen rectangle the shell draws. A shell runs this
/// on every pointer move, so each must read <c>-</c> in <c>Allocated</c>; <c>Broadside.Viewer.Tests.AnnotationLayerTests</c> fails the
/// build if one regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class AnnotationLayerBenchmarks
{
    private readonly Viewport _viewport = new(0, 0, 612, 792, 1);
    private DocumentSession? _session;
    private AnnotationLayer? _hover;
    private AnnotationLayer? _drag;
    private PageLayout? _layout;
    private bool _flip;

    [GlobalSetup]
    public void Setup()
    {
        _session = DocumentSession.Open(AnnotationSamples.Grid());
        _layout = PageLayout.CreateAsync(_session).AsTask().GetAwaiter().GetResult();
        _hover = new AnnotationLayer(_session);
        _hover.LoadPageAsync(0).AsTask().GetAwaiter().GetResult();
        _drag = new AnnotationLayer(_session);
        _drag.LoadPageAsync(0).AsTask().GetAwaiter().GetResult();
        _drag.PointerDown(new PagePoint(0, 100, 30));
    }

    [GlobalCleanup]
    public void Cleanup() => _session?.Dispose();

    [Benchmark]
    public double Hover()
    {
        _flip = !_flip;
        return _hover!.PointerMove(new PagePoint(0, _flip ? 100 : 300, 30)) is { } hovered
            ? _hover.GetScreenRectangle(hovered, _layout!, _viewport).Width
            : 0;
    }

    [Benchmark]
    public double Drag()
    {
        _flip = !_flip;
        _drag!.PointerMove(new PagePoint(0, _flip ? 110 : 120, 40));
        return _drag.GetScreenRectangle(_drag.Selected!.Value, _layout!, _viewport).X;
    }
}
