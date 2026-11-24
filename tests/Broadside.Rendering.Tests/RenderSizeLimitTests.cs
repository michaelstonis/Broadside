using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The renderer's memory limit for untrusted pages (docs/threat-model.md): a page's size comes from the file, so a hostile
/// <c>MediaBox</c> must not make a render allocate an unbounded bitmap, and the caller can see the size before rendering.
/// </summary>
public sealed class RenderSizeLimitTests
{
    [Fact]
    public void A_page_too_large_for_a_bitmap_is_refused_before_anything_is_drawn_and_its_size_can_be_checked_first()
    {
        // ISO 32000-2 §14.11.2: the media box may be any rectangle; 200,000 points square is 4e10 pixels at 72 DPI.
        byte[] file = ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200000 200000] >>",
        ]);
        using PdfDocument document = PdfDocument.Open(file);
        var options = new RenderOptions();
        using var rasterizer = new SoftwareRasterizer();

        Assert.Throws<ArgumentException>(() => options.GetPixelSize(document.Pages[0]));
        Assert.Throws<ArgumentException>(() => rasterizer.Render(document.Pages[0], options));

        // A large page that a bitmap could hold reports its size, so the caller can refuse it or pick a smaller scale first.
        Assert.Equal(new PixelSize(2_000, 2_000), options.WithScale(0.01).GetPixelSize(document.Pages[0]));
    }
}
