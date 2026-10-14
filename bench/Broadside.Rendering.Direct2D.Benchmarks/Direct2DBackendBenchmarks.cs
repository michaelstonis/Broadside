using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.Direct2D;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The Direct2D backend (issue #129; ISO 32000-2 §8.5.3, §8.5.4): replaying a display list of fills, strokes and clips onto a reused
/// WARP device context and reading the pixels back into a reused bitmap. The device, its bitmaps and the backend's working arrays are
/// kept, so <c>Replay</c> must read <c>-</c> in <c>Allocated</c>: nothing managed per fill, stroke, dash or clip.
/// <c>Broadside.Rendering.Direct2D.Tests.ReplayAllocationTests</c> fails the build if it regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class Direct2DBackendBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private Direct2DRenderer? _renderer;
    private Matrix _device;

    /// <summary>The page: one of the three path corpus files, or a synthetic 256 KB fill-heavy, stroke-heavy or clip-heavy page.</summary>
    [Params("paths-fill-rules.pdf", "paths-strokes.pdf", "clipping-paths.pdf", "path-heavy-256kb", "stroke-heavy-256kb", "clip-paths-grid")]
    public string Page { get; set; } = "";

    /// <summary>The resolution.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = Page switch
        {
            "path-heavy-256kb" => PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(256 * 1024))),
            "stroke-heavy-256kb" => PdfDocument.Open(ContentSamples.OnePage(ContentSamples.StrokeHeavy(256 * 1024), ContentSamples.StrokeHeavyResources)),
            "clip-paths-grid" => PdfDocument.Open(ContentSamples.OnePage(ClipSamples.Grid(ClipSampleKind.Paths))),
            _ => PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, Page)),
        };
        PdfPage page = _document.Pages[0];
        _list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithDpi(Dpi);
        PixelSize size = options.GetPixelSize(page);
        _device = options.GetDeviceTransform(page);
        _bitmap = new Bitmap(size.Width, size.Height);
        _renderer = new Direct2DRenderer(Direct2DDriver.Warp);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _renderer?.Dispose();
        _bitmap?.Dispose();
        _document?.Dispose();
    }

    [Benchmark]
    public int Replay()
    {
        _renderer!.Render(_list!, _bitmap!, _device);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }
}
