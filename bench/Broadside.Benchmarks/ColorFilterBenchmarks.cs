using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Smart dark mode (spec #33 viewer capability 9): the software rasterizer replaying a page's display list with
/// <see cref="ColorFilter.DarkMode"/>, which records the area images cover and inverts the lightness of every other pixel when the
/// replay finishes. Like <see cref="RasterizerBenchmarks"/> it must read <c>-</c> in <c>Allocated</c>;
/// <c>Broadside.Rendering.Tests.ColorFilterAllocationTests</c> fails the build if it regresses. Compare with <c>Replay</c> there for
/// the filter's cost.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class ColorFilterBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>The page: synthetic paths (the filter over every pixel) or images (their covered area recorded too).</summary>
    [Params("paths", "images")]
    public string Page { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(Page == "paths" ? ContentSamples.OnePage(ContentSamples.PathHeavy(64 * 1024)) : ContentSamples.ImageHeavy());
        PdfPage page = _document.Pages[0];
        _list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithDpi(144);
        PixelSize size = options.GetPixelSize(page);
        _device = options.GetDeviceTransform(page);
        _bitmap = new Bitmap(size.Width, size.Height);
        _rasterizer = new SoftwareRasterizer { ColorFilter = ColorFilter.DarkMode };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rasterizer?.Dispose();
        _bitmap?.Dispose();
        _document?.Dispose();
    }

    [Benchmark]
    public int ReplayDark()
    {
        _bitmap!.Pixels.Fill(byte.MaxValue);
        _rasterizer!.Render(_list!, _bitmap, _device);
        return _bitmap.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }
}
