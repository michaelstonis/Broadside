using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The software rasterizer's stroker (issue #99; ISO 32000-2 §8.4.3, §8.5.3.2, §10.7.5): replaying a stroke-heavy display list into a
/// reused bitmap. Outlines go straight into the pooled scan converter and the stroker's working arrays are kept, so <c>Replay</c>
/// must read <c>-</c> in <c>Allocated</c>: nothing per subpath, segment, dash or join.
/// <c>Broadside.Rendering.Tests.RasterizerAllocationTests.Stroking_allocates_nothing_per_segment_once_warm</c> fails the build if it
/// regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class StrokeBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>The page: a synthetic 256 KB stroke-heavy content stream, or the strokes corpus file.</summary>
    [Params("stroke-heavy-256kb", "paths-strokes.pdf")]
    public string Page { get; set; } = "";

    /// <summary>The resolution.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = Page == "stroke-heavy-256kb"
            ? PdfDocument.Open(ContentSamples.OnePage(ContentSamples.StrokeHeavy(256 * 1024), ContentSamples.StrokeHeavyResources))
            : PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, Page));
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
