using Broadside.GoldenImages;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Smart dark mode on the Skia backend (spec #33 viewer capability 9): a page render with a color filter falls back to the software
/// path, so it has the software rasterizer's pixels exactly, and the render without a filter is the backend's own as before.
/// </summary>
public sealed class SkiaColorFilterTests
{
    [Theory]
    [InlineData("image-smask.pdf")]
    [InlineData("paths-fill-rules.pdf")]
    public void A_page_rendered_in_dark_mode_is_the_software_rasterizer_s_dark_page(string file)
    {
        using PdfDocument document = GoldenCorpus.Open(file);
        RenderOptions options = new RenderOptions().WithColorFilter(ColorFilter.DarkMode);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(document.Pages[0], options);
        using var backend = new SkiaBackend();

        using SKBitmap dark = backend.Render(document.Pages[0], options);

        Assert.Equal(expected.Width, dark.Width);
        Assert.Equal(expected.Height, dark.Height);
        for (int y = 0; y < expected.Height; y++)
        {
            Assert.Equal(expected.GetRow(y)[..(expected.Width * 4)].ToArray(), dark.GetPixelSpan().Slice(y * dark.RowBytes, expected.Width * 4).ToArray());
        }

        Assert.Empty(backend.Diagnostics);
    }

    [Fact]
    public void Without_a_filter_the_page_is_white_paper_as_before()
    {
        using PdfDocument document = GoldenCorpus.Open("paths-fill-rules.pdf");
        using var backend = new SkiaBackend();

        using SKBitmap light = backend.Render(document.Pages[0], new RenderOptions().WithColorFilter(ColorFilter.None));

        Assert.Equal(SKColors.White, light.GetPixel(0, 0));
    }
}
