using Broadside.Graphics;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #362: images decoded at the resolution they are drawn at. A display list keeps an image whose codec reduces encoded, reads
/// only its header when it is built, and decodes it when a render draws it, at the largest power-of-two reduction that still leaves
/// the asked-for samples per device pixel; each reduction is decoded once per image, kept in the document's image cache, and a
/// failed decode paints nothing and is reported once. Only viewers ask for it: page renders, print and extraction decode in full.
/// The codec here is <see cref="ReducingImageFilter"/>, an exact box average, so the rasterizer's geometry and the rule are
/// measured on their own.
/// </summary>
/// <remarks>In the non-parallel collection: the tests count decodes and cache entries, which another test's COS edit could make stale.</remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class ScaledImageDecodeTests
{
    // A 256 x 256 image over 512 x 512 points: 2 device pixels per sample per unit of scale, so 2 samples per pixel keep reduction
    // log2(1 / (4 · scale)).
    private const string Paint = "q 512 0 0 512 50 100 cm /Im0 Do Q";

    [Fact]
    public void Building_the_list_reads_the_header_and_decodes_nothing()
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(256, 256, 1), 256, 256);
        using (document)
        {
            DisplayList list = document.Pages[0].CreateDisplayList();
            var recorder = new RecordingBackend();
            list.Replay(recorder);

            Assert.Equal(0, codec.Decodes);
            DisplayImage image = Assert.Single(recorder.Images).Image;
            Assert.Equal((256, 256, 1, 8, 3), (image.Width, image.Height, image.Components, image.BitsPerComponent, image.MaxReduction));
            Assert.Equal(0, codec.Decodes);
        }
    }

    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(0.25, 0)]
    [InlineData(0.125, 1)]
    [InlineData(0.0625, 2)]
    [InlineData(0.03125, 3)]
    [InlineData(0.01, 3)]
    public void A_viewer_render_decodes_the_reduction_that_keeps_two_samples_per_pixel(double scale, int expected)
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(256, 256, 1), 256, 256);
        using (document)
        {
            RenderList(document, scale, scaled: true).Dispose();

            Assert.Equal(1, codec.Decodes);
            Assert.Equal(1, codec.DecodesAt(expected));
        }
    }

    [Fact]
    public void One_samples_per_pixel_reduces_one_step_further()
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(256, 256, 1), 256, 256);
        using (document)
        {
            RenderList(document, 0.125, scaled: true, samplesPerPixel: 1).Dispose();

            Assert.Equal(1, codec.DecodesAt(2));
        }
    }

    [Fact]
    public void One_list_replayed_at_two_scales_decodes_two_reductions_and_keeps_them()
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(256, 256, 1), 256, 256);
        using (document)
        {
            RenderList(document, 0.03125, scaled: true).Dispose();
            RenderList(document, 0.0625, scaled: true).Dispose();
            RenderList(document, 0.03125, scaled: true).Dispose();
            RenderList(document, 0.0625, scaled: true).Dispose();

            Assert.Equal(1, codec.DecodesAt(3));
            Assert.Equal(1, codec.DecodesAt(2));
            Assert.Equal(2, codec.Decodes);
        }
    }

    [Fact]
    public void A_finer_reduction_already_decoded_serves_a_coarser_request()
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(256, 256, 1), 256, 256);
        using (document)
        {
            RenderList(document, 0.0625, scaled: true).Dispose();
            RenderList(document, 0.03125, scaled: true).Dispose();

            Assert.Equal(1, codec.DecodesAt(2));
            Assert.Equal(1, codec.Decodes);
        }
    }

    [Fact]
    public void Concurrent_tile_replays_decode_each_reduction_once()
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(1024, 1024, 3), 1024, 1024, "/DeviceRGB");
        using (document)
        {
            DisplayList list = document.Pages[0].CreateDisplayList();
            Matrix device = new RenderOptions().WithScale(0.25).GetDeviceTransform(document.Pages[0]);
            using var start = new Barrier(8);
            Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
            {
                using var rasterizer = new SoftwareRasterizer { ScaledImageDecode = true };
                using var bitmap = new Bitmap(160, 200);
                start.SignalAndWait();
                rasterizer.Render(list, bitmap, device);
            });

            // 1024 samples over 512 points at a quarter scale: 8 samples per pixel, so a quarter keeps 2.
            Assert.Equal(1, codec.DecodesAt(2));
            Assert.Equal(1, codec.Decodes);
        }
    }

    [Fact]
    public void An_evicted_reduction_is_decoded_again()
    {
        // Two 256 x 256 images over 256 points at a quarter scale: each decodes at half (128 x 128, 16 KB), and the budget holds one,
        // so drawing the second evicts the first; once nothing holds it, the first is decoded again.
        byte[] samples = ReducingImageFilter.Photo(256, 256, 1);
        var codec = new ReducingImageFilter();
        string image = ContentSamples.Stream(
            System.Text.Encoding.Latin1.GetString(samples),
            "/Type /XObject /Subtype /Image /Width 256 /Height 256 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /BroadsideReduce");
        byte[] file = ContentSamples.OnePage(
            "q 256 0 0 256 0 0 cm /Im0 Do Q q 256 0 0 256 300 0 cm /Im1 Do Q"u8.ToArray(), "/XObject << /Im0 5 0 R /Im1 6 0 R >>", image, image);
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseFilter(codec).WithImageCacheBudget(20_000));

        RenderList(document, 0.25, scaled: true).Dispose();
        Collect();
        RenderList(document, 0.25, scaled: true).Dispose();

        // Im1 stayed in the cache; Im0 was evicted and collected, so it alone is decoded again (and evicts Im1 in turn, which is
        // still alive and goes back into the cache rather than being decoded a third time).
        Assert.Equal(3, codec.DecodesAt(1));
        Assert.True(document.CacheStatistics.Images.Evictions >= 2, $"{document.CacheStatistics.Images.Evictions} evictions");
    }

    [Fact]
    public void Samples_the_cache_will_not_keep_stay_with_the_image()
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(256, 256, 1), 256, 256, options: new PdfOptions().WithImageCacheBudget(0));
        using (document)
        {
            RenderList(document, 0.0625, scaled: true).Dispose();
            Collect();
            RenderList(document, 0.0625, scaled: true).Dispose();

            Assert.Equal(1, codec.DecodesAt(2));
        }
    }

    [Fact]
    public void A_decode_that_fails_when_drawn_paints_nothing_and_is_reported_once()
    {
        var codec = new ReducingImageFilter { Fails = true };
        using PdfDocument document = PdfDocument.Open(
            ReducingImageFilter.Page(ReducingImageFilter.Photo(256, 256, 1), 256, 256, "/DeviceGray", Paint), new PdfOptions().UseFilter(codec));

        DisplayList list = document.Pages[0].CreateDisplayList();
        Assert.DoesNotContain(document.Diagnostics, static diagnostic => diagnostic.Code == "TestDecodeFailed");
        using Bitmap first = RenderList(document, 0.0625, scaled: true);
        using Bitmap second = RenderList(document, 0.03125, scaled: true);
        using Bitmap full = RenderList(document, 0.0625, scaled: false);

        Assert.Single(document.Diagnostics, static diagnostic => diagnostic.Code == "TestDecodeFailed");
        Assert.Equal(1, codec.Decodes);
        Assert.All([first, second, full], static bitmap => Assert.All(bitmap.Pixels.ToArray(), static value => Assert.Equal(byte.MaxValue, value)));
        Assert.NotNull(list);
    }

    [Fact]
    public void Page_renders_print_and_extraction_decode_the_image_in_full_unless_a_viewer_asks()
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(256, 256, 1), 256, 256, options: new PdfOptions().WithImageCacheBudget(0).WithDisplayListCacheBudget(0));
        using (document)
        {
            using var rasterizer = new SoftwareRasterizer();
            using (rasterizer.Render(document.Pages[0], new RenderOptions().WithScale(0.0625)))
            {
                Assert.Equal(1, codec.DecodesAt(0));
            }

            using (rasterizer.Render(document.Pages[0], new RenderOptions().WithScale(0.0625).WithIntent(DisplayIntent.Print)))
            {
                Assert.Equal(2, codec.DecodesAt(0));
            }

            using (rasterizer.Render(document.Pages[0], new RenderOptions().WithScale(0.0625).WithScaledImageDecode(true).WithOverprintPreview(true)))
            {
                Assert.Equal(3, codec.DecodesAt(0));
            }

            using (DecodedImage extracted = document.Pages[0].GetImage("Im0")!.Decode()!)
            {
                Assert.Equal((256, 256, 0), (extracted.Width, extracted.Height, extracted.Reduction));
                Assert.Equal(4, codec.DecodesAt(0));
            }

            using (rasterizer.Render(document.Pages[0], new RenderOptions().WithScale(0.0625).WithScaledImageDecode(true)))
            {
                Assert.Equal(1, codec.DecodesAt(2));
                Assert.Equal(5, codec.Decodes);
            }
        }
    }

    [Theory]
    [InlineData("/DeviceGray", 1)]
    [InlineData("/DeviceRGB", 3)]
    public void A_reduced_page_stays_within_the_page_tolerance_of_a_full_decode(string space, int components)
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(1024, 1024, components), 1024, 1024, space);
        using (document)
        {
            // 1024 samples over 512 points at 0.25: a quarter keeps 2 samples per pixel.
            using Bitmap reduced = RenderList(document, 0.25, scaled: true);
            using Bitmap full = RenderList(document, 0.25, scaled: false);

            Assert.Equal(1, codec.DecodesAt(2));
            (double psnr, double over8) = Compare(reduced, full);
            Assert.True(psnr >= 35, $"PSNR {psnr:F1} dB");
            Assert.True(over8 <= 0.04, $"{over8:P2} of pixels over 8 levels");
        }
    }

    [Theory]
    [InlineData("q 300 0 0 200 50 100 cm /Im0 Do Q")]
    [InlineData("q 0 300 -200 0 400 100 cm /Im0 Do Q")]
    [InlineData("q 300 40 60 200 50 100 cm /Im0 Do Q")]
    public void A_reduction_of_a_size_it_does_not_divide_covers_exactly_the_unit_square(string content)
    {
        // 613 x 377 at an eighth is 77 x 48 samples, overhanging the image by 3 and 7 source samples: painted onto
        // [0, 77·8/613] x [0, 48·8/377] of the unit square, clipped at 1, it covers exactly what the full image covers.
        var codec = new ReducingImageFilter();
        using PdfDocument document = PdfDocument.Open(
            ReducingImageFilter.Page(ReducingImageFilter.Photo(613, 377, 1), 613, 377, "/DeviceGray", content), new PdfOptions().UseFilter(codec));

        using Bitmap reduced = RenderList(document, 0.04, scaled: true, transparent: true);
        using Bitmap full = RenderList(document, 0.04, scaled: false, transparent: true);

        Assert.Equal(1, codec.DecodesAt(3));
        for (int y = 0; y < full.Height; y++)
        {
            for (int x = 0; x < full.Width; x++)
            {
                Assert.True(full.At(x, y).A == reduced.At(x, y).A, $"alpha at ({x}, {y}): {full.At(x, y).A} full, {reduced.At(x, y).A} reduced");
            }
        }

        (double psnr, _) = Compare(reduced, full);
        Assert.True(psnr >= 30, $"PSNR {psnr:F1} dB");
    }

    [Fact]
    public void A_soft_mask_of_its_own_size_is_resampled_on_its_own_grid_under_a_reduced_image()
    {
        // The image reduces to a quarter; its 256 x 256 soft mask (raw, not reducible) stays whole and is sampled where the
        // reduced image's pixels fall.
        var codec = new ReducingImageFilter();
        byte[] mask = ReducingImageFilter.Photo(256, 256, 1);
        string image = ContentSamples.Stream(
            System.Text.Encoding.Latin1.GetString(ReducingImageFilter.Photo(1024, 1024, 3)),
            "/Type /XObject /Subtype /Image /Width 1024 /Height 1024 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /BroadsideReduce /SMask 6 0 R");
        string soft = ContentSamples.Stream(
            System.Text.Encoding.Latin1.GetString(mask), "/Type /XObject /Subtype /Image /Width 256 /Height 256 /ColorSpace /DeviceGray /BitsPerComponent 8");
        using PdfDocument document = PdfDocument.Open(
            ContentSamples.OnePage(System.Text.Encoding.ASCII.GetBytes(Paint), "/XObject << /Im0 5 0 R >>", image, soft), new PdfOptions().UseFilter(codec));

        using Bitmap reduced = RenderList(document, 0.25, scaled: true);
        using Bitmap full = RenderList(document, 0.25, scaled: false);

        Assert.Equal(1, codec.DecodesAt(2));
        (double psnr, double over8) = Compare(reduced, full);
        Assert.True(psnr >= 35, $"PSNR {psnr:F1} dB");
        Assert.True(over8 <= 0.04, $"{over8:P2} of pixels over 8 levels");
    }

    [Fact]
    public void The_reduction_follows_the_most_enlarged_direction_and_the_colour_conversion()
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(256, 256, 3), 256, 256, "/DeviceRGB");
        using (document)
        {
            var recorder = new RecordingBackend();
            document.Pages[0].CreateDisplayList().Replay(recorder);
            DisplayImage image = Assert.Single(recorder.Images).Image;
            PdfColorConverter rgb = document.GetColorConverter(image.ColorSpace);

            // 256 samples over 32 pixels: 8 per pixel, a quarter keeps 2; 1 sample per pixel allows an eighth.
            Assert.Equal(2, image.GetReduction(new Matrix(32, 0, 0, 32, 0, 0), 2, rgb));
            Assert.Equal(3, image.GetReduction(new Matrix(32, 0, 0, 32, 0, 0), 1, rgb));
            Assert.Equal(2, image.GetReduction(new Matrix(0, 32, -32, 0, 10, 10), 2, rgb));

            // Squeezed across, stretched down: the stretched direction decides.
            Assert.Equal(0, image.GetReduction(new Matrix(8, 0, 0, 256, 0, 0), 2, rgb));

            // A skew enlarges more than either axis alone.
            Assert.Equal(1, image.GetReduction(new Matrix(32, 0, 48, 32, 0, 0), 2, rgb));

            // Enlarged: never reduced; at most what the codec delivers.
            Assert.Equal(0, image.GetReduction(new Matrix(1000, 0, 0, 1000, 0, 0), 2, rgb));
            Assert.Equal(3, image.GetReduction(new Matrix(1, 0, 0, 1, 0, 0), 2, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => image.GetReduction(Matrix.Identity, 0.5, rgb));
            Assert.Equal(0, codec.Decodes);
        }
    }

    [Theory]
    [InlineData("Separation", 1, 1)]
    [InlineData("Indexed", 1, 0)]
    [InlineData("CalRGB", 3, 1)]
    [InlineData("Lab", 3, 1)]
    [InlineData("DeviceCMYK", 4, 1)]
    [InlineData("DeviceGray", 1, 3)]
    public void A_curved_conversion_caps_the_reduction_averaged_before_it(string family, int components, int expected)
    {
        string space = family switch
        {
            "Separation" => "[/Separation /Spot /DeviceRGB << /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 2 >>]",
            "Indexed" => "[/Indexed /DeviceGray 255 <00>]",
            "CalRGB" => "[/CalRGB << /WhitePoint [0.9505 1 1.089] /Gamma [2.2 2.2 2.2] >>]",
            "Lab" => "[/Lab << /WhitePoint [0.9505 1 1.089] >>]",
            _ => "/" + family,
        };
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(256, 256, components), 256, 256, space);
        using (document)
        {
            var recorder = new RecordingBackend();
            document.Pages[0].CreateDisplayList().Replay(recorder);
            DisplayImage image = Assert.Single(recorder.Images).Image;
            PdfColorConverter converter = document.GetColorConverter(image.ColorSpace);

            Assert.Equal(expected, image.GetReduction(new Matrix(8, 0, 0, 8, 0, 0), 2, converter));
            Assert.Equal(0, codec.Decodes);
        }
    }

    [Fact]
    public void A_colour_key_or_a_matte_keeps_the_image_at_its_own_resolution()
    {
        (PdfDocument keyed, _) = Open(ReducingImageFilter.Photo(256, 256, 1), 256, 256, extra: "/Mask [0 10]");
        using (keyed)
        {
            var recorder = new RecordingBackend();
            keyed.Pages[0].CreateDisplayList().Replay(recorder);
            DisplayImage image = Assert.Single(recorder.Images).Image;

            Assert.Equal(0, image.GetReduction(new Matrix(8, 0, 0, 8, 0, 0), 2, null));
        }
    }

    [Fact]
    public void Replaying_a_list_whose_reduced_image_is_decoded_allocates_nothing()
    {
        (PdfDocument document, ReducingImageFilter codec) = Open(ReducingImageFilter.Photo(1024, 1024, 3), 1024, 1024, "/DeviceRGB");
        using (document)
        {
            PdfPage page = document.Pages[0];
            DisplayList list = page.CreateDisplayList();
            RenderOptions options = new RenderOptions().WithScale(0.25);
            PixelSize size = options.GetPixelSize(page);
            Matrix device = options.GetDeviceTransform(page);
            using var bitmap = new Bitmap(size.Width, size.Height);
            using var rasterizer = new SoftwareRasterizer { ScaledImageDecode = true };
            Action render = () => rasterizer.Render(list, bitmap, device);

            long allocated = Allocations.Measure(render, warmUpCalls: 3);

            Assert.Equal(0, allocated);
            Assert.Equal(1, codec.DecodesAt(2));
        }
    }

    [Fact]
    public void The_options_default_to_full_resolution_and_two_samples_per_pixel()
    {
        var options = new RenderOptions();

        Assert.False(options.ScaledImageDecode);
        Assert.Equal(2, options.ImageSamplesPerPixel);
        Assert.Equal(1, options.WithImageSamplesPerPixel(1).ImageSamplesPerPixel);
        Assert.True(options.WithScaledImageDecode(true).ScaledImageDecode);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithImageSamplesPerPixel(0.99));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithImageSamplesPerPixel(double.PositiveInfinity));
        using var rasterizer = new SoftwareRasterizer();
        Assert.False(rasterizer.ScaledImageDecode);
        Assert.Throws<ArgumentOutOfRangeException>(() => rasterizer.ImageSamplesPerPixel = 0);
    }

    private static (PdfDocument Document, ReducingImageFilter Codec) Open(byte[] samples, int width, int height, string colorSpace = "/DeviceGray", PdfOptions? options = null, string extra = "")
    {
        var codec = new ReducingImageFilter();
        PdfDocument document = PdfDocument.Open(ReducingImageFilter.Page(samples, width, height, colorSpace, Paint, extra), (options ?? new PdfOptions()).UseFilter(codec));
        return (document, codec);
    }

    private static Bitmap RenderList(PdfDocument document, double scale, bool scaled, double samplesPerPixel = 2, bool transparent = false)
    {
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        var bitmap = new Bitmap(size.Width, size.Height);
        bitmap.Pixels.Fill(transparent ? (byte)0 : byte.MaxValue);
        using var rasterizer = new SoftwareRasterizer { ScaledImageDecode = scaled, ImageSamplesPerPixel = samplesPerPixel };
        rasterizer.Render(page.CreateDisplayList(), bitmap, options.GetDeviceTransform(page));
        return bitmap;
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>PSNR over the colour channels, and the share of pixels whose largest channel difference exceeds 8 levels.</summary>
    private static (double Psnr, double Over8) Compare(Bitmap a, Bitmap b)
    {
        ReadOnlySpan<byte> x = a.Pixels;
        ReadOnlySpan<byte> y = b.Pixels;
        double squared = 0;
        int over = 0;
        int pixels = x.Length / 4;
        for (int i = 0; i < x.Length; i += 4)
        {
            int largest = 0;
            for (int c = 0; c < 3; c++)
            {
                int difference = x[i + c] - y[i + c];
                squared += difference * difference;
                largest = Math.Max(largest, Math.Abs(difference));
            }

            over += largest > 8 ? 1 : 0;
        }

        double mse = squared / (pixels * 3.0);
        return (mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse), over / (double)pixels);
    }
}
