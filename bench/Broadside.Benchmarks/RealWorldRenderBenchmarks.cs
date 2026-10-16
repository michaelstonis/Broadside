using BenchmarkDotNet.Attributes;
using Broadside.Rendering;

namespace Broadside.Benchmarks;

/// <summary>
/// Rendering the pinned real-world pages of the rendering gate (issue #140; <see cref="RealWorldPages"/>) with the software rasterizer
/// at 72 and 150 DPI, every layer, the Broadside side of the Phase 3 baseline compared with PDFium and pdf.js in
/// <c>docs/benchmarks/phase-3-render-baselines.md</c> (ISO 32000-2 §8, §9, §11).
/// </summary>
/// <remarks>
/// <see cref="Render"/>: the page of a document opened and rendered once in <see cref="Setup"/> (fonts, images, colour spaces and
/// functions cached; the display-list cache off, so the page is interpreted again): the cost of building the display list and
/// rasterizing it into a new bitmap. <see cref="OpenAndRender"/>: a fresh document from memory and the page rendered: the first
/// render a viewer opening the file pays.
/// </remarks>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class RealWorldRenderBenchmarks
{
    private SoftwareRasterizer? _rasterizer;
    private readonly RenderOptions _options = new();
    private byte[] _bytes = [];
    private int _index;
    private PdfDocument? _document;

    /// <summary>Gets or sets the page: <c>corpus/path#page</c>, or the fallback under <c>tests/Corpus/</c>.</summary>
    [ParamsSource(nameof(Pages))]
    public string Page { get; set; } = string.Empty;

    /// <summary>Gets or sets the resolution.</summary>
    [Params(72, 150)]
    public int Dpi { get; set; }

    /// <summary>The pinned pages present on this machine, else the fallback.</summary>
    /// <returns>The parameter values.</returns>
    public static IEnumerable<string> Pages() => RealWorldPages.Available();

    [GlobalSetup]
    public void Setup()
    {
        (_bytes, _index) = RealWorldPages.Read(Page);
        _options.Dpi = Dpi;
        _rasterizer = new SoftwareRasterizer();
        _document = RealWorldPages.Engine.Open(_bytes);
        Render();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _document?.Dispose();
        _rasterizer?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int Render()
    {
        using Bitmap bitmap = _rasterizer!.Render(_document!.Pages[_index], _options);
        return bitmap.Width + bitmap.Height;
    }

    [Benchmark]
    public int OpenAndRender()
    {
        using PdfDocument document = RealWorldPages.Engine.Open(_bytes);
        using Bitmap bitmap = _rasterizer!.Render(document.Pages[_index], _options);
        return bitmap.Width + bitmap.Height;
    }
}
