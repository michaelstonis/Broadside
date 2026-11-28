using Broadside.Graphics;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Sanitizing invisible text (issue #648): text-showing operators drawn in rendering mode 3 are removed when
/// <see cref="PdfSanitizeOptions.RemoveInvisibleText"/> asks, each replaced by a move of the same length so the text after it stays
/// where it was; mode 7 adds to the clip and stays; by default an OCR layer is kept. ISO 32000-2 §9.3.6, Table 104; §9.4.4.
/// </summary>
public sealed class SanitizeInvisibleTextTests
{
    [Fact]
    public void Mode_3_text_is_removed_and_the_text_after_it_does_not_move()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("text-render-modes.pdf"));
        TextGlyph[] before = [.. document.Pages[0].ExtractText().Glyphs];
        TextGlyph last = before[^1];
        Assert.Equal(TextRenderingMode.Fill, last.RenderingMode);

        PdfSanitization result = document.Sanitize(new PdfSanitizeOptions().WithRemoveInvisibleText(true));

        Assert.Contains("invisible text operators (rendering mode 3): 1", result.Removed);
        using PdfDocument reopened = PdfDocument.Open(Save(document, result.CreateSaveOptions()));
        TextGlyph[] after = [.. reopened.Pages[0].ExtractText().Glyphs];
        Assert.Equal(before.Length - 2, after.Length);
        Assert.DoesNotContain(after, glyph => glyph.RenderingMode == TextRenderingMode.Invisible);
        Assert.Equal(last.Origin.X, after[^1].Origin.X, 6);
        Assert.Equal(last.Origin.Y, after[^1].Origin.Y, 6);
        Assert.Equal(97.6, after[^1].Origin.X, 6);
    }

    [Fact]
    public void Mode_7_text_adds_to_the_clip_and_is_kept()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("text-render-modes.pdf"));
        int clipping = document.Pages[4].ExtractText().Glyphs.Count;
        Assert.All(document.Pages[4].ExtractText().Glyphs, glyph => Assert.Equal(TextRenderingMode.Clip, glyph.RenderingMode));

        document.Sanitize(new PdfSanitizeOptions().WithRemoveInvisibleText(true));

        Assert.Equal(clipping, document.Pages[4].ExtractText().Glyphs.Count);
    }

    [Fact]
    public void An_ocr_layer_is_kept_by_default()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("text-render-modes.pdf"));
        int invisible = document.Pages[0].ExtractText().Glyphs.Count(glyph => glyph.RenderingMode == TextRenderingMode.Invisible);
        Assert.Equal(2, invisible);

        PdfSanitization result = document.Sanitize();

        Assert.DoesNotContain(result.Removed, line => line.StartsWith("invisible text", StringComparison.Ordinal));
        Assert.Equal(invisible, document.Pages[0].ExtractText().Glyphs.Count(glyph => glyph.RenderingMode == TextRenderingMode.Invisible));
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }
}
