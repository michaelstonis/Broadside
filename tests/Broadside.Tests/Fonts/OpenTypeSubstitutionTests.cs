using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Fonts;

/// <summary>
/// OpenType glyph substitution (issue #174) through the public seam: an embedded program with a <c>GSUB</c> table
/// (<see cref="ShapingTestFont"/>), <see cref="PdfCanvasFont.Shape"/>, the canvas, and text extraction of the saved file.
/// </summary>
public sealed class OpenTypeSubstitutionTests
{
    public static TheoryData<string, string, int[]> LookupTypes => new()
    {
        { "type 1 format 1 (delta), smcp", "abc", [28, 29, 30] },
        { "type 2 multiple, ccmp", "q", [17, 32] },
        { "type 3 alternate, salt", "g", [33] },
        { "type 4 ligature, liga (longest first)", "ffi fi ff", [35, 27, 36, 27, 37] },
        { "type 4 ligature skipping a mark (IgnoreMarks)", "d\u0301j", [49, 48] },
        { "type 5 format 1 (glyphs)", "xy y", [24, 38, 27, 25] },
        { "type 5 format 2 (classes)", "ae oo e", [1, 39, 27, 15, 40, 27, 5] },
        { "type 5 format 3 (coverages)", "kl k", [42, 12, 27, 11] },
        { "type 6 format 1 (glyphs)", "mnp nnp", [13, 43, 16, 27, 14, 14, 16] },
        { "type 6 format 2 (classes)", "uvw vw", [21, 44, 23, 27, 22, 23] },
        { "type 6 format 3 (coverages)", "rst st", [18, 45, 20, 27, 19, 20] },
        { "type 7 extension", "h", [46] },
        { "type 8 reverse chaining", "zzz", [47, 47, 26] },
    };

    [Theory]
    [MemberData(nameof(LookupTypes))]
    public void Every_lookup_type_produces_the_glyphs_the_feature_file_says(string lookup, string text, int[] expected)
    {
        _ = lookup;
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(ShapingTestFont.Build());
        TextShaping shaping = TextShaping.Default.WithFeature(lookup.Contains("smcp", StringComparison.Ordinal) ? "smcp" : "salt", true);

        ShapedText shaped = font.Shape(text, shaping);

        Assert.Equal(expected, Codes(shaped));
        Assert.False(shaped.IsRightToLeft);
        Assert.Equal(shaped.MeasureWidth(12), font.MeasureWidth(text, 12, shaping), 9);
        Assert.Equal(expected.Sum(glyph => 500 + glyph) * 12 / 1000.0, shaped.MeasureWidth(12), 9);
    }

    [Fact]
    public void Features_are_selected_by_script_language_and_setting()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(ShapingTestFont.Build());

        Assert.Equal([9], Codes(font.Shape("i", TextShaping.Default)));
        Assert.Equal([31], Codes(font.Shape("i", TextShaping.Default.WithLanguage("TRK"))));
        Assert.Equal([9], Codes(font.Shape("i", TextShaping.Default.WithLanguage("TRK").WithFeature("locl", false))));
        Assert.Equal([1, 2, 3], Codes(font.Shape("abc", TextShaping.Default)));
        Assert.Equal([6, 9], Codes(font.Shape("fi", TextShaping.Default.WithFeature("liga", false))));
        Assert.Equal([7], Codes(font.Shape("g", TextShaping.Default)));

        // Under a script whose language system has no Latin features no ligature applies, and the run is right to left.
        Assert.Equal([9, 6], Codes(font.Shape("fi", TextShaping.Default.WithScript("arab"))));
        Assert.Equal(TextShaping.Default.WithFeature("liga", true), TextShaping.Default.WithFeature("liga", true));
        Assert.Throws<ArgumentException>(() => TextShaping.Default.WithScript("toolong"));
    }

    [Fact]
    public void A_ligature_re_extracts_as_its_characters_through_ToUnicode()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(ShapingTestFont.Build());
        ShapedText shaped = font.Shape("fi office", TextShaping.Default);
        Assert.Equal([36, 27, 15, 35, 3, 5], Codes(shaped));

        Show(document.Pages[0], shaped);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        TextPage text = reopened.Pages[0].ExtractText();

        Assert.Equal("fi office", text.Text);
        Assert.Equal("fi", text.GetGlyphText(text.Glyphs[0]));
        Assert.Equal("ffi", text.GetGlyphText(text.Glyphs[3]));
        Assert.DoesNotContain(text.Glyphs, glyph => glyph.Flags.HasFlag(TextGlyphFlags.ActualText));
    }

    [Fact]
    public void An_Arabic_word_shapes_into_joined_forms_and_extracts_back_to_its_source_text()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(ShapingTestFont.Build());

        // Logical order: beh.init alef.fina beh (alef does not join to its left); shown in visual order, right to left.
        ShapedText door = font.Shape("باب", TextShaping.Default);
        Assert.True(door.IsRightToLeft);
        Assert.Equal([50, 60, 54], Codes(door));
        Assert.Equal([2, 1, 0], door.Glyphs.ToArray().Select(glyph => glyph.Cluster));
        Assert.Equal([56, 58, 54], Codes(font.Shape("بتب", TextShaping.Default)));

        // rlig after the joining forms: lam.medi + alef.fina -> lam_alef.fina.
        ShapedText lamAlef = font.Shape("بلا", TextShaping.Default);
        Assert.Equal([65, 54], Codes(lamAlef));
        Assert.Equal([64], Codes(font.Shape("لا", TextShaping.Default)));

        Show(document.Pages[0], door, lamAlef);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        Assert.Equal("باب بلا", reopened.Pages[0].ExtractText().Text);
    }

    [Fact]
    public void A_Hebrew_run_is_shown_right_to_left_and_extracts_in_logical_order()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(ShapingTestFont.Build());
        ShapedText peace = font.Shape("שלום", TextShaping.Default);

        Assert.True(peace.IsRightToLeft);
        Assert.Equal([69, 68, 67, 66], Codes(peace));
        Assert.Equal([66, 67, 68, 69], Codes(font.Shape("שלום", TextShaping.Default.WithRightToLeft(false))));

        Show(document.Pages[0], peace);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        Assert.Equal("שלום", reopened.Pages[0].ExtractText().Text);
    }

    [Fact]
    public void Text_ToUnicode_cannot_give_back_is_wrapped_in_ActualText()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(ShapingTestFont.Build());

        // A character shown by two glyphs, and a ligature with a mark between its components (one cluster of two glyphs).
        Show(document.Pages[0], font.Shape("q", TextShaping.Default), font.Shape("d\u0301j", TextShaping.Default), font.Shape("fi", TextShaping.Default));
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        TextPage text = reopened.Pages[0].ExtractText();

        Assert.Equal("q d\u0301j fi", text.Text);
        Assert.Equal(4, text.Glyphs.Count(glyph => glyph.Flags.HasFlag(TextGlyphFlags.ActualText)));
        Assert.False(text.Glyphs[^1].Flags.HasFlag(TextGlyphFlags.ActualText));
    }

    [Fact]
    public void Shaped_text_is_refused_by_a_canvas_with_another_font_and_adjustments_must_match_the_glyphs()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(ShapingTestFont.Build());
        PdfCanvasFont other = document.GetStandard14Font(Standard14Font.Helvetica);
        ShapedText shaped = font.Shape("fi", TextShaping.Default);
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        canvas.BeginText();
        canvas.SetFont(other, 12);

        Assert.Throws<ArgumentException>(() => canvas.ShowText(shaped));
        canvas.SetFont(font, 12);
        Assert.Throws<ArgumentException>(() => canvas.ShowText(shaped, [1, 2]));
        Assert.Throws<ArgumentException>(() => font.Shape("日", TextShaping.Default));

        // A Standard 14 font has no layout tables: one glyph per character, kerned when kern is on (the default).
        ShapedText helvetica = other.Shape("AV", TextShaping.Default);
        Assert.Equal(other.MeasureWidth("AV", 1000, kerning: true), helvetica.MeasureWidth(1000), 9);
        Assert.Equal(other.MeasureWidth("AV", 1000), other.Shape("AV", TextShaping.Default.WithFeature("kern", false)).MeasureWidth(1000), 9);
    }

    private static int[] Codes(ShapedText shaped) => [.. shaped.Glyphs.ToArray().Select(glyph => glyph.Code)];

    private static void Show(PdfPage page, params ShapedText[] runs)
    {
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.BeginText();
        canvas.SetFont(runs[0].Font, 18);
        canvas.MoveText(72, 600);
        for (int index = 0; index < runs.Length; index++)
        {
            if (index > 0)
            {
                canvas.ShowText(runs[0].Font.Shape(" ", TextShaping.Default));
            }

            canvas.ShowText(runs[index]);
        }

        canvas.EndText();
    }

    private static byte[] Saved(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
