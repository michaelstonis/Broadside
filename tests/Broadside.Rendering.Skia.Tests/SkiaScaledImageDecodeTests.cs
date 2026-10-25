using Broadside.Graphics;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Issue #428 (the Skia half of #362): a viewer's Skia renders convert a large image drawn small at the reduction that keeps two
/// samples per device pixel, each mask at its own, and stay within the page tolerance of the same backend converting every sample
/// (PSNR ≥ 35 dB, at most 4% of pixels more than 8 levels off); page renders, print and export keep converting every sample.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class SkiaScaledImageDecodeTests
{
    [Theory]
    [InlineData(390, 1)]
    [InlineData(300, 2)]
    [InlineData(150, 3)]
    public void The_catalogue_cover_at_phone_widths_converts_a_reduction_within_the_page_tolerance(int width, int reduction)
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        double scale = width / 612.0;

        (byte[] reduced, long reducedPixels) = Render(document, scale, scaled: true);
        (byte[] full, long fullPixels) = Render(document, scale, scaled: false);

        Assert.Equal(2550L * 3150, fullPixels);
        Assert.Equal((long)Reduce(2550, reduction) * Reduce(3150, reduction), reducedPixels);
        ScaledImagePages.AssertWithinTolerance(full, reduced, $"cover at {width} px");
    }

    [Theory]
    [InlineData("dct-baseline.pdf")]
    [InlineData("dct-progressive.pdf")]
    [InlineData("dct-cmyk.pdf")]
    public void Corpus_DCT_pages_at_a_quarter_scale_stay_within_the_page_tolerance(string file)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file), new PdfOptions().WithDisplayListCacheBudget(0));

        (byte[] reduced, _) = Render(document, 0.25, scaled: true);
        (byte[] full, _) = Render(document, 0.25, scaled: false);

        ScaledImagePages.AssertWithinTolerance(full, reduced, file);
    }

    public static TheoryData<string> Pages => [.. ScaledImagePages.Names];

    [Theory]
    [MemberData(nameof(Pages))]
    public void Large_images_and_their_masks_at_a_quarter_scale_stay_within_the_page_tolerance(string page)
    {
        // Flate images decoded at build, reduced from that decode: depths, a stencil, soft masks of the image's size and another, an
        // explicit mask of another size, and a colour key (never reduced).
        using PdfDocument document = PdfDocument.Open(ScaledImagePages.Page(page), new PdfOptions().WithDisplayListCacheBudget(0));

        (byte[] reduced, long reducedPixels) = Render(document, 0.25, scaled: true);
        (byte[] full, long fullPixels) = Render(document, 0.25, scaled: false);

        ScaledImagePages.AssertWithinTolerance(full, reduced, page);
        if (page == "colour-key")
        {
            Assert.Equal(fullPixels, reducedPixels);
        }
        else
        {
            // The 1024 x 1024 image is drawn 128 pixels square: a quarter keeps two samples per pixel.
            Assert.True(reducedPixels + (1024L * 1024 * 3 / 4) <= fullPixels, $"{page}: {reducedPixels} pixels converted, {fullPixels} in full");
        }
    }

    [Fact]
    public void A_curved_colour_conversion_caps_the_codec_reduction_at_a_half()
    {
        // A strongly curved tint transform: the codec may average at most 1/2 before conversion; Skia resamples the rest after it.
        var codec = new ReducingImageFilter();
        const string space = "[/Separation /Spot /DeviceRGB << /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0.2 0.6 0.9] /N 2 >>]";
        using PdfDocument document = PdfDocument.Open(
            ReducingImageFilter.Page(ReducingImageFilter.Photo(1024, 1024, 1), 1024, 1024, space, ScaledImagePages.Paint),
            new PdfOptions().UseFilter(codec).WithDisplayListCacheBudget(0));

        (byte[] reduced, long pixels) = Render(document, 0.0625, scaled: true);
        (byte[] full, _) = Render(document, 0.0625, scaled: false);

        Assert.Equal(512L * 512, pixels);
        (double psnr, _, int max) = ScaledImagePages.Compare(full, reduced);
        Assert.True(psnr >= 45, $"PSNR {psnr:F1} dB");
        Assert.True(max <= 40, $"max {max}");
    }

    [Fact]
    public void A_render_at_one_to_one_never_reduces()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        RenderOptions options = new RenderOptions().WithScale(2550 / 612.0).WithRegion(new PdfRectangle(100, 100, 160, 160));

        using SKBitmap full = Page(document, options.WithScaledImageDecode(false), out _);
        using SKBitmap reduced = Page(document, options.WithScaledImageDecode(true), out _);

        Assert.Equal(full.Bytes, reduced.Bytes);
    }

    [Fact]
    public void Page_renders_convert_every_sample_unless_a_viewer_asks()
    {
        // Viewer-only: RenderOptions.ScaledImageDecode is off by default, as are a new backend's list renders; print and export
        // never turn it on.
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        RenderOptions options = new RenderOptions().WithScale(0.25);

        using SKBitmap viewer = Page(document, new RenderOptions().WithScale(0.25).WithScaledImageDecode(true), out long viewerPixels);
        using SKBitmap byDefault = Page(document, options, out long defaultPixels);
        using var backend = new SkiaBackend();

        Assert.False(options.ScaledImageDecode);
        Assert.False(backend.ScaledImageDecode);
        Assert.Equal(2, backend.ImageSamplesPerPixel);
        Assert.Equal(2550L * 3150, defaultPixels);
        Assert.Equal(638L * 788, viewerPixels); // 3150 samples over 198 pixels: a quarter keeps two per pixel
    }

    [Fact]
    public void One_sample_per_pixel_reduces_one_step_further()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));

        (_, long one) = Render(document, 390 / 612.0, scaled: true, samplesPerPixel: 1);
        (_, long two) = Render(document, 390 / 612.0, scaled: true);

        Assert.Equal(1275L * 1575, two);
        Assert.Equal(638L * 788, one);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SkiaBackend().ImageSamplesPerPixel = 0.5);
    }

    [Fact]
    public void A_zoom_converts_the_new_reduction_once_for_every_tile()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        using var backend = new SkiaBackend { ScaledImageDecode = true };
        using var tile = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(tile);
        foreach (double scale in new[] { 150 / 612.0, 390 / 612.0 })
        {
            Matrix device = new RenderOptions().WithScale(scale).GetDeviceTransform(page);
            for (int i = 0; i < 4; i++)
            {
                backend.Render(list, canvas, device * Matrix.CreateTranslation(-64 * i, -64 * i));
            }
        }

        Assert.Equal(2, backend.ImagesCreated);
        Assert.Equal((319L * 394) + (1275L * 1575), backend.ImagePixelsConverted);
    }

    [Fact]
    public void Replaying_a_list_whose_image_is_reduced_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(0.25);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var backend = new SkiaBackend { ScaledImageDecode = true };
        Action render = () => backend.Render(list, canvas, device);

        Assert.Equal(0, Allocations.Measure(render, warmUpCalls: 3));
    }

    private static int Reduce(int size, int reduction) => (size + (1 << reduction) - 1) >> reduction;

    /// <summary>A list render on white, as a viewer's tile is drawn, and the pixels the backend converted.</summary>
    private static (byte[] Pixels, long Converted) Render(PdfDocument document, double scale, bool scaled, double samplesPerPixel = 2)
    {
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var backend = new SkiaBackend { ScaledImageDecode = scaled, ImageSamplesPerPixel = samplesPerPixel };
        backend.Render(page.CreateDisplayList(), canvas, options.GetDeviceTransform(page));
        return (bitmap.Bytes, backend.ImagePixelsConverted);
    }

    private static SKBitmap Page(PdfDocument document, RenderOptions options, out long converted)
    {
        using var backend = new SkiaBackend();
        SKBitmap bitmap = backend.Render(document.Pages[0], options);
        converted = backend.ImagePixelsConverted;
        return bitmap;
    }
}
