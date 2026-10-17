using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.TestSupport;
using Broadside.Text;
using static Broadside.TestSupport.PositioningTestFont;

namespace Broadside.Tests.Fonts;

/// <summary>
/// OpenType glyph positioning (issue #175) through the public seam: an embedded program with a <c>GPOS</c> table
/// (<see cref="PositioningTestFont"/>) or a legacy <c>kern</c> table, <see cref="PdfCanvasFont.Shape"/>, measuring, the canvas, and
/// the glyph positions text extraction reads back from the saved file.
/// </summary>
public sealed class OpenTypePositioningTests
{
    private const double Size = 20;

    /// <summary>Each lookup type: the text and the glyphs in visual order as (code, advance, x offset, y offset) in thousandths.</summary>
    public static TheoryData<string, string, double[][]> LookupTypes => new()
    {
        { "type 1 format 1 (placement and advance)", "x", [[24, 544, 10, 5]] },
        { "type 1 format 2", "yz", [[25, 555, 0, 0], [26, 566, 0, 0]] },
        { "type 2 format 1 (pairs, no second value: the second glyph starts the next pair)", "ava", [[1, 421, 0, 0], [22, 452, 0, 0], [1, 501, 0, 0]] },
        { "type 2 format 2 (classes, second value)", "to te", [[20, 470, 0, 0], [15, 515, 5, 0], [27, 527, 0, 0], [20, 470, 0, 0], [5, 505, 5, 0]] },
        { "type 3 cursive, right to left (the last glyph stays on the baseline)", "بب", [[BehFinal, 500, 0, 0], [BehInit, 536, 0, -200]] },
        { "type 4 mark to base", "á", [[1, 501, 0, 0], [Acute, 0, 250 - 150 - 501, 100]] },
        { "type 5 mark to ligature (each mark on its component)", "f́í", [[FI, 531, 0, 0], [Acute, 0, 200 - 150 - 531, 150], [Acute, 0, 450 - 150 - 531, 150]] },
        { "type 6 mark to mark", "á̀", [[1, 501, 0, 0], [Acute, 0, -401, 100], [Grave, 0, -401, 400]] },
        { "type 7 contextual (format 1)", "kl k", [[11, 611, 0, 0], [12, 512, 0, 0], [27, 527, 0, 0], [11, 511, 0, 0]] },
        { "type 8 chained contextual (format 3)", "mnp np", [[13, 513, 0, 0], [14, 574, 0, 0], [16, 516, 0, 0], [27, 527, 0, 0], [14, 514, 0, 0], [16, 516, 0, 0]] },
        { "type 9 extension", "h", [[8, 523, 0, 0]] },
    };

    [Theory]
    [MemberData(nameof(LookupTypes))]
    public void Every_lookup_type_positions_the_glyphs_as_the_feature_file_says(string lookup, string text, double[][] expected)
    {
        _ = lookup;
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(Build());

        ShapedText shaped = font.Shape(text, TextShaping.Default);

        Assert.Equal(expected, [.. shaped.Glyphs.ToArray().Select(glyph => new[] { glyph.Code, glyph.Advance, glyph.XOffset, glyph.YOffset })]);
        Assert.Equal(expected.Sum(glyph => glyph[1]) * Size / 1000, shaped.MeasureWidth(Size), 9);
        Assert.Equal(shaped.MeasureWidth(Size), font.MeasureWidth(text, Size, TextShaping.Default), 9);
    }

    [Fact]
    public void Kerned_text_measures_and_renders_with_the_pair_adjustment()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(Build());
        TextShaping unkerned = TextShaping.Default.WithFeature("kern", false);

        Assert.Equal((421 + 452 + 501) * Size / 1000, font.MeasureWidth("ava", Size, TextShaping.Default), 9);
        Assert.Equal((501 + 522 + 501) * Size / 1000, font.MeasureWidth("ava", Size, unkerned), 9);

        // The character API kerns embedded fonts too (it had no kerning for them before GPOS).
        Assert.Equal((421 + 452 + 501) * Size / 1000, font.MeasureWidth("ava", Size, kerning: true), 9);

        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(font, Size);
            canvas.MoveText(72, 600);
            canvas.ShowText(font.Shape("ava", TextShaping.Default));
            canvas.MoveText(0, -100);
            canvas.ShowText("ava", kerning: true);
            canvas.EndText();
        }

        TextPage page = Reopened(document);
        Assert.Equal("avaava", string.Concat(page.Text.Where(character => !char.IsWhiteSpace(character))));
        foreach (double baseline in (double[])[600, 500])
        {
            TextGlyph[] line = [.. page.Glyphs.Where(glyph => Math.Abs(glyph.Origin.Y - baseline) < 0.01)];
            Assert.Equal([72, 80.42, 89.46], line.Select(glyph => Math.Round(glyph.Origin.X, 6)));
        }
    }

    [Fact]
    public void The_legacy_kern_table_kerns_a_font_without_a_GPOS_kern_feature()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont legacy = document.EmbedTrueTypeFont(Build(gpos: false, (Letter('a'), Letter('v'), -80)));
        PdfCanvasFont both = document.EmbedTrueTypeFont(Build(gpos: true, (Letter('a'), Letter('v'), -999)));

        Assert.Equal([421, 522], legacy.Shape("av", TextShaping.Default).Glyphs.ToArray().Select(glyph => glyph.Advance));
        Assert.Equal((421 + 522) * Size / 1000, legacy.MeasureWidth("av", Size, kerning: true), 9);
        Assert.Equal([501, 522], legacy.Shape("av", TextShaping.Default.WithFeature("kern", false)).Glyphs.ToArray().Select(glyph => glyph.Advance));
        Assert.Equal([421, 522], both.Shape("av", TextShaping.Default).Glyphs.ToArray().Select(glyph => glyph.Advance));
    }

    [Theory]
    [InlineData("بَ", Beh, Fatha, 700, 600)]
    [InlineData("שָ", Shin, Qamats, -20, 0)]
    public void Combining_marks_sit_on_their_bases_in_Arabic_and_Hebrew(string text, int baseGlyph, int mark, int baseAnchorY, int markAnchorY)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedTrueTypeFont(Build());
        ShapedText shaped = font.Shape(text, TextShaping.Default);
        Assert.True(shaped.IsRightToLeft);
        Assert.Equal([mark, baseGlyph], shaped.Glyphs.ToArray().Select(glyph => glyph.Code));
        Assert.Equal(0, shaped.Glyphs[0].Advance);

        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(font, Size);
            canvas.SetTextRise(3);
            canvas.MoveText(72, 600);
            canvas.ShowText(shaped);
            canvas.ShowText(font.Shape("a", TextShaping.Default));
            canvas.EndText();
        }

        TextPage page = Reopened(document);
        Assert.Equal(text + "a", page.Text);
        TextGlyph drawnBase = page.Glyphs.Single(glyph => glyph.CharacterCode == baseGlyph);
        TextGlyph drawnMark = page.Glyphs.Single(glyph => glyph.CharacterCode == mark);
        TextGlyph after = page.Glyphs.Single(glyph => glyph.CharacterCode == Letter('a'));

        // The mark's anchor lands on the base's anchor; the text rise comes back for the glyphs after it.
        Assert.Equal(drawnBase.Origin.X + (250 * Size / 1000), drawnMark.Origin.X + (150 * Size / 1000), 6);
        Assert.Equal(drawnBase.Origin.Y + (baseAnchorY * Size / 1000), drawnMark.Origin.Y + (markAnchorY * Size / 1000), 6);
        Assert.Equal(drawnBase.Origin.Y, after.Origin.Y, 6);
        Assert.Equal(603, drawnBase.Origin.Y, 6);
        Assert.Equal(drawnBase.Origin.X + (Width(baseGlyph) * Size / 1000), after.Origin.X, 6);
    }

    [Fact]
    public void Damaged_GPOS_tables_position_text_without_failing()
    {
        byte[] table = Gpos();
#pragma warning disable CA5394 // Reproducible test data, not security.
        var random = new Random(175);
        using PdfDocument document = PdfDocument.Create();
        for (int round = 0; round < 200; round++)
        {
            byte[] damaged = [.. table];
            for (int flip = 0; flip < 6; flip++)
            {
                damaged[random.Next(damaged.Length)] = (byte)random.Next(256);
            }
#pragma warning restore CA5394

            PdfCanvasFont font = document.EmbedTrueTypeFont(Build(damaged));
            foreach (string text in (string[])["ava to te x yz h kl mnp á̀ f́í", "بب بَ", "שָ"])
            {
                ShapedText shaped = font.Shape(text, TextShaping.Default);
                Assert.All(shaped.Glyphs.ToArray(), glyph => Assert.True(double.IsFinite(glyph.Advance + glyph.XOffset + glyph.YOffset)));
                Assert.Equal(shaped.MeasureWidth(Size), font.MeasureWidth(text, Size, TextShaping.Default), 9);
            }
        }
    }

    private static TextPage Reopened(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        using PdfDocument reopened = PdfDocument.Open(stream.ToArray());
        return reopened.Pages[0].ExtractText();
    }
}
