using BenchmarkDotNet.Attributes;
using Broadside.Rendering;

namespace Broadside.Benchmarks;

/// <summary>
/// The realistic tier (#363): the first page and the heaviest page of each representative sample document (samples/documents/pdf)
/// rendered with the software rasterizer at 72 and 150 DPI (ISO 32000-2 §8, §9, §11). Not gated: see the project file.
/// </summary>
/// <remarks>
/// <see cref="Render"/>: the page of a document opened and rendered once in <see cref="Setup"/> (fonts, images and colour spaces cached;
/// the display-list cache off, so the page is interpreted again): building the display list and rasterizing it into a new bitmap.
/// <see cref="OpenAndRender"/>: a fresh document from memory and the page rendered, the first render a viewer opening the file pays.
/// </remarks>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class SampleDocumentBenchmarks
{
    private SoftwareRasterizer? _rasterizer;
    private readonly RenderOptions _options = new();
    private byte[] _bytes = [];
    private int _index;
    private PdfDocument? _document;

    /// <summary>Gets or sets the page: <c>file.pdf#page</c>, zero-based.</summary>
    [ParamsSource(nameof(Pages))]
    public string Page { get; set; } = string.Empty;

    /// <summary>Gets or sets the resolution.</summary>
    [Params(72, 150)]
    public int Dpi { get; set; }

    /// <summary>The first page and the heaviest page (<see cref="SampleDocuments.HeavyPages"/>) of every sample document.</summary>
    /// <returns>The parameter values.</returns>
    public static IEnumerable<string> Pages() => SampleDocuments.BenchmarkPages();

    [GlobalSetup]
    public void Setup()
    {
        (_bytes, _index) = SampleDocuments.Read(Page);
        _options.Dpi = Dpi;
        _rasterizer = new SoftwareRasterizer();
        _document = SampleDocuments.Engine.Open(_bytes);
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
        using PdfDocument document = SampleDocuments.Engine.Open(_bytes);
        using Bitmap bitmap = _rasterizer!.Render(document.Pages[_index], _options);
        return bitmap.Width + bitmap.Height;
    }
}
