using Broadside.GoldenImages;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// This project reaches the golden images the way every backend test does (issue #120): through the harness and public API only.
/// Broadside.Rendering grants this project no internals, so nothing here can lean on the software rasterizer's.
/// </summary>
public sealed class GoldenHarnessTests
{
    [Theory]
    [MemberData(nameof(PageRotationPages))]
    public void A_backend_sizes_its_surface_from_the_public_page_mapping_and_it_fits_the_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        PixelSize size = GoldenCorpus.CreateRenderOptions().GetPixelSize(document.Pages[page]);

        GoldenImage reference = GoldenCorpus.LoadReference(fileName, page);

        Assert.Equal((size.Width, size.Height), (reference.Width, reference.Height));
    }

    [Fact]
    public void A_render_handed_over_as_premultiplied_bgra_with_padded_rows_is_compared_with_the_golden_image()
    {
        // The layout of an SKBitmap of colour type Bgra8888 and alpha type Premul, with rows padded to a multiple of 64 bytes.
        GoldenImage reference = GoldenCorpus.LoadReference("page-rotation.pdf", 1);
        int stride = ((reference.Width * 4) + 63) / 64 * 64;
        byte[] pixels = new byte[stride * reference.Height];
        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = 0; x < reference.Width; x++)
            {
                (byte r, byte g, byte b) = reference[x, y];
                int offset = (y * stride) + (x * 4);
                pixels[offset] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = 255;
            }
        }

        GoldenCorpus.AssertMatchesReference(
            "skia",
            "page-rotation.pdf",
            1,
            GoldenImage.FromPixels(pixels, reference.Width, reference.Height, stride, GoldenPixelFormat.Bgra8Premultiplied));
    }

    public static TheoryData<string, int> PageRotationPages() => GoldenCorpus.PagesOf("page-rotation.pdf");
}
