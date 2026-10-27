using System.Diagnostics;
using System.Globalization;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Issue #428 part 5 (the Direct2D half of #362): a viewer's Direct2D renders convert a large image drawn small at the reduction that
/// keeps two samples per device pixel, each mask at its own, and stay within the page tolerance of the same renderer converting every
/// sample (PSNR ≥ 35 dB, at most 4% of pixels more than 8 levels off); page renders, print and export keep converting every sample.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class Direct2DScaledImageDecodeTests
{
    [Theory]
    [InlineData(390, 1)]
    [InlineData(300, 2)]
    [InlineData(150, 3)]
    public void The_catalogue_cover_at_phone_widths_converts_a_reduction_within_the_page_tolerance(int width, int reduction)
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        double scale = width / 612.0;

        // The reduced render first: a finer reduction the document keeps serves a coarser request. Each render decodes, converts and
        // draws the photograph once; the times (WARP on CI's Windows runner) are written to the test output for the benchmark notes.
        long start = Stopwatch.GetTimestamp();
        Rendered reduced = Render(document, scale, scaled: true);
        TimeSpan reducedTime = Stopwatch.GetElapsedTime(start);
        start = Stopwatch.GetTimestamp();
        Rendered full = Render(document, scale, scaled: false);
        TimeSpan fullTime = Stopwatch.GetElapsedTime(start);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"cover at {width} px: reduced 1/{1 << reduction} {reducedTime.TotalMilliseconds:F0} ms, {reduced.Kept / 1048576.0:F1} MB kept; full {fullTime.TotalMilliseconds:F0} ms, {full.Kept / 1048576.0:F1} MB kept"));

        Assert.Equal(2550L * 3150, full.Converted);
        Assert.Equal((long)Reduce(2550, reduction) * Reduce(3150, reduction), reduced.Converted);
        // The reduced conversion is what is kept (with its resampling to the device), never the full one.
        Assert.InRange(reduced.Kept, reduced.Converted * 4, (reduced.Converted * 4) + (reduced.Area * 4));
        ScaledImagePages.AssertWithinTolerance(full.Pixels, reduced.Pixels, $"cover at {width} px");
    }

    [Theory]
    [InlineData("dct-baseline.pdf")]
    [InlineData("dct-progressive.pdf")]
    [InlineData("dct-cmyk.pdf")]
    public void Corpus_DCT_pages_at_a_quarter_scale_stay_within_the_page_tolerance(string file)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file), new PdfOptions().WithDisplayListCacheBudget(0));

        Rendered reduced = Render(document, 0.25, scaled: true);
        Rendered full = Render(document, 0.25, scaled: false);

        ScaledImagePages.AssertWithinTolerance(full.Pixels, reduced.Pixels, file);
    }

    public static TheoryData<string> Pages => [.. ScaledImagePages.Names];

    [Theory]
    [MemberData(nameof(Pages))]
    public void Large_images_and_their_masks_at_a_quarter_scale_stay_within_the_page_tolerance(string page)
    {
        // Flate images decoded at build, reduced from that decode: depths, a stencil, soft masks of the image's size and another, an
        // explicit mask of another size, and a colour key (never reduced).
        using PdfDocument document = PdfDocument.Open(ScaledImagePages.Page(page), new PdfOptions().WithDisplayListCacheBudget(0));

        Rendered reduced = Render(document, 0.25, scaled: true);
        Rendered full = Render(document, 0.25, scaled: false);

        ScaledImagePages.AssertWithinTolerance(full.Pixels, reduced.Pixels, page);
        if (page == "colour-key")
        {
            Assert.Equal(full.Converted, reduced.Converted);
            Assert.Equal(full.Pixels, reduced.Pixels);
        }
        else
        {
            // The 1024 x 1024 image is drawn 128 pixels square: a quarter keeps two samples per pixel, and only that is kept.
            Assert.True(reduced.Converted + (1024L * 1024 * 3 / 4) <= full.Converted, $"{page}: {reduced.Converted} pixels converted, {full.Converted} in full");
            Assert.True(reduced.Kept < full.Kept, $"{page}: {reduced.Kept} bytes kept, {full.Kept} in full");
        }
    }

    [Fact]
    public void A_curved_colour_conversion_caps_the_codec_reduction_at_a_half()
    {
        // A strongly curved tint transform: the codec may average at most 1/2 before conversion; the renderer resamples the rest after it.
        var codec = new ReducingImageFilter();
        const string space = "[/Separation /Spot /DeviceRGB << /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0.2 0.6 0.9] /N 2 >>]";
        using PdfDocument document = PdfDocument.Open(
            ReducingImageFilter.Page(ReducingImageFilter.Photo(1024, 1024, 1), 1024, 1024, space, ScaledImagePages.Paint),
            new PdfOptions().UseFilter(codec).WithDisplayListCacheBudget(0));

        Rendered reduced = Render(document, 0.0625, scaled: true);
        Rendered full = Render(document, 0.0625, scaled: false);

        Assert.Equal(512L * 512, reduced.Converted);
        (double psnr, _, int max) = ScaledImagePages.Compare(full.Pixels, reduced.Pixels);
        Assert.True(psnr >= 45, $"PSNR {psnr:F1} dB");
        Assert.True(max <= 40, $"max {max}");
    }

    [Fact]
    public void A_render_at_one_to_one_never_reduces()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        RenderOptions options = new RenderOptions().WithScale(2550 / 612.0).WithRegion(new PdfRectangle(100, 100, 160, 160));

        using Bitmap full = Page(document, options.WithScaledImageDecode(false), out long fullPixels);
        using Bitmap reduced = Page(document, options.WithScaledImageDecode(true), out long reducedPixels);

        Assert.Equal(fullPixels, reducedPixels);
        Assert.Equal(full.Pixels.ToArray(), reduced.Pixels.ToArray());
    }

    [Fact]
    public void Page_renders_convert_every_sample_unless_a_viewer_asks()
    {
        // Viewer-only: RenderOptions.ScaledImageDecode is off by default, as are a new renderer's list renders; print and export
        // never turn it on.
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        RenderOptions options = new RenderOptions().WithScale(0.25);

        using Bitmap viewer = Page(document, new RenderOptions().WithScale(0.25).WithScaledImageDecode(true), out long viewerPixels);
        using Bitmap byDefault = Page(document, options, out long defaultPixels);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        Assert.False(options.ScaledImageDecode);
        Assert.False(renderer.ScaledImageDecode);
        Assert.Equal(2, renderer.ImageSamplesPerPixel);
        Assert.Equal(2550L * 3150, defaultPixels);
        Assert.Equal(638L * 788, viewerPixels); // 3150 samples over 198 pixels: a quarter keeps two per pixel
    }

    [Fact]
    public void One_sample_per_pixel_reduces_one_step_further()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));

        long one = Render(document, 390 / 612.0, scaled: true, samplesPerPixel: 1).Converted;
        long two = Render(document, 390 / 612.0, scaled: true).Converted;

        Assert.Equal(1275L * 1575, two);
        Assert.Equal(638L * 788, one);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.ImageSamplesPerPixel = 0.5);
    }

    [Fact]
    public void A_zoom_in_converts_the_new_reduction_once_for_every_tile_and_a_zoom_out_converts_nothing()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp) { ScaledImageDecode = true };
        using var tile = new Bitmap(64, 64);

        // Zoom in (1/8 to 1/2), then out again: the half conversion serves the eighth, as the document's samples do.
        foreach (double scale in new[] { 150 / 612.0, 390 / 612.0, 150 / 612.0 })
        {
            Matrix device = new RenderOptions().WithScale(scale).GetDeviceTransform(page);
            for (int i = 0; i < 4; i++)
            {
                renderer.Render(list, tile, device * Matrix.CreateTranslation(-64 * i, -64 * i));
            }
        }

        Assert.Equal(2, renderer.ImagesCreated);
        Assert.Equal((319L * 394) + (1275L * 1575), renderer.ImagePixelsConverted);
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
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp) { ScaledImageDecode = true };
        Action render = () => renderer.Render(list, bitmap, device);

        Assert.Equal(0, Allocations.Measure(render, warmUpCalls: 3));
    }

    private static int Reduce(int size, int reduction) => (size + (1 << reduction) - 1) >> reduction;

    /// <summary>A list render on white, as a viewer's tile is drawn, with the pixels the renderer converted and the bytes it keeps.</summary>
    private static Rendered Render(PdfDocument document, double scale, bool scaled, double samplesPerPixel = 2)
    {
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        bitmap.Pixels.Fill(0xFF);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp) { ScaledImageDecode = scaled, ImageSamplesPerPixel = samplesPerPixel };
        renderer.Render(page.CreateDisplayList(), bitmap, options.GetDeviceTransform(page));
        return new Rendered(Pixels(bitmap), renderer.ImagePixelsConverted, renderer.CachedImageBytes, (long)size.Width * size.Height);
    }

    private static Bitmap Page(PdfDocument document, RenderOptions options, out long converted)
    {
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        Bitmap bitmap = renderer.Render(document.Pages[0], options);
        converted = renderer.ImagePixelsConverted;
        return bitmap;
    }

    /// <summary>The bitmap's pixels, row by row without padding.</summary>
    private static byte[] Pixels(Bitmap bitmap)
    {
        int rowBytes = bitmap.Width * 4;
        byte[] pixels = new byte[rowBytes * bitmap.Height];
        for (int y = 0; y < bitmap.Height; y++)
        {
            bitmap.GetRow(y)[..rowBytes].CopyTo(pixels.AsSpan(y * rowBytes, rowBytes));
        }

        return pixels;
    }

    private readonly record struct Rendered(byte[] Pixels, long Converted, long Kept, long Area);
}
