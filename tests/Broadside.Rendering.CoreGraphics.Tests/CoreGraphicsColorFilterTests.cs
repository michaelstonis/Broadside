using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Smart dark mode on the CoreGraphics backend (spec #33 viewer capability 9): a page render with a color filter falls back to the
/// software path and is drawn into the bitmap context as an image, so it shows the software rasterizer's dark page.
/// </summary>
public sealed class CoreGraphicsColorFilterTests
{
    [Fact]
    public void A_page_rendered_in_dark_mode_is_the_software_rasterizer_s_dark_page()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("image-smask.pdf"));
        RenderOptions options = new RenderOptions().WithColorFilter(ColorFilter.DarkMode);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap reference = rasterizer.Render(document.Pages[0], options);

        GoldenImage render = BitmapRendering.Render(document.Pages[0], options);

        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(reference), render, GoldenThreshold.Default);
        Assert.True(comparison.Matches, comparison.Describe());
        Assert.Equal((byte)0, render[300, 400].R); // the paper is black
        Assert.Equal(GoldenImage.FromBitmap(reference)[120, 120], render[120, 120]); // an image pixel as rendered
    }
}
