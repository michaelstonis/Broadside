using System.Globalization;
using System.Text;
using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Tiles of one display list on the Skia backend (spec #33 story 14, viewer capability 1; the MAUI viewer's tile backend): each tile
/// replays only the paints that reach it, converts an image once for all tiles, and shows the pixels the whole page shows there.
/// </summary>
public sealed class SkiaTileTests
{
    private const int TileSize = 64;

    /// <remarks>
    /// One backend draws the page, then its tiles, so the tiles reuse the page's resamplings moved by whole pixels. Pages with paths or
    /// text are left out: Skia's own anti-aliasing of edges and glyphs differs by a level or two in a few pixels along
    /// tile seams whatever the list replays.
    /// </remarks>
    [Theory]
    [InlineData("image-stencil-mask.pdf")]
    [InlineData("image-smask.pdf")]
    [InlineData("image-4bpc-indexed.pdf")]
    [InlineData("image-explicit-mask.pdf")]
    [InlineData("shading-type2-axial.pdf")]
    public void Tiles_show_the_pixels_of_the_whole_page(string fileName)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var backend = new SkiaBackend();
        using var whole = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(whole))
        {
            canvas.Clear(SKColors.White);
            backend.Render(list, canvas, device);
        }

        using var tiled = new SKBitmap(whole.Info);
        using (var canvas = new SKCanvas(tiled))
        {
            using var tile = new SKBitmap(new SKImageInfo(TileSize, TileSize, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var tileCanvas = new SKCanvas(tile);
            for (int y = 0; y < size.Height; y += TileSize)
            {
                for (int x = 0; x < size.Width; x += TileSize)
                {
                    tileCanvas.Clear(SKColors.White);
                    backend.Render(list, tileCanvas, device * Matrix.CreateTranslation(-x, -y));
                    using SKImage image = SKImage.FromBitmap(tile);
                    canvas.DrawImage(image, x, y, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), null);
                }
            }
        }

        Assert.Equal(whole.Pixels, tiled.Pixels);
    }

    [Fact]
    public void An_image_larger_than_the_image_budget_is_converted_once_for_every_tile()
    {
        // A 64 x 64 image is 16 KiB of Skia pixels, over a 4 KiB budget: before, every tile converted it again.
        using PdfDocument document = PdfDocument.Open(OneImage(64, "q 400 0 0 400 100 200 cm /Im Do Q"), new PdfOptions().WithImageCacheBudget(4 * 1024));
        DisplayList list = document.Pages[0].CreateDisplayList();
        Matrix device = new RenderOptions().GetDeviceTransform(document.Pages[0]);
        using var backend = new SkiaBackend();
        RenderTiles(backend, list, device, columns: 612 / TileSize, rows: 792 / TileSize);

        Assert.Equal(1, backend.ImagesCreated);
    }

    [Fact]
    public void A_tile_the_image_does_not_reach_does_not_convert_it()
    {
        using PdfDocument document = PdfDocument.Open(OneImage(8, "q 50 0 0 50 500 700 cm /Im Do Q"));
        DisplayList list = document.Pages[0].CreateDisplayList();
        Matrix device = new RenderOptions().GetDeviceTransform(document.Pages[0]);
        using var backend = new SkiaBackend();

        // The top-left tile: the image is at the top right of the page.
        RenderTiles(backend, list, device, columns: 1, rows: 1);

        Assert.Equal(0, backend.ImagesCreated);
    }

    private static void RenderTiles(SkiaBackend backend, DisplayList list, in Matrix device, int columns, int rows)
    {
        using var tile = new SKBitmap(new SKImageInfo(TileSize, TileSize, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(tile);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                canvas.Clear(SKColors.White);
                backend.Render(list, canvas, device * Matrix.CreateTranslation(-x * TileSize, -y * TileSize));
            }
        }
    }

    private static byte[] OneImage(int size, string content)
    {
        var data = new StringBuilder(size * size * 3);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                data.Append((char)((x * 255 / size) & 0xFF)).Append((char)((y * 255 / size) & 0xFF)).Append((char)0x80);
            }
        }

        string image = ContentSamples.Stream(data.ToString(), string.Create(CultureInfo.InvariantCulture, $"/Type /XObject /Subtype /Image /Width {size} /Height {size} /ColorSpace /DeviceRGB /BitsPerComponent 8"));
        return ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), "/XObject << /Im 5 0 R >>", image);
    }
}
