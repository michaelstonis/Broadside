using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.Skia;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Benchmarks;

/// <summary>
/// The SkiaSharp backend (issue #121; ADR 0006): replaying the path corpus display lists (fills, strokes with dashes and stroke
/// adjustment, clips) onto a reused raster canvas. Geometry is recorded in kept managed buffers and each Skia path comes from the
/// backend's path cache once warm, so <c>Replay</c> must read <c>-</c> in <c>Allocated</c>: nothing per fill, stroke, dash or clip.
/// <c>Broadside.Rendering.Skia.Tests.SkiaAllocationTests</c> fails the build if it regresses. Compare with
/// <see cref="StrokeBenchmarks"/> and <see cref="ClipBenchmarks"/> for the software rasterizer's numbers.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class SkiaBackendBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private SKBitmap? _bitmap;
    private SKCanvas? _canvas;
    private SkiaBackend? _backend;
    private Matrix _device;

    /// <summary>The page: a path corpus file, or a synthetic 256 KB stroke-heavy content stream.</summary>
    [Params("paths-fill-rules.pdf", "paths-strokes.pdf", "clipping-paths.pdf", "stroke-heavy-256kb")]
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
        _bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        _canvas = new SKCanvas(_bitmap);
        _backend = new SkiaBackend();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _backend?.Dispose();
        _canvas?.Dispose();
        _bitmap?.Dispose();
        _document?.Dispose();
    }

    [Benchmark]
    public int Replay()
    {
        _backend!.Render(_list!, _canvas!, _device);
        return _bitmap!.GetPixelSpan()[(_bitmap.Height / 2 * _bitmap.RowBytes) + 2];
    }
}
