using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Content;

/// <summary>
/// Find and replace in existing content (issue #641): the matched glyphs are found by identity and rewritten in their text-showing
/// operator, in the original font when it can show the replacement (simple, Type 0 Identity-H and Type 3 fonts), else in a fallback
/// font inside the same text object; a match across operators; the three fit modes; delete. ISO 32000-2 §9.4.3 (Table 107), §9.3.4,
/// §9.6.5, §9.7.6.2 and §9.9.2.
/// </summary>
public sealed class TextReplaceTests
{
    [Fact]
    public void A_simple_font_shows_a_replacement_made_of_its_own_glyphs()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page("BT /F1 12 Tf 72 700 Td (Hello World, again) Tj ET"u8.ToArray()));
        PdfPage page = document.Pages[0];
        List<TextGlyph> before = [.. page.ExtractText().Glyphs];

        PdfTextEditResult result = page.ReplaceText(Single(page, "World"), "There");

        Assert.Equal(1, result.Replaced);
        Assert.Equal(0, result.UsedFallbackFont);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("Hello There, again", Text(page));
        Assert.Empty(page.FindText(new TextQuery("World")));
        List<TextGlyph> after = [.. page.ExtractText().Glyphs];
        Assert.All(after, glyph => Assert.IsType<PdfTrueTypeFont>(glyph.Font));

        // The same widths: every glyph is where it was.
        AssertSameOrigins(before, after);
    }

    [Fact]
    public void A_replacement_reads_back_and_the_old_text_is_gone_after_a_save()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Invoice date: 2024-01-31) Tj ET");

        PdfTextEditResult result = document.ReplaceText(new TextQuery("2024-01-31"), "2026-11-27");

        Assert.Equal(1, result.Replaced);
        using PdfDocument reopened = PdfDocument.Open(Save(document));
        Assert.Equal("Invoice date: 2026-11-27", Text(reopened.Pages[0]));
        Assert.Empty(reopened.FindText(new TextQuery("2024"), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_subset_TrueType_font_without_the_glyph_switches_to_the_fallback_font_inside_the_text_object()
    {
        using PdfDocument document = PdfDocument.Open(SubsetPage("BT /F1 12 Tf 72 700 Td (Hello World) Tj ( after) Tj ET", "Helo Wrdaft"));
        PdfPage page = document.Pages[0];
        PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);
        TextGlyph after = page.ExtractText().Glyphs.Single(glyph => glyph.CharacterCode == 'f');

        PdfTextEditResult result = page.ReplaceText(Single(page, "World"), "Zed", new PdfTextReplaceOptions().WithFallbackFont(helvetica));

        Assert.Equal(1, result.Replaced);
        Assert.Equal(1, result.UsedFallbackFont);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == TextEditDiagnostics.TextEditFallbackFont.Name);
        Assert.Equal("Hello Zed after", Text(page));
        string content = RedactionSamples.Content(document, page);
        Assert.Matches(@"/TeF1 12 Tf\s*\[<5A>\] TJ\s*/F1 12 Tf", content);
        List<TextGlyph> glyphs = [.. page.ExtractText().Glyphs];

        // 'e' and 'd' exist in the subset, so only 'Z' is in Helvetica; every glyph keeps the size and the baseline.
        Assert.Equal("Helvetica", Assert.IsAssignableFrom<PdfSimpleFont>(glyphs.Single(glyph => glyph.CharacterCode == 'Z').Font).BaseFont);
        Assert.All(glyphs, glyph => Assert.Equal(700, glyph.Origin.Y, 3));
        Assert.All(glyphs, glyph => Assert.Equal(12, glyph.FontSize, 3));
        Assert.Equal(after.Origin.X, glyphs.Single(glyph => glyph.CharacterCode == 'f').Origin.X, 3);
    }

    [Fact]
    public void Without_a_fallback_font_the_resolver_or_Helvetica_shows_what_the_subset_lacks()
    {
        using PdfDocument document = PdfDocument.Open(SubsetPage("BT /F1 12 Tf 72 700 Td (Hello World) Tj ET", "Helo Wrd"));
        PdfPage page = document.Pages[0];

        PdfTextEditResult result = page.ReplaceText(Single(page, "World"), "Quiz");

        Assert.Equal(1, result.UsedFallbackFont);
        Assert.Equal("Hello Quiz", Text(page));
    }

    [Fact]
    public void A_Type0_Identity_H_subset_reuses_the_codes_the_page_shows()
    {
        using PdfDocument document = PdfDocument.Open(CanvasPage("Hello World"));
        PdfPage page = document.Pages[0];
        Assert.IsType<PdfType0Font>(page.ExtractText().Glyphs[0].Font);
        List<TextGlyph> before = [.. page.ExtractText().Glyphs];

        PdfTextEditResult result = page.ReplaceText(Single(page, "World"), "Hold");

        Assert.Equal(1, result.Replaced);
        Assert.Equal(0, result.UsedFallbackFont);
        Assert.Equal("Hello Hold", Text(page));
        List<TextGlyph> after = [.. page.ExtractText().Glyphs];
        Assert.All(after, glyph => Assert.IsType<PdfType0Font>(glyph.Font));
        AssertSameOrigins([.. before.Take(6)], [.. after.Take(6)]);
    }

    [Fact]
    public void A_Type0_subset_falls_back_for_characters_it_never_showed()
    {
        using PdfDocument document = PdfDocument.Open(CanvasPage("Hello World"));
        PdfPage page = document.Pages[0];

        PdfTextEditResult result = page.ReplaceText(
            Single(page, "World"), "Quiz", new PdfTextReplaceOptions().WithFallbackFont(document.GetStandard14Font(Standard14Font.Helvetica)));

        Assert.Equal(1, result.UsedFallbackFont);
        Assert.Equal("Hello Quiz", Text(page));
    }

    [Fact]
    public void A_Type3_font_shows_a_replacement_when_every_glyph_has_a_procedure_and_falls_back_otherwise()
    {
        using PdfDocument document = PdfDocument.Open(Type3Page("BT /T3 20 Tf 72 700 Td (abcab) Tj ET"));
        PdfPage page = document.Pages[0];
        PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);

        PdfTextEditResult same = page.ReplaceText(Single(page, "bc"), "cab", new PdfTextReplaceOptions().WithFallbackFont(helvetica));

        Assert.Equal((1, 0), (same.Replaced, same.UsedFallbackFont));
        Assert.Equal("acabab", Text(page));
        Assert.All(page.ExtractText().Glyphs, glyph => Assert.IsType<PdfType3Font>(glyph.Font));

        PdfTextEditResult fallback = page.ReplaceText(page.FindText(new TextQuery("ab"))[^1], "xy", new PdfTextReplaceOptions().WithFallbackFont(helvetica));

        Assert.Equal((1, 1), (fallback.Replaced, fallback.UsedFallbackFont));
        Assert.Equal("acabxy", Text(page));
    }

    [Fact]
    public void A_match_across_two_TJ_operators_is_replaced_in_the_first_and_removed_from_the_second()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td [(Hello W) -20 (or)] TJ [(ld) 50 (, friend)] TJ ET");
        PdfPage page = document.Pages[0];
        TextGlyph comma = page.ExtractText().Glyphs.Single(glyph => glyph.CharacterCode == ',');

        PdfTextEditResult result = page.ReplaceText(Single(page, "World"), "Earth");

        Assert.Equal(1, result.Replaced);
        Assert.Equal("Hello Earth, friend", Text(page));
        Assert.Equal(comma.Origin.X, page.ExtractText().Glyphs.Single(glyph => glyph.CharacterCode == ',').Origin.X, 3);
        string content = RedactionSamples.Content(document, page);
        Assert.DoesNotContain("(ld)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepPosition_leaves_later_glyphs_where_they_were()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (A cat sat) Tj ET");
        PdfPage page = document.Pages[0];
        double before = Glyph(page, 's').Origin.X;

        page.ReplaceText(Single(page, "cat"), "tiger", new PdfTextReplaceOptions().WithFit(PdfTextFit.KeepPosition));

        Assert.Equal("A tiger sat", Text(page).Replace("  ", " ", StringComparison.Ordinal));
        Assert.Equal(before, Glyph(page, 's').Origin.X, 3);
    }

    [Fact]
    public void Compress_squeezes_a_wider_replacement_with_Tz_into_the_old_width()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (A cat sat) Tj ET");
        PdfPage page = document.Pages[0];
        TextMatch match = Single(page, "cat");
        double space = Glyph(page, 's').Origin.X;

        page.ReplaceText(match, "cats", new PdfTextReplaceOptions().WithFit(PdfTextFit.Compress));

        Assert.Equal("A cats sat", Text(page));
        string content = RedactionSamples.Content(document, page);
        Assert.Contains("Tz", content, StringComparison.Ordinal);
        Assert.Contains("100 Tz", content, StringComparison.Ordinal);
        List<TextGlyph> glyphs = [.. page.ExtractText().Glyphs];
        TextGlyph last = glyphs[5];
        Assert.Equal('s', (char)last.CharacterCode);
        Assert.True(last.Origin.X + last.Advance.X <= match.Bounds.Right + 0.01, "The replacement fits the old width.");
        Assert.Equal(space, Glyph(page, 's', 1).Origin.X, 3);
    }

    [Fact]
    public void Compress_stops_at_half_the_width()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (A i sat) Tj ET");
        PdfPage page = document.Pages[0];

        page.ReplaceText(Single(page, "i"), "MMMM", new PdfTextReplaceOptions().WithFit(PdfTextFit.Compress));

        Assert.Contains("50 Tz", RedactionSamples.Content(document, page), StringComparison.Ordinal);
    }

    [Fact]
    public void ShiftLine_moves_later_glyphs_on_the_line_and_in_later_operators_on_it_but_not_other_lines()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (A cat ) Tj 100 0 Td (sat) Tj -100 -20 Td (next) Tj ET");
        PdfPage page = document.Pages[0];
        double sat = Glyph(page, 's').Origin.X;
        double next = Glyph(page, 'n').Origin.X;
        PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);
        double difference = helvetica.MeasureWidth("tiger", 12) - helvetica.MeasureWidth("cat", 12);

        page.ReplaceText(Single(page, "cat"), "tiger", new PdfTextReplaceOptions().WithFit(PdfTextFit.ShiftLine));

        Assert.Equal(sat + difference, Glyph(page, 's').Origin.X, 3);
        Assert.Equal(next, Glyph(page, 'n').Origin.X, 3);
        Assert.Equal(700 - 20, Glyph(page, 'n').Origin.Y, 3);
    }

    [Fact]
    public void Delete_with_ShiftLine_closes_the_gap()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td [(Hello cruel) -10 ( World)] TJ ET");
        PdfPage page = document.Pages[0];
        double hello = Glyph(page, 'H').Origin.X;
        double world = Glyph(page, 'W').Origin.X;
        double width = document.GetStandard14Font(Standard14Font.Helvetica).MeasureWidth("cruel", 12);

        PdfTextEditResult result = page.DeleteText(Single(page, "cruel"), new PdfTextReplaceOptions().WithFit(PdfTextFit.ShiftLine));

        // The -10 between "cruel" and the space is after the match, so it stays; the space before "cruel" stays too.
        Assert.Equal(1, result.Replaced);
        Assert.Equal("Hello World", Text(page).Replace("  ", " ", StringComparison.Ordinal));
        Assert.Equal(hello, Glyph(page, 'H').Origin.X, 3);
        Assert.Equal(world - width, Glyph(page, 'W').Origin.X, 3);
    }

    [Fact]
    public void Delete_keeping_positions_leaves_the_gap_and_the_old_text_is_gone()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Total: 42 USD) Tj ET");
        PdfPage page = document.Pages[0];
        double usd = Glyph(page, 'U').Origin.X;

        PdfTextEditResult result = document.DeleteText(new TextQuery("42 "));

        Assert.Equal(1, result.Replaced);
        Assert.Equal(usd, Glyph(page, 'U').Origin.X, 3);
        Assert.DoesNotContain("42", Text(page), StringComparison.Ordinal);
    }

    [Fact]
    public void A_regular_expression_replacement_may_use_its_groups()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Due 2024-01-31 and 2025-12-01) Tj ET");

        PdfTextEditResult result = document.ReplaceText(TextQuery.FromRegex(@"(\d{4})-(\d{2})-(\d{2})"), "$3.$2.$1");

        Assert.Equal(2, result.Replaced);
        Assert.Equal("Due 31.01.2024 and 01.12.2025", Text(document.Pages[0]));
    }

    [Fact]
    public void Text_in_a_form_shared_with_another_page_is_replaced_in_a_copy()
    {
        using PdfDocument document = PdfDocument.Open(SharedFormFile());

        PdfTextEditResult result = document.ReplaceText(new TextQuery("Draft"), "Final", pages: 0..1);

        Assert.Equal(1, result.Replaced);
        Assert.Equal("Final", Text(document.Pages[0]));
        Assert.Equal("Draft", Text(document.Pages[1]));
    }

    [Fact]
    public void Text_clipping_modes_keep_their_mode_on_the_new_run()
    {
        using PdfDocument document = RedactionSamples.Open("BT 7 Tr /F1 12 Tf 72 700 Td (clip me) Tj ET 0 0 612 792 re f");
        PdfPage page = document.Pages[0];

        page.ReplaceText(Single(page, "me"), "you", new PdfTextReplaceOptions().WithFallbackFont(document.GetStandard14Font(Standard14Font.Helvetica)));

        Assert.All(page.ExtractText(new TextExtractionOptions().WithHiddenContent()).Glyphs, glyph => Assert.Equal(TextRenderingMode.Clip, glyph.RenderingMode));
    }

    [Fact]
    public void A_match_the_page_no_longer_shows_is_left_alone_and_recorded()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Hello World) Tj ET");
        PdfPage page = document.Pages[0];
        TextMatch match = Single(page, "World");
        page.ReplaceText(match, "There");

        PdfTextEditResult result = page.ReplaceText(match, "Again");

        Assert.Equal(0, result.Replaced);
        Assert.Equal(TextEditDiagnostics.TextEditMatchNotLocated.Name, Assert.Single(result.Diagnostics).Code);
        Assert.Equal("Hello There", Text(page));
    }

    [Fact]
    public void Strict_mode_throws_on_the_first_diagnostic_and_leaves_the_page_unchanged()
    {
        byte[] file = ContentSamples.OnePage(
            "BT /F1 12 Tf 72 700 Td /Span << /Alt (A greeting) >> BDC (Hello) Tj EMC ET"u8.ToArray(), "/Font << /F1 5 0 R >>", RedactionSamples.Helvetica);
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());
        PdfPage page = document.Pages[0];
        string before = RedactionSamples.Content(document, page);

        DiagnosticException thrown = Assert.Throws<DiagnosticException>(() => page.ReplaceText(Single(page, "Hello"), "Bye"));

        Assert.Equal(TextEditDiagnostics.TextEditReplacementTextRemoved.Name, thrown.Diagnostic.Code);
        Assert.Equal(before, RedactionSamples.Content(document, page));
        Assert.Equal("Hello", Text(page));
    }

    [Fact]
    public void A_match_on_another_page_is_rejected()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Hello) Tj ET");
        TextMatch match = Single(document.Pages[0], "Hello") with { PageIndex = 3 };

        Assert.Throws<ArgumentException>(() => document.Pages[0].ReplaceText(match, "x"));
        Assert.Throws<ArgumentNullException>(() => document.Pages[0].ReplaceText(match with { PageIndex = 0 }, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfTextReplaceOptions().WithFit((PdfTextFit)9));
    }

    internal static TextMatch Single(PdfPage page, string text) => Assert.Single(page.FindText(new TextQuery(text).WithMatchCase()));

    internal static string Text(PdfPage page) => page.ExtractText().Text.Trim();

    internal static byte[] Save(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithIncrementalUpdate(false).WithRemoveUnreachableObjects(true));
        return stream.ToArray();
    }

    /// <summary>A page with <paramref name="text"/> shown by the canvas in <see cref="TextSamples.FontProgram"/>: a Type 0 font, Identity-H, a subset with ToUnicode.</summary>
    internal static byte[] CanvasPage(string text)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        PdfCanvasFont font = document.EmbedTrueTypeFont(TextSamples.FontProgram);
        using (PdfCanvas canvas = page.CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(font, 12);
            canvas.MoveText(72, 700);
            canvas.ShowText(text);
            canvas.EndText();
        }

        return Save(document);
    }

    /// <summary>A page whose <c>/F1</c> is a TrueType font with WinAnsiEncoding whose embedded program has outlines only for <paramref name="kept"/>.</summary>
    internal static byte[] SubsetPage(string content, string kept, string resources = "", params string[] objects)
    {
        var builder = new TrueTypeBuilder { Cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((32, 126, 1 - 32)))) };
        builder.Glyphs.Add([]);
        for (int code = 32; code <= 126; code++)
        {
            builder.Glyphs.Add(kept.Contains((char)code, StringComparison.Ordinal) && code != 32 ? TrueTypeBuilder.Rectangle(50, 0, 450, 600 + (code % 5 * 20)) : []);
        }

        builder.Metrics = [.. Enumerable.Range(0, builder.Glyphs.Count).Select(static glyph => ((ushort)500, (short)50))];
        byte[] program = builder.Build();
        string widths = string.Join(' ', Enumerable.Repeat("500", 95));
        string font = $"<< /Type /Font /Subtype /TrueType /BaseFont /ABCDEF+Subset /FirstChar 32 /LastChar 126 /Widths [{widths}] /Encoding /WinAnsiEncoding /FontDescriptor 6 0 R >>";
        const string descriptor = "<< /Type /FontDescriptor /FontName /ABCDEF+Subset /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 7 0 R >>";
        string stream = string.Create(CultureInfo.InvariantCulture, $"<< /Length {program.Length} /Length1 {program.Length} >>\nstream\n") + Encoding.Latin1.GetString(program) + "\nendstream";
        return ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), "/Font << /F1 5 0 R >> " + resources, [font, descriptor, stream, .. objects]);
    }

    /// <summary>A page whose <c>/T3</c> is a Type 3 font with procedures for a, b and c only.</summary>
    internal static byte[] Type3Page(string content)
    {
        string font = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 6 0 R /b 7 0 R /c 8 0 R >> "
            + "/Encoding << /Type /Encoding /Differences [97 /a /b /c /d /e /f /g /h /i /j /k /l /m /n /o /p /q /r /s /t /u /v /w /x /y /z] >> /FirstChar 97 /LastChar 122 /Widths ["
            + string.Join(' ', Enumerable.Repeat("600", 26)) + "] >>";
        return ContentSamples.OnePage(
            Encoding.Latin1.GetBytes(content),
            "/Font << /T3 5 0 R >>",
            font,
            ContentSamples.Stream("600 0 0 0 500 500 d1 0 0 500 500 re f"),
            ContentSamples.Stream("600 0 0 0 500 500 d1 0 0 250 500 re f"),
            ContentSamples.Stream("600 0 0 0 500 500 d1 0 0 500 250 re f"));
    }

    /// <summary>Two pages that paint one form XObject showing "Draft" in Helvetica.</summary>
    internal static byte[] SharedFormFile() => ContentSamples.File(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /XObject << /Fx 6 0 R >> >> /Contents 5 0 R >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /XObject << /Fx 6 0 R >> >> /Contents 5 0 R >>",
        ContentSamples.Stream("q 1 0 0 1 72 700 cm /Fx Do Q"),
        ContentSamples.Stream("BT /F1 12 Tf 0 0 Td (Draft) Tj ET", "/Type /XObject /Subtype /Form /BBox [0 -5 100 20] /Resources << /Font << /F1 7 0 R >> >>"),
        RedactionSamples.Helvetica,
    ]);

    private static TextGlyph Glyph(PdfPage page, char code, int skip = 0) =>
        page.ExtractText().Glyphs.Where(glyph => glyph.CharacterCode == code).Skip(skip).First();

    private static void AssertSameOrigins(List<TextGlyph> before, List<TextGlyph> after)
    {
        Assert.Equal(before.Count, after.Count);
        for (int index = 0; index < before.Count; index++)
        {
            Assert.Equal(before[index].Origin.X, after[index].Origin.X, 3);
            Assert.Equal(before[index].Origin.Y, after[index].Origin.Y, 3);
        }
    }
}
