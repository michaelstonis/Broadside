using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The software rasterizer replaying clipped paints (ISO 32000-2 §8.5.4; ADR 0006): a grid of 192 cells, each with two fills inside
/// nested <c>q</c>/<c>Q</c>, clipped by nothing (the baseline), by rectangles (the fast path: a narrower clip rectangle, no mask) or by
/// a triangle and an even-odd diamond (coverage masks, one copied per nesting level). <c>Rectangles</c> should cost about what
/// <c>None</c> does; <c>Paths</c> pays for scan converting and writing each mask. <c>Allocated</c> must read <c>-</c> for every kind:
/// masks are pooled per rasterizer. <c>Broadside.Rendering.Tests.ClipAllocationTests</c> fails the build if that regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class ClipBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>What clips each cell's paints.</summary>
    [Params(ClipSampleKind.None, ClipSampleKind.Rectangles, ClipSampleKind.Paths)]
    public ClipSampleKind Clips { get; set; }

    /// <summary>The resolution.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(ContentSamples.OnePage(ClipSamples.Grid(Clips)));
        PdfPage page = _document.Pages[0];
        _list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithDpi(Dpi);
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
    }

    [Benchmark]
    public int Replay()
    {
        _rasterizer!.Render(_list!, _bitmap!, _device);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }
}
