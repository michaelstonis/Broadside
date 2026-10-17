using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The software rasterizer replaying a page's display list into a reused bitmap (ADR 0006; ISO 32000-2 §8.5.3, §10.7.4). The scan
/// converter keeps its cells in pooled arrays, so <c>Replay</c> must read <c>-</c> in <c>Allocated</c>: nothing per path, cell or
/// scanline. <c>Broadside.Rendering.Tests.RasterizerAllocationTests</c> fails the build if it regresses. <c>EncodePng</c> is the
/// PNG writer over the same bitmap (it allocates its compressed output).
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class RasterizerBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>The page: a synthetic 256 KB path-heavy content stream, or a corpus file.</summary>
    [Params("path-heavy-256kb", "paths-fill-rules.pdf")]
    public string Page { get; set; } = "";

    /// <summary>The resolution.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = Page == "path-heavy-256kb"
            ? PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(256 * 1024)))
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

    /// <summary>The same replay with anti-aliasing off (§10.7.4: whole pixels only); it too must allocate nothing.</summary>
    [Benchmark]
    public int ReplayAliased()
    {
        _rasterizer!.Render(_list!, _bitmap!, _device, AntiAliasing.None);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }

    [Benchmark]
    public long EncodePng()
    {
        using var stream = new MemoryStream();
        _bitmap!.SavePng(stream);
        return stream.Length;
    }
}
