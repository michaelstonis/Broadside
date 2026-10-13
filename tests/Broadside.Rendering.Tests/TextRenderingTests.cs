using System.Text;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Text through glyph outlines in the software rasterizer (ISO 32000-2 §9.2.4, §9.3.6, §9.4.4): each glyph's outline is filled,
/// stroked, both or neither as the text rendering mode says, at the place the text matrix gives it, inside the clip.
/// </summary>
public sealed class TextRenderingTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Black = new(0, 0, 0, 255);
    private static readonly Rgba Blue = new(51, 102, 204, 255);
    private static readonly Rgba Red = new(204, 26, 26, 255);

    [Fact]
    public void An_embedded_glyph_is_filled_where_its_outline_lies_and_nowhere_else()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-truetype-embedded.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        // H at 24 pt from (72, 700): its left stem spans x 74.4-79.2, its crossbar y 707.2-709.6 (device rows 82.4-84.8); between
        // the stems above and below the crossbar is empty.
        Assert.Equal(Black, bitmap.At(76, 84));
        Assert.Equal(Black, bitmap.At(81, 83));
        Assert.Equal(White, bitmap.At(81, 78));
        Assert.Equal(White, bitmap.At(81, 89));
        Assert.Equal(White, bitmap.At(76, 70));
    }

    [Fact]
    public void Each_text_rendering_mode_fills_strokes_both_or_neither()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-render-modes.pdf"));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        // The H's left stem spans x 44.8-54.4 at 48 pt; the 4-unit stroke covers 2 units either side of its edges.
        Assert.Equal(Blue, bitmap.At(49, 53));
        Assert.Equal(White, bitmap.At(49, 113));
        Assert.Equal(Red, bitmap.At(44, 113));
        Assert.Equal(Blue, bitmap.At(49, 173));
        Assert.Equal(Red, bitmap.At(44, 173));

        // Mode 3 paints nothing but advances: the I shown after the invisible HI starts at x 97.6 and paints its stem at 102.4-112.
        Assert.Equal(White, bitmap.At(49, 233));
        Assert.Equal(Blue, bitmap.At(107, 233));
    }

    [Fact]
    public void Glyphs_are_cut_by_the_clipping_path()
    {
        // Four A glyphs at 50 pt, 30 points apart, behind a clip that ends at x = 100: the first two paint, the last two do not.
        byte[] content = Encoding.ASCII.GetBytes("0 0 100 792 re W n BT /F1 50 Tf 50 400 Td (AAAA) Tj ET");
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(content));
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap bitmap = rasterizer.Render(document.Pages[0]);

        Assert.Equal(Black, bitmap.At(54, 389));
        Assert.Equal(Black, bitmap.At(84, 389));
        Assert.Equal(White, bitmap.At(114, 389));
        Assert.Equal(White, bitmap.At(144, 389));
    }

    [Fact]
    public void Forcing_outlines_is_off_by_default_and_changes_nothing_in_the_software_rasterizer_which_always_draws_them()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-render-modes.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        var options = new RenderOptions();
        Assert.False(options.ForceOutlines);

        using Bitmap native = rasterizer.Render(document.Pages[0], options);
        using Bitmap outlines = rasterizer.Render(document.Pages[0], options.WithForceOutlines(true));

        Assert.True(options.ForceOutlines);
        Assert.True(native.Pixels.SequenceEqual(outlines.Pixels));
    }
}
