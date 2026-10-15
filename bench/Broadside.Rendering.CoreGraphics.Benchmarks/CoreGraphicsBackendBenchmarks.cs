using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.CoreGraphics;
using Broadside.TestSupport;
using CoreGraphics;

namespace Broadside.Benchmarks;

/// <summary>
/// The CoreGraphics backend (issues #125 and #127; ISO 32000-2 §8.5.3, §8.5.4, §8.4.3, §9.3.6): replaying the path corpus files and
/// synthetic path-, stroke-, clip- and text-heavy display lists onto a reused CoreGraphics bitmap context. Paths go to the context
/// point by point, colours and dashes through kept buffers, glyphs one by one through the font loaded once per program, so
/// <c>Replay</c> must read <c>-</c> in <c>Allocated</c>: nothing per path, segment, dash, clip or glyph. <c>Broadside.Rendering.CoreGraphics.Tests.ReplayAllocationTests</c> fails the build if it regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class CoreGraphicsBackendBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private CGBitmapContext? _context;
    private CoreGraphicsRenderer? _renderer;
    private Matrix _device;
    private RenderOptions? _options;

    /// <summary>
    /// The page: one of the three path corpus files, a synthetic 256 KB path-, stroke- or clip-heavy content stream, or about 2,000
    /// glyphs of an embedded TrueType program drawn by CoreGraphics' font engine or, with <c>-outlines</c>, from their outlines.
    /// </summary>
    [Params("paths-fill-rules.pdf", "paths-strokes.pdf", "clipping-paths.pdf", "path-heavy-256kb", "stroke-heavy-256kb", "clip-paths-grid", "text-heavy-2000", "text-heavy-2000-outlines")]
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
        _options = new RenderOptions().WithDpi(Dpi).WithForceOutlines(Page.EndsWith("-outlines", StringComparison.Ordinal));
        RenderOptions options = _options;
        PixelSize size = options.GetPixelSize(page);
        _device = options.GetDeviceTransform(page);
        using CGColorSpace space = CGColorSpace.CreateSrgb()!;
        _context = new CGBitmapContext(null, size.Width, size.Height, 8, size.Width * 4, space, CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little);
        _context.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, size.Height));
        _renderer = new CoreGraphicsRenderer();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _renderer?.Dispose();
        _context?.Dispose();
        _document?.Dispose();
    }

    [Benchmark]
    public nint Replay()
    {
        _renderer!.Render(_list!, _context!, _device, _options!.ForceOutlines);
        return _context!.Handle.Handle;
    }
}
