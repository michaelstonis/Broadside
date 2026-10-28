using System.Globalization;
using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Issue #432: the Skia backends of a viewer's pool, drawing one document at the same time, share the images they convert
/// (<see cref="SkiaBackend.ShareConvertedImages"/>): each image and mask is converted once whichever backend draws it, the pixels
/// kept stay within one image cache budget, a converted image is freed exactly once and never while a draw holds it, and the output
/// is the same as a backend's own.
/// </summary>
public sealed class SkiaSharedImageCacheTests
{
    private const int Backends = 8;

    [Fact]
    public void Backends_drawing_at_once_convert_an_image_and_its_mask_once_and_draw_what_a_backend_of_their_own_draws()
    {
        // A 1024 x 1024 image with a 700 x 500 soft mask drawn at its own resolution: two conversions, whichever backend makes them.
        using PdfDocument document = PdfDocument.Open(ScaledImagePages.Page("soft-mask-own-size"));
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content);
        RenderOptions options = new RenderOptions().WithScale(0.5);
        PixelSize size = options.GetPixelSize(document.Pages[0]);
        Matrix device = options.GetDeviceTransform(document.Pages[0]);
        byte[] expected;
        using (var own = new SkiaBackend { ScaledImageDecode = true })
        {
            expected = Render(own, list, size, device);
            Assert.Equal(2, own.ImagesCreated);
        }

        for (int round = 0; round < 5; round++)
        {
            SkiaBackend[] backends = [.. Enumerable.Range(0, Backends).Select(_ => new SkiaBackend { ScaledImageDecode = true, ShareConvertedImages = true })];
            try
            {
                using var start = new Barrier(Backends);
                byte[][] pixels = new byte[Backends][];
                Parallel.For(0, Backends, new ParallelOptions { MaxDegreeOfParallelism = Backends }, i =>
                {
                    start.SignalAndWait();
                    pixels[i] = Render(backends[i], list, size, device);
                });

                Assert.Equal(2, backends.Sum(b => b.ImagesCreated));
                SharedImageCache<ImageCache.Entry> shared = backends[0].SharedImages!;
                Assert.All(backends, b => Assert.Same(shared, b.SharedImages));
                Assert.Equal(2 * (round + 1), shared.Converted);
                Assert.All(pixels, p => Assert.Equal(expected, p));
            }
            finally
            {
                foreach (SkiaBackend backend in backends)
                {
                    backend.Dispose();
                }
            }
        }
    }

    [Fact]
    public void The_last_backend_to_leave_frees_every_shared_image_once()
    {
        using PdfDocument document = PdfDocument.Open(ScaledImagePages.Page("soft-mask-own-size"));
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content);
        RenderOptions options = new RenderOptions().WithScale(0.5);
        PixelSize size = options.GetPixelSize(document.Pages[0]);
        Matrix device = options.GetDeviceTransform(document.Pages[0]);
        using var first = new SkiaBackend { ShareConvertedImages = true };
        using var second = new SkiaBackend { ShareConvertedImages = true };
        Render(first, list, size, device);
        Render(second, list, size, device);
        SharedImageCache<ImageCache.Entry> shared = first.SharedImages!;
        ImageCache.Entry[] kept = [.. Kept(shared, list)];
        Assert.Equal(2, kept.Length);
        Assert.Equal((2, 0), (first.ImagesCreated, second.ImagesCreated));

        first.Dispose();
        Assert.All(kept, entry => Assert.False(entry.IsFreed));
        Assert.Equal(1, shared.Users);

        second.Dispose();
        Assert.All(kept, entry => Assert.True(entry.IsFreed));
        Assert.All(kept, entry => Assert.Null(entry.Image));
        Assert.Equal(0, shared.Bytes);
        Assert.Equal(0, shared.Users);
    }

    [Fact]
    public void Shared_images_stay_within_one_image_budget_for_every_backend()
    {
        // Six distinct 64 x 64 images (16 KiB each as Skia pixels) under a 40 KiB budget, drawn by eight backends at once: the
        // images kept for all of them stay within the budget once (a backend of its own keeps up to the budget each).
        const long budget = 40 * 1024;
        using PdfDocument document = PdfDocument.Open(DistinctImages(count: 6, size: 64), new PdfOptions().WithImageCacheBudget(budget));
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content);
        RenderOptions options = new();
        PixelSize size = options.GetPixelSize(document.Pages[0]);
        Matrix device = options.GetDeviceTransform(document.Pages[0]);
        byte[] expected;
        using (var own = new SkiaBackend())
        {
            expected = Render(own, list, size, device);
        }

        SkiaBackend[] backends = [.. Enumerable.Range(0, Backends).Select(_ => new SkiaBackend { ShareConvertedImages = true })];
        try
        {
            using var start = new Barrier(Backends);
            byte[][] pixels = new byte[Backends][];
            Parallel.For(0, Backends, new ParallelOptions { MaxDegreeOfParallelism = Backends }, i =>
            {
                start.SignalAndWait();
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    pixels[i] = Render(backends[i], list, size, device);
                }
            });

            SharedImageCache<ImageCache.Entry> shared = backends[0].SharedImages!;
            Assert.InRange(shared.Bytes, 1, budget);
            Assert.All(backends, b => Assert.Equal(0, b.CachedImageBytes));
            Assert.All(pixels, p => Assert.Equal(expected, p));
        }
        finally
        {
            foreach (SkiaBackend backend in backends)
            {
                backend.Dispose();
            }
        }
    }

    [Fact]
    public void A_backend_that_stops_sharing_leaves_and_converts_its_own_again()
    {
        using PdfDocument document = PdfDocument.Open(DistinctImages(count: 1, size: 8));
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content);
        RenderOptions options = new();
        PixelSize size = options.GetPixelSize(document.Pages[0]);
        Matrix device = options.GetDeviceTransform(document.Pages[0]);
        using var backend = new SkiaBackend { ShareConvertedImages = true };
        Render(backend, list, size, device);
        SharedImageCache<ImageCache.Entry> shared = backend.SharedImages!;
        Assert.Equal(1, shared.Users);

        backend.ShareConvertedImages = false;
        Assert.Null(backend.SharedImages);
        Assert.Equal(0, shared.Users);
        Render(backend, list, size, device);
        Assert.Equal(2, backend.ImagesCreated);
        Assert.True(backend.CachedImageBytes > 0);
    }

    private static IEnumerable<ImageCache.Entry> Kept(SharedImageCache<ImageCache.Entry> shared, DisplayList list)
    {
        foreach (DisplayImage image in Images(list))
        {
            if (shared.Lease(shared.SlotOf(image)) is { } entry)
            {
                shared.Release(entry);
                yield return entry;
            }

            if (image.SoftMask is { } mask && shared.Lease(shared.SlotOf(mask)) is { } masked)
            {
                shared.Release(masked);
                yield return masked;
            }
        }
    }

    private static List<DisplayImage> Images(DisplayList list)
    {
        var recording = new RecordingBackend();
        list.Replay(recording);
        return [.. recording.Images.Select(i => i.Image)];
    }

    private static byte[] Render(SkiaBackend backend, DisplayList list, PixelSize size, in Matrix device)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        backend.Render(list, canvas, device);
        return bitmap.Bytes;
    }

    private static byte[] DistinctImages(int count, int size)
    {
        var content = new StringBuilder();
        var names = new StringBuilder();
        string[] objects = new string[count];
        for (int i = 0; i < count; i++)
        {
            content.Append(CultureInfo.InvariantCulture, $"q 90 0 0 90 {10 + (i * 95)} 10 cm /Im{i} Do Q ");
            names.Append(CultureInfo.InvariantCulture, $"/Im{i} {5 + i} 0 R ");
            var data = new StringBuilder(size * size * 3);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    data.Append((char)((x * 255 / size) & 0xFF)).Append((char)((y * 255 / size) & 0xFF)).Append((char)((i * 40) & 0xFF));
                }
            }

            objects[i] = ContentSamples.Stream(data.ToString(), $"/Type /XObject /Subtype /Image /Width {size} /Height {size} /ColorSpace /DeviceRGB /BitsPerComponent 8");
        }

        return ContentSamples.OnePage(Encoding.Latin1.GetBytes(content.ToString()), $"/XObject << {names}>>", objects);
    }
}
