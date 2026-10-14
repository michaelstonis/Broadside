using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Type 3 glyphs in the display list and the software rasterizer (issue #108; ISO 32000-2 §9.6.4): <see cref="Type3Samples.Type3Heavy"/>
/// shows about 2,000 glyphs of four descriptions (a d1 shape, a d1 inline image mask, a d0 glyph that fills and strokes, a d1 glyph
/// painting a clipped form) in three colours and two sizes. <c>Build</c> runs each description once and copies its sub-list at
/// every show: its <c>Allocated</c> is the list itself (about 124 bytes of records per glyph), nothing per glyph besides.
/// <c>Replay</c> must read <c>-</c> in <c>Allocated</c>. <c>Broadside.Tests.Rendering.DisplayListAllocationTests</c> and
/// <c>Broadside.Rendering.Tests.RasterizerAllocationTests</c> fail the build if either regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class Type3RenderingBenchmarks
{
    private PdfDocument? _document;
    private PdfPage? _page;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>The resolution of <see cref="Replay"/>.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(Type3Samples.Type3Heavy(2000), new PdfOptions().WithDisplayListCacheBudget(0)); // Build measures building (issue #103).
        _page = _document.Pages[0];
        _list = _page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithDpi(Dpi);
        PixelSize size = options.GetPixelSize(_page);
        _device = options.GetDeviceTransform(_page);
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
    public DisplayList Build() => _page!.CreateDisplayList();

    [Benchmark]
    public int Replay()
    {
        _rasterizer!.Render(_list!, _bitmap!, _device);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }
}
