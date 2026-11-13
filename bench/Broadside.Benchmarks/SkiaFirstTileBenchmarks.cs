using BenchmarkDotNet.Attributes;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.Skia;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Benchmarks;

/// <summary>
/// Issue #374: the first tile of the catalogue-class page (<see cref="CatalogueSamples.Cover"/>, a 2550 x 3150 DCT photograph, 4:4:4
/// or 4:2:0) in the MAUI viewer's Skia tiles, from a fresh document: open, build the display list, prefetch the tile's images (with
/// SkiaSharp's decoder registered, or not) and draw the top-left 256-pixel tile of the page fitted to a phone, 1206 pixels (an iPhone
/// at 3x: the photograph at full resolution with two samples per pixel) or 390 (a quarter). Without the decoder the replay decodes
/// with the managed codec, as before #374. A 4:2:0 photograph at full resolution is not offered to Skia (libjpeg-turbo would
/// interpolate its chroma), so that case is the same either way.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class SkiaFirstTileBenchmarks
{
    private const int TileSize = 256;

    private byte[] _file = [];
    private SKBitmap? _tile;
    private SKCanvas? _canvas;

    /// <summary>The image decoder: the managed codec, or SkiaSharp's.</summary>
    [Params("managed", "skia")]
    public string Decoder { get; set; } = "managed";

    /// <summary>The chroma subsampling of the photograph.</summary>
    [Params("444", "420")]
    public string Sampling { get; set; } = "444";

    /// <summary>The page's width in pixels.</summary>
    [Params(1206, 390)]
    public int Width { get; set; } = 1206;

    [GlobalSetup]
    public void Setup()
    {
        _file = CatalogueSamples.Cover(subsampling: Sampling == "444" ? DctChromaSubsampling.None : DctChromaSubsampling.HorizontalAndVertical);
        _tile = new SKBitmap(new SKImageInfo(TileSize, TileSize, SKColorType.Bgra8888, SKAlphaType.Premul));
        _canvas = new SKCanvas(_tile);
        FirstTile();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _canvas?.Dispose();
        _tile?.Dispose();
    }

    [Benchmark]
    public int FirstTile()
    {
        PdfOptions options = Decoder == "skia" ? new PdfOptions().UseSkiaImageDecoder() : new PdfOptions();
        using PdfDocument document = PdfDocument.Open(_file, options);
        PdfPage page = document.Pages[0];
        var render = new RenderOptions().WithScale(Width / (double)new RenderOptions().GetPixelSize(page).Width);
        Matrix device = render.GetDeviceTransform(page);
        DisplayList list = page.CreateDisplayList();
        using var backend = new SkiaBackend { ScaledImageDecode = true };
        list.PrefetchImagesAsync(device, null, backend.ImageSamplesPerPixel, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        SKCanvas canvas = _canvas!;
        canvas.Clear(SKColors.White);
        backend.Render(list, canvas, device);
        canvas.Flush();
        return _tile!.GetPixel(128, 128).Green;
    }
}
