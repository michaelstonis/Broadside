using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Type 3 glyphs in the software rasterizer (ISO 32000-2 §9.6.4, Tables 110 and 111): each glyph's description paints in glyph space
/// through its font matrix, <c>d1</c> glyphs in the text's colour and <c>d0</c> glyphs in their own.
/// </summary>
public sealed class Type3RenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Blue = new(0, 0, 255, 255);
    private static readonly Rgba Red = new(255, 0, 0, 255);

    [Fact]
    public void Two_fonts_that_differ_only_in_their_glyph_space_units_render_identically()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        // (abc) in T3a (FontMatrix 0.001) on the baseline y = 700 (device rows 74-92) and in T3b (FontMatrix 0.01, every number
        // divided by 10) on y = 650, 50 rows lower: the same pixels.
        for (int row = 70; row < 96; row++)
        {
            Assert.True(bitmap.GetRow(row).SequenceEqual(bitmap.GetRow(row + 50)), $"Row {row} differs from row {row + 50}.");
        }

        Assert.Contains(bitmap.GetRow(83).ToArray(), static value => value != 255);
    }

    [Fact]
    public void A_d1_glyph_paints_in_the_text_colour_a_d0_glyph_in_its_own_and_the_page_form_nowhere()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        // a: an 18-point square from (72, 700) in the text's blue (its own green and its form's red are ignored); b: a red triangle
        // from x = 96 with its apex at (105, 718); c: an 8x1 image mask over 18 points from x = 120, its samples 1 0 1 0 1 0 1 0
        // painting the 0s (Decode [0 1]): bars at 122.25-124.5, 126.75-129, ... in blue.
        Assert.Equal(Blue, bitmap.At(75, 80));
        Assert.Equal(Blue, bitmap.At(88, 90));
        Assert.Equal(Red, bitmap.At(105, 88));
        Assert.Equal(White, bitmap.At(97, 76));
        Assert.Equal(White, bitmap.At(121, 83));
        Assert.Equal(Blue, bitmap.At(123, 83));
        Assert.Equal(White, bitmap.At(125, 83));
        Assert.Equal(Blue, bitmap.At(127, 83));

        // The page's own /Fm0 would paint the whole page black.
        Assert.Equal(White, bitmap.At(300, 400));
    }
}
