using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Writing;

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

    [Fact]
    public void Every_text_state_parameter_replays_in_the_graphics_state_of_the_shown_glyphs()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFont(document.GetStandard14Font(Standard14Font.TimesRoman), 10);
            canvas.BeginText();
            canvas.SetCharacterSpacing(1.5);
            canvas.SetWordSpacing(3);
            canvas.SetHorizontalScaling(80);
            canvas.SetTextLeading(14);
            canvas.SetTextRise(2);
            canvas.SetTextRenderingMode(TextRenderingMode.FillStroke);
            canvas.SetFillColor(PdfColor.FromRgb(1, 0, 0));
            canvas.SetLineWidth(0.5);
            canvas.ShowText("A");
            canvas.EndText();
        }

        GraphicsState state = Assert.Single(ReplayGlyphs(document)).State;
        Assert.Equal((1.5, 3.0, 0.8, 14.0, 2.0, TextRenderingMode.FillStroke, 10.0), (state.CharacterSpacing, state.WordSpacing,
            state.HorizontalScaling, state.Leading, state.TextRise, state.TextRenderingMode, state.FontSize));
        Assert.Equal("Times-Roman", state.Font!.BaseFont);
        Assert.Equal(0.5, state.LineWidth);
    }

    [Fact]
    public void Restoring_the_graphics_state_restores_the_font_on_the_canvas_and_in_the_file()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);
        PdfCanvasFont courier = document.GetStandard14Font(Standard14Font.Courier);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFont(helvetica, 12);
            canvas.SaveState();
            canvas.SetFont(courier, 9);
            Assert.Equal((courier, 9.0), (canvas.Font, canvas.FontSize));
            canvas.BeginText();
            canvas.ShowText("C");
            canvas.EndText();
            canvas.RestoreState();
            Assert.Equal((helvetica, 12.0), (canvas.Font, canvas.FontSize));
            canvas.BeginText();
            canvas.ShowText("H");
            canvas.EndText();
        }

        List<Glyph> glyphs = ReplayGlyphs(document);
        Assert.Equal([("Courier", 9.0), ("Helvetica", 12.0)], glyphs.Select(glyph => (glyph.BaseFont, glyph.State.FontSize)));
    }

    [Fact]
    public void Every_positioning_operator_puts_the_next_glyph_where_the_text_line_matrix_says()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.Transform(Matrix.CreateTranslation(5, 7));
            canvas.BeginText();
            Assert.True(canvas.IsInTextObject);
            canvas.SetFont(document.GetStandard14Font(Standard14Font.Helvetica), 10);
            canvas.SetTextLeading(12);
            canvas.MoveText(100, 700);
            canvas.ShowText("A");
            canvas.MoveToNextLine();
            canvas.ShowText("B");
            canvas.MoveTextAndSetLeading(10, -20);
            canvas.ShowText("C");
            canvas.ShowTextOnNextLine("D");
            canvas.ShowTextOnNextLine("E", 2, 1);
            canvas.SetTextMatrix(new Matrix(2, 0, 0, 2, 300, 400));
            canvas.ShowText("F");
            canvas.MoveText(10, 10);
            canvas.ShowText("G");
            canvas.EndText();
            Assert.False(canvas.IsInTextObject);
        }

        Dictionary<string, Glyph> glyphs = ReplayGlyphs(document).ToDictionary(glyph => glyph.Unicode);
        Assert.Equal((105, 707), Origin(glyphs["A"]));
        Assert.Equal((105, 695), Origin(glyphs["B"]));
        Assert.Equal((115, 675), Origin(glyphs["C"]));
        Assert.Equal(20, glyphs["C"].State.Leading);
        Assert.Equal((115, 655), Origin(glyphs["D"]));
        Assert.Equal((115, 635), Origin(glyphs["E"]));
        Assert.Equal((2.0, 1.0), (glyphs["E"].State.WordSpacing, glyphs["E"].State.CharacterSpacing));
        Assert.Equal((305, 407), Origin(glyphs["F"]));
        Assert.Equal(2 * 10, glyphs["F"].TextMatrix.A); // Tm's scale of 2 times the font size.
        Assert.Equal((325, 427), Origin(glyphs["G"]));
    }

    [Fact]
    public void Character_word_and_horizontal_scaling_advance_each_glyph_as_section_9_4_4_computes()
    {
        const string text = "a b";
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(document.GetStandard14Font(Standard14Font.Helvetica), 10);
            canvas.SetCharacterSpacing(1);
            canvas.SetWordSpacing(5);
            canvas.SetHorizontalScaling(50);
            canvas.ShowText(text);
            canvas.EndText();
        }

        List<Glyph> glyphs = ReplayGlyphs(document);
        double[] widths = AfmReference.Read("Helvetica").Advances(text);
        double afterA = ((widths[0] * 10 / 1000) + 1) * 0.5;
        double afterSpace = afterA + (((widths[1] * 10 / 1000) + 1 + 5) * 0.5);
        Assert.Equal([0, Math.Round(afterA, 6), Math.Round(afterSpace, 6)], glyphs.Select(glyph => Math.Round(glyph.X, 6)));
    }

    [Fact]
    public void Kerned_text_is_positioned_and_measured_by_the_AFM_widths_plus_the_KPX_pairs()
    {
        const string text = "AVATAR Toyota";
        const double size = 20;
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(helvetica, size);
            canvas.ShowText(text, kerning: true);
            canvas.EndText();
        }

        AfmReference afm = AfmReference.Read("Helvetica");
        double[] widths = afm.Advances(text);
        double[] kerns = [.. text.Zip(text.Skip(1), afm.Kern)];
        Assert.Contains(kerns, kern => kern != 0);
        List<Glyph> glyphs = ReplayGlyphs(document);
        double x = 0;
        for (int index = 0; index < text.Length; index++)
        {
            Assert.Equal(x, glyphs[index].X, 6);
            x += (widths[index] + (index < kerns.Length ? kerns[index] : 0)) * size / 1000;
        }

        Assert.Equal(x, helvetica.MeasureWidth(text, size, kerning: true), 9);
        Assert.Equal(widths.Sum() * size / 1000, helvetica.MeasureWidth(text, size), 9);
    }

    [Fact]
    public void Explicit_adjustments_move_the_next_glyph_by_thousandths_of_the_font_size()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(document.GetStandard14Font(Standard14Font.Courier), 10);
            canvas.ShowText("ABC", [100, -200, 0]);
            Assert.Throws<ArgumentException>(() => canvas.ShowText("AB", [1.0]));
            Assert.Throws<ArgumentException>(() => canvas.ShowText("A", [double.NaN]));
            canvas.EndText();
        }

        Assert.Equal([0, 5, 13], ReplayGlyphs(document).Select(glyph => Math.Round(glyph.X, 6)));
    }

    [Fact]
    public void A_character_outside_WinAnsiEncoding_throws_naming_it_and_nothing_is_written()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);
        Assert.False(helvetica.CanEncode("abcЖ"));
        Assert.True(helvetica.CanEncode("abc"));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(helvetica, 12);
            ArgumentException error = Assert.Throws<ArgumentException>(() => canvas.ShowText("abcЖ"));
            Assert.Equal("text", error.ParamName);
            Assert.Contains("'Ж' (U+0416) at index 3", error.Message, StringComparison.Ordinal);
            Assert.Contains("WinAnsiEncoding", error.Message, StringComparison.Ordinal);
            Assert.Contains("embedded", error.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => canvas.ShowTextOnNextLine("Ж"));
            Assert.Throws<ArgumentException>(() => canvas.ShowTextOnNextLine("Ж", 1, 1));
            Assert.Throws<ArgumentException>(() => canvas.ShowText("Ж", kerning: true));
            canvas.ShowText("ok");
            canvas.EndText();
        }

        List<Glyph> glyphs = ReplayGlyphs(document);
        Assert.Equal("ok", string.Concat(glyphs.Select(glyph => glyph.Unicode)));
        Assert.Equal((0.0, 0.0, 0.0), (glyphs[0].Y, glyphs[0].State.WordSpacing, glyphs[0].State.CharacterSpacing));
    }

    [Fact]
    public void WinAnsi_characters_beyond_ASCII_re_read_as_the_drawn_unicode_with_their_AFM_widths()
    {
        const string text = "€–“”é…Œ©ßü";
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont times = document.GetStandard14Font(Standard14Font.TimesBold);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(times, 10);
            canvas.ShowText(text);
            canvas.EndText();
        }

        List<Glyph> glyphs = ReplayGlyphs(document);
        Assert.Equal(text, string.Concat(glyphs.Select(glyph => glyph.Unicode)));
        Assert.Equal(AfmReference.Read("Times-Bold").Advances(text).Sum() / 100, times.MeasureWidth(text, 10), 9);
    }

    [Theory]
    [InlineData(Standard14Font.Symbol, "αΣ∞", "alpha Sigma infinity")]
    [InlineData(Standard14Font.ZapfDingbats, "✓✔", "a19 a20")]
    public void Symbol_and_ZapfDingbats_show_text_in_their_built_in_encodings(Standard14Font which, string text, string names)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.GetStandard14Font(which);
        Assert.False(font.CanEncode("A"));
        Assert.False(document.GetStandard14Font(Standard14Font.Helvetica).CanEncode(text));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(font, 10);
            canvas.ShowText(text);
            ArgumentException error = Assert.Throws<ArgumentException>(() => canvas.ShowText("A"));
            Assert.Contains("built-in encoding of " + font.Name, error.Message, StringComparison.Ordinal);
            canvas.EndText();
        }

        List<Glyph> glyphs = ReplayGlyphs(document);
        Assert.Equal(text, string.Concat(glyphs.Select(glyph => glyph.Unicode)));
        Assert.All(glyphs, glyph => Assert.Equal(font.Name, glyph.BaseFont));
        AfmReference afm = AfmReference.Read(font.Name);
        Assert.Equal(names.Split(' ').Sum(name => afm.Widths[name]) / 100, font.MeasureWidth(text, 10), 9);
    }

    [Theory]
    [InlineData(Standard14Font.Helvetica)]
    [InlineData(Standard14Font.HelveticaBoldOblique)]
    [InlineData(Standard14Font.TimesRoman)]
    [InlineData(Standard14Font.TimesBoldItalic)]
    [InlineData(Standard14Font.Courier)]
    [InlineData(Standard14Font.CourierBoldOblique)]
    public void Font_metrics_are_the_AFM_values_and_the_font_is_not_embedded(Standard14Font which)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.GetStandard14Font(which);
        AfmReference afm = AfmReference.Read(font.Name);
        Assert.Equal(afm.Number("Ascender"), font.Ascent);
        Assert.Equal(afm.Number("Descender"), font.Descent);
        Assert.Equal(afm.Number("CapHeight"), font.CapHeight);
        Assert.Equal(afm.Number("XHeight"), font.XHeight);
        Assert.True(font.AppliesWordSpacing);
        Assert.Equal(font.Name, font.Font.BaseFont);
        Assert.False(font.Font.IsEmbedded);
    }

    [Fact]
    public void Text_operators_out_of_place_in_Figure_9_throw_and_leave_the_content_valid()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 10, 10));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            Assert.Throws<InvalidOperationException>(() => canvas.EndText());
            Assert.Throws<InvalidOperationException>(() => canvas.MoveText(1, 1));
            Assert.Throws<InvalidOperationException>(() => canvas.MoveToNextLine());
            Assert.Throws<InvalidOperationException>(() => canvas.SetTextMatrix(Matrix.Identity));
            Assert.Throws<InvalidOperationException>(() => canvas.ShowText("x"));
            canvas.MoveTo(0, 0);
            Assert.Throws<InvalidOperationException>(() => canvas.BeginText());
            Assert.Throws<InvalidOperationException>(() => canvas.SetFont(helvetica, 12));
            Assert.Throws<InvalidOperationException>(() => canvas.SetCharacterSpacing(1));
            canvas.LineTo(10, 10);
            canvas.Stroke();
            canvas.BeginText();
            Assert.Throws<InvalidOperationException>(() => canvas.ShowText("x"));
            Assert.Throws<InvalidOperationException>(() => canvas.BeginText());
            Assert.Throws<InvalidOperationException>(() => canvas.MoveTo(0, 0));
            Assert.Throws<InvalidOperationException>(() => canvas.SaveState());
            Assert.Throws<InvalidOperationException>(() => canvas.RestoreState());
            Assert.Throws<InvalidOperationException>(() => canvas.Transform(Matrix.Identity));
            Assert.Throws<InvalidOperationException>(() => canvas.DrawForm(form));
            Assert.Throws<ArgumentOutOfRangeException>(() => canvas.SetFont(helvetica, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => canvas.SetTextRise(double.PositiveInfinity));
            using (var other = PdfDocument.Create())
            {
                Assert.Throws<ArgumentException>(() => canvas.SetFont(other.GetStandard14Font(Standard14Font.Helvetica), 12));
            }

            canvas.SetFont(helvetica, 12);
            canvas.ShowText("ok");
            canvas.EndText();
        }

        Assert.Equal("ok", string.Concat(ReplayGlyphs(document).Select(glyph => glyph.Unicode)));
    }

    [Fact]
    public void One_font_dictionary_serves_every_canvas_page_and_form_and_each_font_gets_its_own_name()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 200, 200));
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 100, 100));
        foreach (PdfCanvas canvas in new[] { document.Pages[0].CreateCanvas(), document.Pages[1].CreateCanvas(), form.CreateCanvas() })
        {
            using (canvas)
            {
                canvas.BeginText();
                canvas.SetFont(document.GetStandard14Font(Standard14Font.Helvetica), 12);
                canvas.ShowText("a");
                canvas.SetFont(document.GetStandard14Font(Standard14Font.Helvetica), 10);
                canvas.SetFont(document.GetStandard14Font(Standard14Font.Courier), 12);
                canvas.ShowText("b");
                canvas.EndText();
            }
        }

        CosDictionary[] resources = [document.Pages[0].Resources!, document.Pages[1].Resources!, form.Resources!];
        Dictionary<string, CosObject>[] fonts = [.. resources.Select(dictionary =>
            ((CosDictionary)document.Resolve(dictionary[FontKey])).ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value))];
        Assert.All(fonts, dictionary => Assert.Equal(["F1", "F2"], dictionary.Keys.Select(key => key.TrimStart('/')).Order()));
        Assert.Equal(2, fonts.SelectMany(dictionary => dictionary.Values).Distinct().Count());
        Assert.Single(fonts.Select(dictionary => string.Join(",", dictionary.OrderBy(entry => entry.Key).Select(entry => entry.Key + "=" + entry.Value))).Distinct());
        Assert.Equal("ab", string.Concat(ReplayGlyphs(document).Select(glyph => glyph.Unicode)));
    }

    [Fact]
    public void Disposing_the_canvas_inside_a_text_object_ends_it()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(document.GetStandard14Font(Standard14Font.Helvetica), 12);
            canvas.ShowText("open");
        }

        Assert.Equal("open", string.Concat(ReplayGlyphs(document).Select(glyph => glyph.Unicode)));
    }

    [Fact]
    public void Qpdf_accepts_the_file_and_poppler_draws_the_text_where_the_canvas_put_it()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages[0].MediaBox = new PdfRectangle(0, 0, 200, 100);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(document.GetStandard14Font(Standard14Font.HelveticaBold), 40);
            canvas.MoveText(10, 20);
            canvas.ShowText("WAVE", kerning: true);
            canvas.EndText();
        }

        byte[] saved = CanvasTests.Saved(document);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        (exitCode, byte[] image, string error) = ExternalTool.RunBinary("pdftoppm", saved, "-r", "72", "-gray");
        Assert.True(exitCode == 0, error);
        (int width, int height, int offset) = PgmHeader(image);
        Assert.Equal((200, 100), (width, height));
        int Ink(int top, int bottom)
        {
            int count = 0;
            for (int y = top; y < bottom; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    count += image[offset + (y * width) + x] < 128 ? 1 : 0;
                }
            }

            return count;
        }

        // Cap height 718/1000 of 40 points is about 29 points above the baseline at y = 20 (row 80 from the top).
        Assert.True(Ink(100 - 20 - 28, 100 - 20) > 200, "The glyphs between the baseline and the cap height are inked.");
        Assert.Equal(0, Ink(0, 100 - 20 - 34));
    }

    private static readonly CosName FontKey = new("Font");

    private static (double X, double Y) Origin(Glyph glyph) => (Math.Round(glyph.X, 6), Math.Round(glyph.Y, 6));

    /// <summary>The width, height and the offset of the first sample of a binary PGM (<c>P5</c>) image.</summary>
    private static (int Width, int Height, int Offset) PgmHeader(byte[] image)
    {
        Assert.Equal("P5"u8.ToArray(), image[..2]);
        int position = 2;
        int Number()
        {
            while (char.IsWhiteSpace((char)image[position]))
            {
                position++;
            }

            int value = 0;
            while (char.IsAsciiDigit((char)image[position]))
            {
                value = (value * 10) + image[position++] - '0';
            }

            return value;
        }

        int width = Number();
        int height = Number();
        _ = Number();
        return (width, height, position + 1);
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
