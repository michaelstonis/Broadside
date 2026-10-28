using System.Globalization;
using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #432: software rasterizers that draw one document at the same time (a viewer's pool of tile backends) and share the images
/// they convert (<see cref="SoftwareRasterizer.ShareConvertedImages"/>) convert each image once, keep the converted pixels within one
/// image cache budget, and draw exactly what a rasterizer converting its own draws.
/// </summary>
public sealed class SharedRasterizerImageTests
{
    private const int Rasterizers = 8;

    [Fact]
    public void Rasterizers_drawing_at_once_convert_an_image_once_and_draw_what_a_rasterizer_of_their_own_draws()
    {
        // A 1024 x 1024 image with a soft mask of its size, folded into the converted pixels, drawn at half its resolution.
        using PdfDocument document = PdfDocument.Open(ScaledImagePages.Page("soft-mask-same-size"));
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content);
        RenderOptions options = new RenderOptions().WithScale(0.5);
        PixelSize size = options.GetPixelSize(document.Pages[0]);
        Matrix device = options.GetDeviceTransform(document.Pages[0]);
        byte[] expected;
        using (var own = new SoftwareRasterizer { ScaledImageDecode = true })
        {
            expected = Render(own, list, size, device);
        }

        for (int round = 0; round < 5; round++)
        {
            SoftwareRasterizer[] rasterizers = [.. Enumerable.Range(0, Rasterizers).Select(_ => new SoftwareRasterizer { ScaledImageDecode = true, ShareConvertedImages = true })];
            try
            {
                using var start = new Barrier(Rasterizers);
                byte[][] pixels = new byte[Rasterizers][];
                Parallel.For(0, Rasterizers, new ParallelOptions { MaxDegreeOfParallelism = Rasterizers }, i =>
                {
                    start.SignalAndWait();
                    pixels[i] = Render(rasterizers[i], list, size, device);
                });

                SharedImageCache<SharedPixels> shared = rasterizers[0].SharedImages!;
                Assert.All(rasterizers, r => Assert.Same(shared, r.SharedImages));
                Assert.Equal(round + 1, shared.Converted);
                Assert.Equal(512L * 512 * 4, shared.Bytes); // reduced to a half: two samples per pixel
                Assert.All(pixels, p => Assert.Equal(expected, p));
            }
            finally
            {
                foreach (SoftwareRasterizer rasterizer in rasterizers)
                {
                    rasterizer.Dispose();
                }
            }

            Assert.Equal(0, rasterizers[0].SharedImages?.Bytes ?? 0);
        }
    }

    [Fact]
    public void Shared_pixels_stay_within_one_image_budget_and_leave_with_the_last_rasterizer()
    {
        // Six distinct 64 x 64 images (16 KiB each as pixels) under a 40 KiB budget, drawn by eight rasterizers at once.
        const long budget = 40 * 1024;
        using PdfDocument document = PdfDocument.Open(DistinctImages(count: 6, size: 64), new PdfOptions().WithImageCacheBudget(budget));
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content);
        RenderOptions options = new();
        PixelSize size = options.GetPixelSize(document.Pages[0]);
        Matrix device = options.GetDeviceTransform(document.Pages[0]);
        byte[] expected;
        using (var own = new SoftwareRasterizer())
        {
            expected = Render(own, list, size, device);
        }

        SoftwareRasterizer[] rasterizers = [.. Enumerable.Range(0, Rasterizers).Select(_ => new SoftwareRasterizer { ShareConvertedImages = true })];
        SharedImageCache<SharedPixels>? shared = null;
        try
        {
            using var start = new Barrier(Rasterizers);
            byte[][] pixels = new byte[Rasterizers][];
            Parallel.For(0, Rasterizers, new ParallelOptions { MaxDegreeOfParallelism = Rasterizers }, i =>
            {
                start.SignalAndWait();
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    pixels[i] = Render(rasterizers[i], list, size, device);
                }
            });

            shared = rasterizers[0].SharedImages!;
            Assert.InRange(shared.Bytes, 1, budget);
            Assert.All(pixels, p => Assert.Equal(expected, p));
        }
        finally
        {
            foreach (SoftwareRasterizer rasterizer in rasterizers)
            {
                rasterizer.Dispose();
            }
        }

        Assert.Equal(0, shared.Users);
        Assert.Equal(0, shared.Bytes);
    }

    [Fact]
    public void A_zero_image_budget_keeps_each_rasterizer_converting_into_its_own_buffers()
    {
        using PdfDocument document = PdfDocument.Open(DistinctImages(count: 1, size: 8), new PdfOptions().WithImageCacheBudget(0));
        DisplayList list = document.Pages[0].CreateDisplayList(DisplayLayers.Content);
        RenderOptions options = new();
        using var rasterizer = new SoftwareRasterizer { ShareConvertedImages = true };
        Render(rasterizer, list, options.GetPixelSize(document.Pages[0]), options.GetDeviceTransform(document.Pages[0]));
        Assert.Equal(0, rasterizer.SharedImages?.Converted ?? 0);
    }

    private static byte[] Render(SoftwareRasterizer rasterizer, DisplayList list, PixelSize size, in Matrix device)
    {
        using var bitmap = new Bitmap(size.Width, size.Height);
        bitmap.Pixels.Fill(byte.MaxValue);
        rasterizer.Render(list, bitmap, device);
        return bitmap.Pixels.ToArray();
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
