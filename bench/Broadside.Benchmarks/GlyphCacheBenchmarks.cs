using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The software rasterizer's glyph cache (issue #370; ISO 32000-2 §10.7.4): every 256-pixel tile of a text page fitted to a phone's
/// width (1206 pixels), rendered from one display list by one rasterizer, as a viewer's tile backend renders them, with the cache
/// off (<c>NoCache</c>: budget 0, every glyph scan converted), with exact positioning (the default, the same pixels) and with
/// quantized positioning (offsets rounded to a quarter pixel). The cache is warm after the first call, so <c>Allocated</c> must read
/// <c>-</c>: a hit allocates nothing (<c>RasterizerAllocationTests.Drawing_glyphs_through_the_glyph_cache_allocates_nothing_once_warm</c>).
/// Figures: <c>docs/benchmarks/glyph-cache.md</c>.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class GlyphCacheBenchmarks
{
    private const int TileSize = 256;
    private const int PhoneWidth = 1206;

    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _tile;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;
    private int _columns;
    private int _rows;

    /// <summary>The page: the synthetic text-heavy page (an embedded TrueType font) or Standard 14 text.</summary>
    [Params("text-heavy", "text-standard14.pdf")]
    public string Page { get; set; } = "";

    /// <summary>The cache: off, exact positioning, quantized positioning.</summary>
    [Params("NoCache", "Exact", "Quantized")]
    public string Cache { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _document = Page == "text-heavy"
            ? PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(10_000)))
            : RealWorldPages.Engine.Open(File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, Page)));
        PdfPage page = _document.Pages[0];
        _list = page.CreateDisplayList();
        var options = new RenderOptions().WithScale(PhoneWidth / (double)new RenderOptions().GetPixelSize(page).Width);
        PixelSize size = options.GetPixelSize(page);
        _device = options.GetDeviceTransform(page);
        _columns = (size.Width + TileSize - 1) / TileSize;
        _rows = (size.Height + TileSize - 1) / TileSize;
        _tile = new Bitmap(TileSize, TileSize);
        _rasterizer = new SoftwareRasterizer
        {
            GlyphCacheBudget = Cache == "NoCache" ? 0 : SoftwareRasterizer.DefaultGlyphCacheBudget,
            GlyphPositioning = Cache == "Quantized" ? GlyphPositioning.Quantized : GlyphPositioning.Exact,
        };
        Tiles();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rasterizer?.Dispose();
        _tile?.Dispose();
        _document?.Dispose();
    }

    /// <summary>Renders every tile of the page once.</summary>
    /// <returns>A pixel, so the work is not optimized away.</returns>
    [Benchmark]
    public int Tiles()
    {
        for (int row = 0; row < _rows; row++)
        {
            for (int column = 0; column < _columns; column++)
            {
                _rasterizer!.Render(_list!, _tile!, _device * Matrix.CreateTranslation(-column * TileSize, -row * TileSize));
            }
        }

        return _tile!.Pixels[(128 * _tile.Stride) + 2];
    }
}
