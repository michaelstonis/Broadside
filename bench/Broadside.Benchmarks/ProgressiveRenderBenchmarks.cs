using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// What cancellation, progress and time slicing cost a render (issue #97, viewer capability 2): the 256 KB path-heavy page drawn
/// in one go (<c>Whole</c>, as <c>RasterizerBenchmarks.Replay</c>), as a progressive render with a cancellable token and a progress
/// receiver continued to the end (<c>Cancellable</c>), and in 1 ms slices that resume where they stopped (<c>Sliced</c>).
/// <c>Allocated</c> must read <c>-</c> for <c>Whole</c> and the same small constant per render for the others, however many slices
/// it takes (in-process, short job: 1.4 KB, nothing per operation or slice); <c>Broadside.Rendering.Tests.ProgressiveRenderAllocationTests</c>
/// fails the build if anything is allocated per operation.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class ProgressiveRenderBenchmarks
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly NullProgress _progress = new();
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(256 * 1024)));
        PdfPage page = _document.Pages[0];
        _list = page.CreateDisplayList();
        var options = new RenderOptions();
        PixelSize size = options.GetPixelSize(page);
        _device = options.GetDeviceTransform(page);
        _bitmap = new Bitmap(size.Width, size.Height);
        _rasterizer = new SoftwareRasterizer();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rasterizer?.Dispose();
        _bitmap?.Dispose();
        _document?.Dispose();
        _cancellation.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int Whole()
    {
        _rasterizer!.Render(_list!, _bitmap!, _device);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }

    [Benchmark]
    public int Cancellable()
    {
        using ProgressiveRender render = _rasterizer!.BeginRender(_list!, _bitmap!, _device, _cancellation.Token, _progress);
        render.Continue(Timeout.InfiniteTimeSpan);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }

    [Benchmark]
    public int Sliced()
    {
        using ProgressiveRender render = _rasterizer!.BeginRender(_list!, _bitmap!, _device, _cancellation.Token, _progress);
        int slices = 1;
        while (!render.Continue(TimeSpan.FromMilliseconds(1)))
        {
            slices++;
        }

        return slices + _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }

    private sealed class NullProgress : IProgress<double>
    {
        public void Report(double value)
        {
        }
    }
}
