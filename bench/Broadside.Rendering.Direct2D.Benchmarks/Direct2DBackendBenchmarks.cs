using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.Direct2D;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The Direct2D backend (issues #129 and #131; ISO 32000-2 §8.5.3, §8.5.4, §9.4.3): replaying a display list of fills, strokes, clips
/// and glyphs (drawn by DirectWrite, or from their outlines when forced) onto a reused
/// WARP device context and reading the pixels back into a reused bitmap. The device, its bitmaps and the backend's working arrays are
/// kept, so <c>Replay</c> must read <c>-</c> in <c>Allocated</c>: nothing managed per fill, stroke, dash, clip or glyph.
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
    private RenderOptions? _options;

    /// <summary>
    /// The page: one of the three path corpus files, a synthetic 256 KB fill-heavy, stroke-heavy or clip-heavy page, or text: the
    /// embedded TrueType corpus file and a synthetic page of 2,000 glyphs, natively and with outlines forced.
    /// </summary>
    [Params("paths-fill-rules.pdf", "paths-strokes.pdf", "clipping-paths.pdf", "path-heavy-256kb", "stroke-heavy-256kb", "clip-paths-grid", "text-truetype-embedded.pdf", "text-heavy-2000", "text-heavy-2000-outlines")]
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
            "text-heavy-2000" or "text-heavy-2000-outlines" => PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(2000))),
            _ => PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, Page)),
        };
        PdfPage page = _document.Pages[0];
        _list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithDpi(Dpi).WithForceOutlines(Page.EndsWith("-outlines", StringComparison.Ordinal));
        _options = options;
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
        _renderer!.Render(_list!, _bitmap!, _device, _options!.ForceOutlines);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }
}
