using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Text drawn on a page through <see cref="PdfCanvas"/> with non-embedded Standard 14 fonts: saved, reopened and run through the
/// content interpreter, it shows the drawn Unicode at the drawn positions; measurements follow the AFM files. ISO 32000-2 §9.2–9.4
/// (text objects, text state, positioning and showing), §9.6.2.2 (Standard 14 fonts) and Annex D (WinAnsiEncoding).
/// </summary>
public sealed class CanvasTextTests
{
    [Fact]
    public void Text_shown_in_helvetica_re_reads_with_the_drawn_unicode_at_the_positions_of_the_AFM_advances()
    {
        const string text = "Hello, World";
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(helvetica, 12);
            canvas.MoveText(72, 700);
            canvas.ShowText(text);
            canvas.EndText();
        }

        List<Glyph> glyphs = ReplayGlyphs(document);

        Assert.Equal(text, string.Concat(glyphs.Select(glyph => glyph.Unicode)));
        double[] advances = AfmReference.Read("Helvetica").Advances(text);
        double x = 72;
        for (int index = 0; index < text.Length; index++)
        {
            Assert.Equal(x, glyphs[index].X, 6);
            Assert.Equal(700, glyphs[index].Y, 6);
            Assert.Equal("Helvetica", glyphs[index].BaseFont);
            Assert.False(glyphs[index].IsEmbedded);
            x += advances[index] * 12 / 1000;
        }
    }

    /// <summary>Saves, reopens and records every glyph of the first page with its Unicode, origin and graphics state.</summary>
    private static List<Glyph> ReplayGlyphs(PdfDocument document)
    {
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        var recorder = new GlyphRecorder();
        reopened.Pages[0].ProcessContent(recorder);
        Assert.Empty(reopened.Diagnostics);
        return recorder.Glyphs;
    }

    internal sealed record Glyph(string Unicode, double X, double Y, string? BaseFont, bool IsEmbedded, GraphicsState State, Matrix TextMatrix);

    private sealed class GlyphRecorder : ContentProcessor
    {
        public List<Glyph> Glyphs { get; } = [];

        public override ContentEvents Events => ContentEvents.Text | ContentEvents.Glyphs | ContentEvents.Clips;

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
        {
            PathPoint origin = glyph.DeviceOrigin;
            Glyphs.Add(new Glyph(glyph.Unicode.ToString(), origin.X, origin.Y, glyph.Font?.BaseFont, glyph.Font?.IsEmbedded ?? false, context.State, glyph.TextMatrix));
        }
    }
}
