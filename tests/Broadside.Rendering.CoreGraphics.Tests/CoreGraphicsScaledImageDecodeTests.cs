using Broadside.Graphics;
using Broadside.TestSupport;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Issue #428 part 3 (the CoreGraphics half of #362): a viewer's CoreGraphics renders convert a large image drawn small at the
/// reduction that keeps two samples per device pixel, each mask at its own, and stay within the page tolerance of the same backend
/// converting every sample (PSNR ≥ 35 dB, at most 4% of pixels more than 8 levels off); page renders, print and export keep
/// converting every sample.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class CoreGraphicsScaledImageDecodeTests
{
    [Theory]
    [InlineData(390, 1)]
    [InlineData(300, 2)]
    [InlineData(150, 3)]
    public void The_catalogue_cover_at_phone_widths_converts_a_reduction_within_the_page_tolerance(int width, int reduction)
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        double scale = width / 612.0;

        // The reduced render first: the document's samples serve a coarser request from a finer decode.
        (byte[] reduced, long reducedPixels, _) = Render(document, scale, scaled: true);
        (byte[] full, long fullPixels, _) = Render(document, scale, scaled: false);

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

        (byte[] reduced, _, _) = Render(document, 0.25, scaled: true);
        (byte[] full, _, _) = Render(document, 0.25, scaled: false);

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

        (byte[] reduced, long reducedPixels, _) = Render(document, 0.25, scaled: true);
        (byte[] full, long fullPixels, _) = Render(document, 0.25, scaled: false);

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
        // A strongly curved tint transform: the codec may average at most 1/2 before conversion; the backend resamples the rest after it.
        var codec = new ReducingImageFilter();
        const string space = "[/Separation /Spot /DeviceRGB << /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0.2 0.6 0.9] /N 2 >>]";
        using PdfDocument document = PdfDocument.Open(
            ReducingImageFilter.Page(ReducingImageFilter.Photo(1024, 1024, 1), 1024, 1024, space, ScaledImagePages.Paint),
            new PdfOptions().UseFilter(codec).WithDisplayListCacheBudget(0));

        (byte[] reduced, long pixels, _) = Render(document, 0.0625, scaled: true);
        (byte[] full, _, _) = Render(document, 0.0625, scaled: false);

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

        byte[] reduced = Page(document, options.WithScaledImageDecode(true), out long reducedPixels);
        byte[] full = Page(document, options.WithScaledImageDecode(false), out long fullPixels);

        Assert.Equal(fullPixels, reducedPixels);
        Assert.Equal(full, reduced);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void By_default_a_page_render_draws_as_with_scaled_decode_off(string page)
    {
        // Off by default: the same pixels as an explicit off, and as a list render with the renderer's toggle left off.
        using PdfDocument document = PdfDocument.Open(ScaledImagePages.Page(page), new PdfOptions().WithDisplayListCacheBudget(0));
        RenderOptions options = new RenderOptions().WithScale(0.25);

        byte[] byDefault = Page(document, options, out long defaultPixels);
        byte[] off = Page(document, new RenderOptions().WithScale(0.25).WithScaledImageDecode(false), out long offPixels);

        Assert.Equal(byDefault, off);
        Assert.Equal(defaultPixels, offPixels);
    }

    [Fact]
    public void Page_renders_convert_every_sample_unless_a_viewer_asks()
    {
        // Viewer-only: RenderOptions.ScaledImageDecode is off by default, as are a new renderer's list renders; print and export
        // never turn it on.
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        RenderOptions options = new RenderOptions().WithScale(0.25);

        Page(document, new RenderOptions().WithScale(0.25).WithScaledImageDecode(true), out long viewerPixels);
        Page(document, options, out long defaultPixels);
        using var renderer = new CoreGraphicsRenderer();

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

        (_, long one, _) = Render(document, 390 / 612.0, scaled: true, samplesPerPixel: 1);
        (_, long two, _) = Render(document, 390 / 612.0, scaled: true);

        Assert.Equal(1275L * 1575, two);
        Assert.Equal(638L * 788, one);
        using var renderer = new CoreGraphicsRenderer();
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.ImageSamplesPerPixel = 0.5);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.ImageSamplesPerPixel = double.NaN);
    }

    [Fact]
    public void The_converted_pixels_kept_are_the_reduced_size()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));

        (_, long converted, long kept) = Render(document, 150 / 612.0, scaled: true);

        // An eighth of 2550 x 3150, as BGRA, with the resamplings to the device kept beside it; never the full 32 MB.
        Assert.Equal(319L * 394, converted);
        Assert.InRange(kept, 319L * 394 * 4, (319L * 394 * 4) + (150L * 194 * 4 * 2));
    }

    [Fact]
    public void A_zoom_in_converts_the_new_reduction_once_for_every_tile_and_a_zoom_out_converts_nothing()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        using var renderer = new CoreGraphicsRenderer { ScaledImageDecode = true };
        using CGBitmapContext tile = Flipped(64, 64);

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
        using CGBitmapContext context = Flipped(size.Width, size.Height);
        using var renderer = new CoreGraphicsRenderer { ScaledImageDecode = true };
        Action render = () => renderer.Render(list, context, device);

        Assert.Equal(0, Allocations.Measure(render, warmUpCalls: 3));
    }

    private static int Reduce(int size, int reduction) => (size + (1 << reduction) - 1) >> reduction;

    /// <summary>A list render on white, as a viewer's tile is drawn: the pixels, those the renderer converted, and the bytes it keeps.</summary>
    private static (byte[] Pixels, long Converted, long Kept) Render(PdfDocument document, double scale, bool scaled, double samplesPerPixel = 2)
    {
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        using CGBitmapContext context = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] pixels, out int stride);
        using (var white = new CGColor(1, 1, 1, 1))
        {
            context.SetFillColor(white);
        }

        context.FillRect(new CGRect(0, 0, size.Width, size.Height));
        context.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, size.Height));
        using var renderer = new CoreGraphicsRenderer { ScaledImageDecode = scaled, ImageSamplesPerPixel = samplesPerPixel };
        renderer.Render(page.CreateDisplayList(), context, options.GetDeviceTransform(page));
        context.Flush();
        return (Rows(pixels, size, stride), renderer.ImagePixelsConverted, renderer.CachedImageBytes);
    }

    /// <summary>A page render through <see cref="RenderOptions"/>: its pixels, and the pixels the renderer converted.</summary>
    private static byte[] Page(PdfDocument document, RenderOptions options, out long converted)
    {
        PdfPage page = document.Pages[0];
        PixelSize size = options.GetPixelSize(page);
        using CGBitmapContext context = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] pixels, out int stride);
        using var renderer = new CoreGraphicsRenderer();
        renderer.Render(page, context, options);
        context.Flush();
        converted = renderer.ImagePixelsConverted;
        return Rows(pixels, size, stride);
    }

    /// <summary>The pixels without the rows' padding, so that the tolerance counts only the page's pixels.</summary>
    private static byte[] Rows(byte[] pixels, PixelSize size, int stride)
    {
        int row = size.Width * 4;
        byte[] packed = new byte[row * size.Height];
        for (int y = 0; y < size.Height; y++)
        {
            pixels.AsSpan(y * stride, row).CopyTo(packed.AsSpan(y * row));
        }

        return packed;
    }

    private static CGBitmapContext Flipped(int width, int height)
    {
        CGBitmapContext context = BitmapRendering.CreateContext(width, height, out _, out _);
        context.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, height));
        return context;
    }
}
