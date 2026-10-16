using System.Globalization;
using System.Text;
using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Device resources are made once per renderer and kept within the document's image budget (spec D10): an image is converted to a
/// Direct2D bitmap once however often it is painted, and a tiling pattern's cell is drawn into a tile once per scale.
/// </summary>
public sealed class ImageCacheTests
{
    [Fact]
    public void An_image_painted_many_times_is_converted_once_per_renderer_across_renders()
    {
        using PdfDocument document = PdfDocument.Open(RepeatedImage(paints: 5, size: 8));
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content);
        using var bitmap = new Bitmap(612, 792);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        Matrix device = new RenderOptions().GetDeviceTransform(document.Pages[0]);
        renderer.Render(list, bitmap, device);
        Assert.Equal(1, renderer.ImagesCreated);

        renderer.Render(list, bitmap, device);
        Assert.Equal(1, renderer.ImagesCreated);

        // The image's pixels, and its resamplings to the device for the first transforms it is drawn at.
        Assert.InRange(renderer.CachedImageBytes, 8 * 8 * 4, 8 * 8 * 4 * 1024);
    }

    [Fact]
    public void A_zero_image_budget_converts_every_paint_keeps_nothing_and_draws_the_same()
    {
        byte[] file = RepeatedImage(paints: 5, size: 8);
        using PdfDocument cached = PdfDocument.Open(file);
        using PdfDocument uncached = PdfDocument.Open(file, new PdfOptions().WithImageCacheBudget(0));
        RenderOptions options = new();

        GoldenImage expected = Render(cached.Pages[0], options, out _, out _);
        GoldenImage actual = Render(uncached.Pages[0], options, out int created, out long kept);

        Assert.Equal(5, created);
        Assert.Equal(0, kept);
        Assert.True(GoldenComparison.Compare(expected, actual, new GoldenThreshold(0, 0, 0)).Matches);
    }

    [Fact]
    public void Large_images_stay_within_the_image_budget()
    {
        // Three distinct 64 x 64 images (16 KiB each as pixels) under a 40 KiB budget: at most two are kept at a time.
        using PdfDocument document = PdfDocument.Open(DistinctImages(count: 3, size: 64), new PdfOptions().WithImageCacheBudget(40 * 1024));

        Render(document.Pages[0], new RenderOptions(), out int created, out long kept);

        Assert.Equal(3, created);
        Assert.InRange(kept, 1, 40 * 1024);
    }

    [Theory]
    [InlineData("pattern-tiling-colored.pdf")]
    [InlineData("pattern-tiling-uncolored.pdf")]
    public void A_tiling_pattern_cell_is_drawn_once_per_renderer_and_scale(string fileName)
    {
        // Each cell of the list becomes one tile; a warm replay reuses it, a replay at another scale draws it again.
        using PdfDocument document = GoldenCorpus.Open(fileName);
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content);
        using var bitmap = new Bitmap(1300, 1700);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        Matrix at72 = new RenderOptions().GetDeviceTransform(document.Pages[0]);
        Matrix at144 = new RenderOptions().WithDpi(144).GetDeviceTransform(document.Pages[0]);
        renderer.Render(list, bitmap, at72);
        Assert.Empty(renderer.Diagnostics);
        int recorded = renderer.CellsRecorded;
        Assert.True(recorded > 0);

        renderer.Render(list, bitmap, at72);
        Assert.Equal(recorded, renderer.CellsRecorded);

        renderer.Render(list, bitmap, at144);
        Assert.Equal(2 * recorded, renderer.CellsRecorded);
    }

    [Fact]
    public void A_disposed_renderer_releases_its_device_resources()
    {
        using PdfDocument document = PdfDocument.Open(RepeatedImage(paints: 2, size: 8));
        var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        using (renderer.Render(document.Pages[0]))
        {
        }

        Assert.True(renderer.CachedImageBytes > 0);

        renderer.Dispose();

        Assert.Equal(0, renderer.CachedImageBytes);
    }

    private static GoldenImage Render(PdfPage page, RenderOptions options, out int created, out long kept)
    {
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        using Bitmap bitmap = renderer.Render(page, options);
        created = renderer.ImagesCreated;
        kept = renderer.CachedImageBytes;
        return GoldenImage.FromBitmap(bitmap);
    }

    private static byte[] RepeatedImage(int paints, int size)
    {
        var content = new StringBuilder();
        for (int i = 0; i < paints; i++)
        {
            content.Append(CultureInfo.InvariantCulture, $"q 50 0 0 50 {10 + (i * 60)} 10 cm /Im Do Q ");
        }

        return ContentSamples.OnePage(Encoding.Latin1.GetBytes(content.ToString()), "/XObject << /Im 5 0 R >>", Rgb(size, 0));
    }

    private static byte[] DistinctImages(int count, int size)
    {
        var content = new StringBuilder();
        var names = new StringBuilder();
        string[] objects = new string[count];
        for (int i = 0; i < count; i++)
        {
            content.Append(CultureInfo.InvariantCulture, $"q 100 0 0 100 {10 + (i * 110)} 10 cm /Im{i} Do Q ");
            names.Append(CultureInfo.InvariantCulture, $"/Im{i} {5 + i} 0 R ");
            objects[i] = Rgb(size, i);
        }

        return ContentSamples.OnePage(Encoding.Latin1.GetBytes(content.ToString()), $"/XObject << {names}>>", objects);
    }

    private static string Rgb(int size, int seed)
    {
        var data = new StringBuilder(size * size * 3);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                data.Append((char)((x * 255 / size) & 0xFF)).Append((char)((y * 255 / size) & 0xFF)).Append((char)((seed * 80) & 0xFF));
            }
        }

        return ContentSamples.Stream(data.ToString(), $"/Type /XObject /Subtype /Image /Width {size} /Height {size} /ColorSpace /DeviceRGB /BitsPerComponent 8");
    }
}
