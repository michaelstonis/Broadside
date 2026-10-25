using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Rendering.Skia;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Benchmarks;

/// <summary>
/// Every 256-pixel tile of a page fitted to a phone's width (1206 pixels, an iPhone at 3x), rendered from one cached display list
/// the way the viewer's tile backends render them (spec #33 story 14): the Skia backend onto a tile's canvas (as
/// <c>SkiaTileBackend</c> does for the MAUI viewer on iOS and Android) and the software rasterizer (the Blazor viewer). The pages are the
/// cases the performance deep dive found slow per tile: an image larger than the image cache budget (re-converted on every tile
/// before), and a text page (every tile replayed the whole page on Skia before); since issue #362 also the catalogue-class photo
/// (<see cref="CatalogueSamples.Cover"/>, a 2550 x 3150 DCT image), which the software tiles decode at the resolution drawn, as
/// the viewer's software backend does. Compare one tile's cost with <see cref="TileRenderingBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class PhoneTileBenchmarks
{
    private const int TileSize = 256;
    private const int PhoneWidth = 1206;

    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _tile;
    private SoftwareRasterizer? _rasterizer;
    private SkiaBackend? _skia;
    private SKBitmap? _skiaTile;
    private SKCanvas? _skiaCanvas;
    private Matrix _device;
    private int _columns;
    private int _rows;

    /// <summary>The page: an image over the image cache budget, a text-heavy page, or the catalogue-class photograph.</summary>
    [Params("image-over-budget", "text-heavy", "image-photo")]
    public string Page { get; set; } = "";

    /// <summary>The tile backend.</summary>
    [Params("Skia", "Software")]
    public string Backend { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _document = Page switch
        {
            "image-over-budget" => PdfDocument.Open(LargeImage(1024), new PdfOptions().WithImageCacheBudget(1 << 20)),
            "image-photo" => PdfDocument.Open(CatalogueSamples.Cover()),
            _ => PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(10_000))),
        };
        PdfPage page = _document.Pages[0];
        _list = page.CreateDisplayList();
        var options = new RenderOptions().WithScale(PhoneWidth / (double)new RenderOptions().GetPixelSize(page).Width);
        PixelSize size = options.GetPixelSize(page);
        _device = options.GetDeviceTransform(page);
        _columns = (size.Width + TileSize - 1) / TileSize;
        _rows = (size.Height + TileSize - 1) / TileSize;
        _tile = new Bitmap(TileSize, TileSize);
        _rasterizer = new SoftwareRasterizer { ScaledImageDecode = true };
        _skia = new SkiaBackend();
        _skiaTile = new SKBitmap(new SKImageInfo(TileSize, TileSize, SKColorType.Bgra8888, SKAlphaType.Premul));
        _skiaCanvas = new SKCanvas(_skiaTile);
        Tiles();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _skiaCanvas?.Dispose();
        _skiaTile?.Dispose();
        _skia?.Dispose();
        _rasterizer?.Dispose();
        _tile?.Dispose();
        _document?.Dispose();
    }

    /// <summary>Renders every tile of the page once.</summary>
    [Benchmark]
    public int Tiles()
    {
        for (int row = 0; row < _rows; row++)
        {
            for (int column = 0; column < _columns; column++)
            {
                Matrix device = _device * Matrix.CreateTranslation(-column * TileSize, -row * TileSize);
                if (Backend == "Skia")
                {
                    DrawSkia(device);
                }
                else
                {
                    _rasterizer!.Render(_list!, _tile!, device);
                }
            }
        }

        return _tile!.Pixels[(128 * _tile.Stride) + 2];
    }

    private void DrawSkia(in Matrix device)
    {
        SKCanvas canvas = _skiaCanvas!;
        canvas.Clear(SKColors.White);
        _skia!.Render(_list!, canvas, device);
        canvas.Flush();
    }

    /// <summary>A page filled by one <paramref name="size"/> × <paramref name="size"/> DeviceRGB image.</summary>
    private static byte[] LargeImage(int size)
    {
        var data = new StringBuilder(size * size * 3);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                data.Append((char)(x & 0xFF)).Append((char)(y & 0xFF)).Append((char)((x ^ y) & 0xFF));
            }
        }

        string image = ContentSamples.Stream(data.ToString(), string.Create(CultureInfo.InvariantCulture, $"/Type /XObject /Subtype /Image /Width {size} /Height {size} /ColorSpace /DeviceRGB /BitsPerComponent 8"));
        return ContentSamples.OnePage(Encoding.ASCII.GetBytes("q 612 0 0 792 0 0 cm /Im Do Q"), "/XObject << /Im 5 0 R >>", image);
    }
}
