using System.Globalization;
using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Images interned once per display list map to one Skia image per backend (issue #122), kept within the document's image cache
/// budget (spec D10: <see cref="PdfOptions.ImageCacheBudget"/>; zero disables the cache without changing the output).
/// </summary>
public sealed class SkiaImageCacheTests
{
    [Fact]
    public void An_image_painted_many_times_is_converted_once_per_backend_across_renders()
    {
        using PdfDocument document = PdfDocument.Open(RepeatedImage(paints: 5, size: 8));
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content);
        using var bitmap = new SKBitmap(new SKImageInfo(612, 792, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var backend = new SkiaBackend();
        Matrix device = new RenderOptions().GetDeviceTransform(document.Pages[0]);
        backend.Render(list, canvas, device);
        Assert.Equal(1, backend.ImagesCreated);

        backend.Render(list, canvas, device);
        Assert.Equal(1, backend.ImagesCreated);

        // The image's pixels, and its resamplings to the device for the first transforms it is drawn at.
        Assert.InRange(backend.CachedImageBytes, 8 * 8 * 4, 8 * 8 * 4 * 1024);
    }

    [Fact]
    public void A_zero_image_budget_converts_every_paint_keeps_nothing_and_draws_the_same()
    {
        byte[] file = RepeatedImage(paints: 5, size: 8);
        using PdfDocument cached = PdfDocument.Open(file);
        using PdfDocument uncached = PdfDocument.Open(file, new PdfOptions().WithImageCacheBudget(0));
        using var backend = new SkiaBackend();
        using var uncachedBackend = new SkiaBackend();
        using SKBitmap expected = backend.Render(cached.Pages[0]);
        using SKBitmap actual = uncachedBackend.Render(uncached.Pages[0]);

        Assert.Equal(5, uncachedBackend.ImagesCreated);
        Assert.Equal(0, uncachedBackend.CachedImageBytes);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    [Fact]
    public void Large_images_stay_within_the_image_budget()
    {
        // Three distinct 64 x 64 images (16 KiB each as Skia pixels) under a 40 KiB budget: at most two are kept at a time.
        using PdfDocument document = PdfDocument.Open(DistinctImages(count: 3, size: 64), new PdfOptions().WithImageCacheBudget(40 * 1024));
        using var backend = new SkiaBackend();
        using (backend.Render(document.Pages[0]))
        {
            Assert.Equal(3, backend.ImagesCreated);
            Assert.InRange(backend.CachedImageBytes, 1, 40 * 1024);
        }
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
