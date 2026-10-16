using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The annotation and form-field layers (ISO 32000-2 §12.5.5, §12.7; issue #98): building the display list of a page with 80
/// annotations over its content, and replaying it through the software rasterizer, where half the annotations composite through a
/// layer with their opacity and a third pivot for NoZoom. <c>Replay</c> must read <c>-</c> under <c>Allocated</c>;
/// <c>Broadside.Rendering.Tests.AnnotationAllocationTests</c> fails the build if that regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class AnnotationRenderingBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>The resolution.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(AnnotationSamples.Grid(), new PdfOptions().WithDisplayListCacheBudget(0)); // Build measures building (issue #103).
        PdfPage page = _document.Pages[0];
        _list = page.CreateDisplayList(DisplayLayers.All);
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
    public int Build() => _document!.Pages[0].CreateDisplayList(DisplayLayers.Annotations | DisplayLayers.FormFields).Document.Pages.Count;

    [Benchmark]
    public int Replay()
    {
        _rasterizer!.Render(_list!, _bitmap!, _device);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }
}
