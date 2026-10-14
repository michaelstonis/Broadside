using System.Text;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Text clipping modes in the software rasterizer (ISO 32000-2 §9.3.6, §8.5.4): the glyphs a text object shows in modes 4 to 7 are
/// painted as the mode says and then, at the end of the text object, cut the clip down to their outlines, so what is painted
/// afterwards shows only through the glyphs.
/// </summary>
public sealed class TextClipRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Red = new(204, 26, 26, 255);
    private static readonly Rgba Green = new(26, 179, 51, 255);
    private static readonly Rgba Yellow = new(242, 204, 26, 255);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Each_clipping_mode_page_of_text_render_modes_pdf_shows_the_image_only_inside_the_glyphs(int page)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-render-modes.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[page]);

        // HI at 120 pt from (30, 100) on a 300 x 300 page: the H's left stem covers device x 42-66, rows 116-200; its right stem
        // x 78-102; the I's stem x 138-162 (I starts at 30 + 96). The 6 x 6 checkerboard has 50-pixel cells, green where the cell's
        // column plus row is even. The image is painted over the fill (modes 4 and 6), so the stems' insides are the image's.
        Assert.Equal(Green, bitmap.At(54, 160));
        Assert.Equal(Yellow, bitmap.At(54, 140));
        Assert.Equal(Green, bitmap.At(150, 190));

        // Outside the glyphs nothing of the image shows: between the H's stems above its crossbar, right of the I, above the text.
        Assert.Equal(White, bitmap.At(72, 125));
        Assert.Equal(White, bitmap.At(220, 160));
        Assert.Equal(White, bitmap.At(54, 60));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void A_stroking_clip_mode_strokes_the_glyphs_before_they_clip_so_the_stroke_outside_the_outline_stays(int page)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-render-modes.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[page]);

        // The 4-unit red stroke reaches 2 units outside the H's left edge at x = 42: the pixel x 40-41 is stroke only.
        Assert.Equal(Red, bitmap.At(40, 160));
        Assert.Equal(White, bitmap.At(37, 160));
    }

    [Theory]
    [InlineData("BT /F1 50 Tf 7 Tr ET")]
    [InlineData("BT /F1 50 Tf 7 Tr 100 400 Td (  ) Tj ET")]
    public void A_clipping_text_object_without_glyph_outlines_leaves_the_clip_unchanged(string text)
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Encoding.ASCII.GetBytes(text + " 1 0 0 rg 0 0 612 792 re f")));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        var red = new Rgba(255, 0, 0, 255);
        Assert.Equal(red, bitmap.At(0, 0));
        Assert.Equal(red, bitmap.At(300, 400));
        Assert.Equal(red, bitmap.At(611, 791));
    }

    [Fact]
    public void Overlapping_glyphs_of_one_text_clip_union_under_the_nonzero_rule()
    {
        // Two B boxes (x 50-280 of a 600-unit advance) at 100 pt, the second pulled back by 500 units: x 5-28 and 15-38, rows
        // 346-392. Both are wound the same way, so their overlap (winding number 2) stays inside the clip, as even-odd would not.
        byte[] content = Encoding.ASCII.GetBytes("BT /F1 100 Tf 7 Tr 0 400 Td [(B) 500 (B)] TJ ET 1 0 0 rg 0 0 612 792 re f");
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(content));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        var red = new Rgba(255, 0, 0, 255);
        Assert.Equal(red, bitmap.At(10, 370));
        Assert.Equal(red, bitmap.At(20, 370));
        Assert.Equal(red, bitmap.At(33, 370));
        Assert.Equal(White, bitmap.At(45, 370));
        Assert.Equal(White, bitmap.At(20, 330));
    }
}
