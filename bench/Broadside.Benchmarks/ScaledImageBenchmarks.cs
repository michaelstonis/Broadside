using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Issue #362, the catalogue-class page: one 2550 x 3150 DCT photograph (4:4:4, ICCBased sRGB, <see cref="CatalogueSamples"/>)
/// filling a letter page shown at a phone's width (390 pixels, or 1206 at 3x), as a viewer shows it (<c>Scaled</c>: images
/// decoded at the resolution drawn, two samples per device pixel) or as a page render does (decoded in full).
/// <see cref="Page"/> is the whole cold cost of a page: building its display list, decoding the image and rendering, with the
/// caches off so every call decodes again. <see cref="Replay"/> renders the page again from a kept list whose image is decoded:
/// the painting alone, which allocates nothing (<c>Allocated</c> reads <c>-</c>).
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class ScaledImageBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>The device width the page is fitted to, in pixels.</summary>
    [Params(390, 1206)]
    public int Width { get; set; }

    /// <summary>Whether images are decoded at the resolution drawn, as viewers do.</summary>
    [Params(false, true)]
    public bool Scaled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0).WithImageCacheBudget(0));
        PdfPage page = _document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(Width / page.CropBox.Width);
        PixelSize size = options.GetPixelSize(page);
        _device = options.GetDeviceTransform(page);
        _bitmap = new Bitmap(size.Width, size.Height);
        _rasterizer = new SoftwareRasterizer { ScaledImageDecode = Scaled };
        _list = page.CreateDisplayList();
        Replay();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rasterizer?.Dispose();
        _bitmap?.Dispose();
        _document?.Dispose();
    }

    /// <summary>Builds the page's list, decodes its image and renders it: what a viewer pays the first time it shows the page.</summary>
    [Benchmark]
    public int Page()
    {
        DisplayList list = _document!.Pages[0].CreateDisplayList();
        _rasterizer!.Render(list, _bitmap!, _device);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }

    /// <summary>Renders the page again from a kept list whose image is already decoded.</summary>
    [Benchmark]
    public int Replay()
    {
        _rasterizer!.Render(_list!, _bitmap!, _device);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }
}
