using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.Skia;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Benchmarks;

/// <summary>
/// Text on the SkiaSharp backend (issue #123; ISO 32000-2 §9.3.6, §9.4.3): replaying 2,000 glyphs of an embedded TrueType program,
/// natively through a typeface created once per program (one cached text blob per glyph) or from their outlines (one cached Skia
/// path per glyph), and 2,000 Type 3 glyphs (paths). <c>Replay</c> must read <c>-</c> in <c>Allocated</c>;
/// <c>Broadside.Rendering.Skia.Tests.SkiaAllocationTests</c> fails the build if it regresses. Compare with
/// <see cref="TextRenderingBenchmarks"/> for the software rasterizer.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class SkiaTextBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private SKBitmap? _bitmap;
    private SKCanvas? _canvas;
    private SkiaBackend? _backend;
    private Matrix _device;

    /// <summary>The page: 2,000 glyphs of an embedded TrueType program, or 2,000 Type 3 glyphs.</summary>
    [Params("text-heavy-2000", "type3-heavy-2000")]
    public string Page { get; set; } = "";

    /// <summary>Whether every glyph is drawn from its outline (<see cref="RenderOptions.ForceOutlines"/>).</summary>
    [Params(false, true)]
    public bool ForceOutlines { get; set; }

    /// <summary>The resolution.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(Page == "text-heavy-2000" ? TextSamples.Page(ContentSamples.TextHeavy(2000)) : Type3Samples.Type3Heavy(2000));
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
        _backend!.Render(_list!, _canvas!, _device, ForceOutlines);
        return _bitmap!.GetPixelSpan()[(_bitmap.Height / 2 * _bitmap.RowBytes) + 2];
    }
}
