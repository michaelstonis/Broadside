using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.CoreGraphics;
using Broadside.TestSupport;
using CoreGraphics;

namespace Broadside.Benchmarks;

/// <summary>
/// The CoreGraphics backend (issue #125; ISO 32000-2 §8.5.3, §8.5.4, §8.4.3): replaying the path corpus files and synthetic path-,
/// stroke- and clip-heavy display lists onto a reused CoreGraphics bitmap context. Paths go to the context point by point, colours
/// and dashes through kept buffers, so <c>Replay</c> must read <c>-</c> in <c>Allocated</c>: nothing per path, segment, dash or
/// clip. <c>Broadside.Rendering.CoreGraphics.Tests.ReplayAllocationTests</c> fails the build if it regresses.
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

    /// <summary>The page: one of the three path corpus files, or a synthetic 256 KB path-, stroke- or clip-heavy content stream.</summary>
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
        _renderer!.Render(_list!, _context!, _device);
        return _context!.Handle.Handle;
    }
}
