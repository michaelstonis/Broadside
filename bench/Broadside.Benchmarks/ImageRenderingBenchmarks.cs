using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The software rasterizer drawing images (issue #102; ISO 32000-2 §8.9.4, §8.9.5): <see cref="ContentSamples.ImageHeavy"/> paints
/// ten images through every conversion path (8-bit bytes, 16-bit and inverted-Decode component values, Indexed) and every
/// resampling path (nearest, bilinear, box filter, rotated). <c>Replay</c> converts each image afresh and must read <c>-</c> in
/// <c>Allocated</c>: nothing per image, row or pixel. <c>Build</c> decodes the images into a new display list (it allocates the
/// decoded samples and the list). <c>Broadside.Rendering.Tests.RasterizerAllocationTests</c> fails the build if replay regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class ImageRenderingBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>The resolution.</summary>
    [Params(72, 300)]
    public int Dpi { get; set; }

    /// <summary>Keeps no display lists (issue #103), so that <c>Build</c> builds on every call instead of returning the kept list.</summary>
    private static PdfOptions BuildOptions => new PdfOptions().WithDisplayListCacheBudget(0).WithImageCacheBudget(0);

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(ContentSamples.ImageHeavy(), BuildOptions);
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

    [Benchmark]
    public DisplayList Build() => _document!.Pages[0].CreateDisplayList();
}
