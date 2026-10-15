using BenchmarkDotNet.Attributes;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// A page render with and without overprint preview (ISO 32000-2 §8.6.7, §11.7.4; issue #227): 140 cells of overprinting fills,
/// spot and DeviceN fills, strokes, CMYK images, stencil masks and shadings (<see cref="OverprintSamples.Grid"/>). A page render
/// builds the display list too, which allocates the same either way; <c>Broadside.Rendering.Tests.OverprintAllocationTests</c>
/// fails the build if the preview starts allocating per paint.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class OverprintBenchmarks
{
    private PdfDocument? _document;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private RenderOptions? _options;

    /// <summary>The resolution.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    /// <summary>Whether overprint preview is on.</summary>
    [Params(false, true)]
    public bool Preview { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(OverprintSamples.Grid(140));
        _options = new RenderOptions().WithDpi(Dpi).WithOverprintPreview(Preview);
        PixelSize size = _options.GetPixelSize(_document.Pages[0]);
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
    public int Render()
    {
        _rasterizer!.Render(_document!.Pages[0], _bitmap!, _options!);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }
}
