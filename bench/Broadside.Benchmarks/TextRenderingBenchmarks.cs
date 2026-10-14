using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Text through glyph outlines (issue #100; ISO 32000-2 §9.3.6, §9.4.3): building the display list of a text-heavy page (glyph runs,
/// outlines interned once per glyph id) and replaying it into a reused bitmap; with text clips (issue #107: modes 4 to 7, each text
/// object's glyph outlines one clip path with a page-sized fill painted through it). Outlines are cached in the list and every glyph goes
/// through the pooled scan converter and stroker, so <c>Replay</c> must read <c>-</c> in <c>Allocated</c>: nothing per glyph.
/// <c>Broadside.Rendering.Tests.RasterizerAllocationTests.Drawing_glyphs_allocates_nothing_per_glyph_once_warm</c> fails the build if
/// it regresses, and <c>Clipping_to_text_allocates_nothing_per_glyph_once_warm</c> for the text clips.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class TextRenderingBenchmarks
{
    private PdfDocument? _document;
    private PdfPage? _page;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>
    /// The page: about 2,000 glyphs in an embedded TrueType font in modes 0 to 2, about 2,000 in the clipping modes 4 to 7, or the
    /// render-modes corpus file.
    /// </summary>
    [Params("text-heavy-2000", "text-clip-heavy-2000", "text-render-modes.pdf")]
    public string Page { get; set; } = "";

    /// <summary>The resolution.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    /// <summary>Keeps no display lists (issue #103), so that <c>Build</c> builds on every call instead of returning the kept list.</summary>
    private static PdfOptions BuildOptions => new PdfOptions().WithDisplayListCacheBudget(0);

    [GlobalSetup]
    public void Setup()
    {
        _document = Page switch
        {
            "text-heavy-2000" => PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(2000)), BuildOptions),
            "text-clip-heavy-2000" => PdfDocument.Open(TextSamples.Page(TextSamples.TextClipHeavy(2000)), BuildOptions),
            _ => PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, Page), BuildOptions),
        };
        _page = _document.Pages[0];
        _list = _page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithDpi(Dpi);
        PixelSize size = options.GetPixelSize(_page);
        _device = options.GetDeviceTransform(_page);
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
    public DisplayList Build() => _page!.CreateDisplayList();

    [Benchmark]
    public int Replay()
    {
        _rasterizer!.Render(_list!, _bitmap!, _device);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }
}
