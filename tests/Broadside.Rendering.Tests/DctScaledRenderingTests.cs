using Broadside.Filters;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #362, part 2: pages whose DCT images a viewer decodes at 1/2, 1/4 or 1/8 stay within the page tolerance of the same page
/// decoded in full (PSNR ≥ 35 dB, at most 4% of pixels more than 8 levels off, at two samples per device pixel), and cover exactly
/// the same pixels, including a 2550 x 3150 photograph that no reduction divides.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DctScaledRenderingTests
{
    [Theory]
    [InlineData(390, 1)]
    [InlineData(300, 2)]
    [InlineData(150, 3)]
    public void The_catalogue_cover_at_phone_widths_stays_within_the_page_tolerance(int width, int reduction)
    {
        // 2550 samples over the page's 612 points: at 300 pixels, 8.5 samples per pixel, so a quarter keeps 2.1.
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        RenderOptions options = new RenderOptions().WithScale(width / 612.0).WithTransparentBackground(true);

        using Bitmap reduced = Render(document, options.WithScaledImageDecode(true));
        using Bitmap full = Render(document, options.WithScaledImageDecode(false));

        Assert.Equal(reduction, ReductionDrawn(document, width / 612.0));
        AssertCoverage(full, reduced);
        AssertWithinTolerance(full, reduced, 35, 0.04);
    }

    [Theory]
    [InlineData("dct-baseline.pdf")]
    [InlineData("dct-progressive.pdf")]
    [InlineData("dct-cmyk.pdf")]
    public void Corpus_DCT_pages_at_a_quarter_scale_stay_within_the_page_tolerance(string file)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file), new PdfOptions().WithDisplayListCacheBudget(0));
        RenderOptions options = new RenderOptions().WithScale(0.25);

        using Bitmap reduced = Render(document, options.WithScaledImageDecode(true));
        using Bitmap full = Render(document, options.WithScaledImageDecode(false));

        AssertWithinTolerance(full, reduced, 35, 0.04);
    }

    [Fact]
    public void A_render_at_one_to_one_never_reduces()
    {
        using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
        RenderOptions options = new RenderOptions().WithScale(2550 / 612.0).WithRegion(new PdfRectangle(100, 100, 160, 160));

        using Bitmap reduced = Render(document, options.WithScaledImageDecode(true));
        using Bitmap full = Render(document, options.WithScaledImageDecode(false));

        Assert.Equal(full.Pixels.ToArray(), reduced.Pixels.ToArray());
    }

    private static Bitmap Render(PdfDocument document, RenderOptions options)
    {
        using var rasterizer = new SoftwareRasterizer();
        return rasterizer.Render(document.Pages[0], options);
    }

    /// <summary>The reduction a viewer decodes the first page's image at, at <paramref name="scale"/>.</summary>
    private static int ReductionDrawn(PdfDocument document, double scale)
    {
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        (DisplayImage image, PdfDefaultColorSpaces _, ColorConversion conversion) = Assert.Single(recorder.Images);
        Matrix device = new RenderOptions().WithScale(scale).GetDeviceTransform(document.Pages[0]);
        Matrix transform = new Matrix(612, 0, 0, 792, 0, 0) * device; // the cover fills the page
        return image.GetReduction(transform, 2, document.GetColorConverter(image.ColorSpace, conversion with { Target = DeviceColorModel.Rgb }));
    }

    private static void AssertCoverage(Bitmap full, Bitmap reduced)
    {
        ReadOnlySpan<byte> a = full.Pixels;
        ReadOnlySpan<byte> b = reduced.Pixels;
        for (int i = 3; i < a.Length; i += 4)
        {
            Assert.True(a[i] == b[i], $"alpha differs at byte {i}: {a[i]} full, {b[i]} reduced");
        }
    }

    private static void AssertWithinTolerance(Bitmap full, Bitmap reduced, double minimumPsnr, double maximumOver8)
    {
        ReadOnlySpan<byte> x = full.Pixels;
        ReadOnlySpan<byte> y = reduced.Pixels;
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
        double psnr = mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);
        Assert.True(psnr >= minimumPsnr, $"PSNR {psnr:F1} dB");
        Assert.True(over / (double)pixels <= maximumOver8, $"{over / (double)pixels:P2} of pixels over 8 levels");
    }
}
