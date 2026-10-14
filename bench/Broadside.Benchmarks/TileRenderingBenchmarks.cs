using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// One 256-pixel tile at the centre of a page, rendered by the software rasterizer from a cached display list (spec #33 story 14,
/// viewer capability 1): the list skips the paints whose bounds miss the tile (<see cref="DisplayList.Replay(IRenderingBackend, PdfRectangle)"/>),
/// so a tile costs what its own content costs, at any scale. <c>Tile</c> must read <c>-</c> in <c>Allocated</c>;
/// <c>Broadside.Rendering.Tests.RasterizerAllocationTests</c> fails the build if it regresses. Compare with
/// <c>RasterizerBenchmarks.Replay</c>, the whole page.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class TileRenderingBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _tile;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>The page: a synthetic 256 KB path-heavy content stream, or a corpus file.</summary>
    [Params("path-heavy-256kb", "paths-fill-rules.pdf")]
    public string Page { get; set; } = "";

    /// <summary>The zoom: device pixels per point.</summary>
    [Params(1, 4)]
    public int Scale { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = Page == "path-heavy-256kb"
            ? PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(256 * 1024)))
            : PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, Page));
        PdfPage page = _document.Pages[0];
        _list = page.CreateDisplayList();
        var options = new RenderOptions().WithScale(Scale);
        PixelSize size = options.GetPixelSize(page);
        _device = options.GetDeviceTransform(page) * Matrix.CreateTranslation(-(size.Width - 256) / 2, -(size.Height - 256) / 2);
        _tile = new Bitmap(256, 256);
        _rasterizer = new SoftwareRasterizer();

        // The list computes its bounds on its first region render; the benchmark measures the renders after that.
        _rasterizer.Render(_list, _tile, _device);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rasterizer?.Dispose();
        _tile?.Dispose();
        _document?.Dispose();
    }

    [Benchmark]
    public int Tile()
    {
        _rasterizer!.Render(_list!, _tile!, _device);
        return _tile!.Pixels[(128 * _tile.Stride) + 2];
    }
}
