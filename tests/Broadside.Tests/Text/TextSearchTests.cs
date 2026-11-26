using System.Text;
using System.Text.RegularExpressions;
using Broadside.Graphics;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>Text search over extracted text (issue #623): literal and regular-expression queries, match glyphs and quads.</summary>
public sealed class TextSearchTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_literal_query_ignores_case_and_diacritics_by_default_and_matches_in_reading_order()
    {
        using PdfDocument document = PdfDocument.Open(Lines(["Café one", "cafe two CAFE"]));

        IReadOnlyList<TextMatch> matches = document.Pages[0].ExtractText().Find(new TextQuery("cafe"));

        Assert.Equal(["Café", "cafe", "CAFE"], matches.Select(match => match.Text));
        Assert.All(matches, match => Assert.Equal(-1, match.PageIndex)); // a text page on its own does not know its page
        Assert.All(document.Pages[0].FindText(new TextQuery("cafe")), match => Assert.Equal(0, match.PageIndex));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Matching_case_and_diacritics_narrows_the_results_and_the_query_folds_as_the_text_does()
    {
        using PdfDocument document = PdfDocument.Open(Lines(["Café one", "cafe two CAFE"]));
        TextPage page = document.Pages[0].ExtractText();

        Assert.Equal(["cafe"], page.Find(new TextQuery("cafe").WithMatchCase()).Select(match => match.Text));
        Assert.Equal(["Café"], page.Find(new TextQuery("café").WithMatchDiacritics()).Select(match => match.Text));

        // A decomposed query (e and a combining acute) is the same text, and compatibility characters fold to what they stand for
        // whatever the host's globalization data: full-width letters.
        Assert.Equal(["Café"], page.Find(new TextQuery("café").WithMatchDiacritics()).Select(match => match.Text));
        Assert.Equal(["cafe"], page.Find(new TextQuery("ｃａｆｅ").WithMatchCase()).Select(match => match.Text));
    }

    [Fact]
    public void Whole_word_matches_only_at_word_boundaries()
    {
        using PdfDocument document = PdfDocument.Open(Lines(["cat concatenate cat, scatter"]));
        TextPage page = document.Pages[0].ExtractText();

        Assert.Equal(4, page.Find(new TextQuery("cat")).Count);
        Assert.Equal([0, 16], page.Find(new TextQuery("cat").WithWholeWord()).Select(match => match.TextStart));
        Assert.Equal(["cat", "cat"], page.Find(TextQuery.FromRegex("c.t").WithWholeWord()).Select(match => match.Text));
    }

    [Fact]
    public void A_phrase_matches_across_a_line_break_with_one_quad_per_line()
    {
        using PdfDocument document = PdfDocument.Open(Lines(["alpha beta", "gamma delta"]));
        TextPage page = document.Pages[0].ExtractText();

        TextMatch beta = Assert.Single(page.Find(new TextQuery("beta")));
        Quad quad = Assert.Single(beta.Quads);

        // Helvetica advances (AFM): "alpha " is 2,724 units, "alpha beta" 4,670 and "gamma" 3,334, at 12 points.
        const double betaLeft = 72 + 32.688, betaRight = 72 + 56.04;
        Assert.Equal(betaLeft, quad.BottomLeft.X, 3);
        Assert.Equal(betaRight, quad.BottomRight.X, 3);
        Assert.Equal(700 - 2.484, quad.BottomLeft.Y, 3); // the glyph boxes reach down to the descender (-207/1000 em)
        Assert.Equal(quad.Bounds, beta.Bounds);

        // Runs of white space in the query match the break between two lines.
        TextMatch phrase = Assert.Single(page.Find(new TextQuery("  beta \n gamma ")));
        Assert.Equal("beta gamma", phrase.Text);
        Assert.Equal(2, phrase.Quads.Count);
        Assert.Equal(betaLeft, phrase.Quads[0].BottomLeft.X, 3);
        Assert.Equal(72 + 40.008, phrase.Quads[1].BottomRight.X, 3);
        Assert.Equal(686 - 2.484, phrase.Quads[1].BottomLeft.Y, 3);
        Assert.Equal(72, phrase.Bounds.Left, 3); // "gamma" starts the second line
        Assert.Equal(686 - 2.484, phrase.Bounds.Bottom, 3);
        Assert.Equal(betaRight, phrase.Bounds.Right, 3);
    }

    [Fact]
    public void Glyph_start_and_count_index_the_text_pages_glyphs_and_text_start_its_plain_text()
    {
        using PdfDocument document = PdfDocument.Open(Lines(["alpha beta", "gamma delta"]));
        TextPage page = document.Pages[0].ExtractText();

        IReadOnlyList<TextMatch> matches = page.Find(new TextQuery("a"));
        Assert.Equal(6, matches.Count);
        foreach (TextMatch match in matches)
        {
            Assert.Equal(1, match.GlyphCount);
            Assert.Equal("a", page.GetGlyphText(page.Glyphs[match.GlyphStart]).ToString());
            Assert.Equal(match.Text, page.PlainText.Substring(match.TextStart, match.Text.Length));
            Assert.Equal(page.Glyphs[match.GlyphStart].Quad, Assert.Single(match.Quads));
        }

        // The glyphs from the first to the last of a match include the white-space glyph between words; a line break shows none.
        TextMatch phrase = Assert.Single(page.Find(new TextQuery("pha beta gam")));
        Assert.Equal("pha beta" + "gam", GlyphText(page, phrase));
        TextMatch words = Assert.Single(page.Find(new TextQuery("alpha beta")));
        Assert.Equal("alpha beta", GlyphText(page, words));
        Assert.Equal(page.Text.IndexOf("alpha", StringComparison.Ordinal), page.Glyphs[words.GlyphStart].TextStart);
    }

    [Fact]
    public void A_regular_expression_runs_on_the_unfolded_text_and_ignores_case_unless_asked()
    {
        using PdfDocument document = PdfDocument.Open(Lines(["call 555-1234 or 555-9876", "Café CAFE"]));
        TextPage page = document.Pages[0].ExtractText();

        IReadOnlyList<TextMatch> numbers = page.Find(TextQuery.FromRegex(@"\d{3}-\d{4}"));
        Assert.Equal(["555-1234", "555-9876"], numbers.Select(match => match.Text));
        Assert.All(numbers, match => Assert.Equal(8, match.GlyphCount));

        Assert.Equal(["CAFE"], page.Find(TextQuery.FromRegex("caf.").WithMatchCase(false)).Skip(1).Select(match => match.Text));
        Assert.Equal(["CAFE"], page.Find(TextQuery.FromRegex("CAF.").WithMatchCase()).Select(match => match.Text));
        Assert.Equal(["CAFE"], page.Find(TextQuery.FromRegex("cafe")).Select(match => match.Text)); // no folding: "é" is not "e"

        // The breaks between words and lines are single spaces.
        Assert.Equal(["9876 Café"], page.Find(TextQuery.FromRegex(@"\d+ \w+(?= CAFE)")).Select(match => match.Text));
    }

    [Fact]
    public void A_regex_object_keeps_its_case_option_and_timeout_unless_the_query_changes_them()
    {
        using PdfDocument document = PdfDocument.Open(Lines(["Café CAFE"]));
        TextPage page = document.Pages[0].ExtractText();

        TextQuery exact = TextQuery.FromRegex(new Regex("CAF.", RegexOptions.None, TimeSpan.FromSeconds(5)));
        Assert.True(exact.MatchCase);
        Assert.Equal(TimeSpan.FromSeconds(5), exact.RegexTimeout);
        Assert.Equal(["CAFE"], page.Find(exact).Select(match => match.Text));

        TextQuery folded = TextQuery.FromRegex(new Regex("caf.", RegexOptions.IgnoreCase));
        Assert.False(folded.MatchCase);
        Assert.Equal(TimeSpan.FromSeconds(2), folded.RegexTimeout);
        Assert.Equal(2, page.Find(folded).Count);
        Assert.Empty(page.Find(folded.WithMatchCase()));
        Assert.True(folded.IsRegex);
        Assert.Equal("caf.", folded.Pattern);
    }

    [Fact]
    public void A_zero_length_regex_match_is_skipped()
    {
        using PdfDocument document = PdfDocument.Open(Lines(["abc xx"]));
        TextPage page = document.Pages[0].ExtractText();

        Assert.Equal(["xx"], page.Find(TextQuery.FromRegex("x*")).Select(match => match.Text));
        Assert.Empty(page.Find(TextQuery.FromRegex(@"\b")));
    }

    [Fact]
    public void A_regex_that_runs_past_its_timeout_throws_for_that_page_only()
    {
        string digits = string.Join(' ', Enumerable.Repeat("0123456789", 200));
        using PdfDocument document = PdfDocument.Open(Pages(["first 1"], [digits], ["last 1"]));
        TextQuery query = TextQuery.FromRegex(@"\d").WithRegexTimeout(TimeSpan.FromTicks(1));

        Assert.Throws<RegexMatchTimeoutException>(() => document.Pages[1].FindText(query));
        Assert.Throws<RegexMatchTimeoutException>(() => document.FindText(query, 1..2, Token).ToList());

        // The document and the query are unchanged: with time enough, every page is searched.
        Assert.Equal(2002, document.FindText(query.WithRegexTimeout(TimeSpan.FromSeconds(30)), cancellationToken: Token).Count());
    }

    [Fact]
    public void An_empty_query_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new TextQuery(string.Empty));
        Assert.Throws<ArgumentException>(() => new TextQuery(" \n\t"));
        Assert.Throws<ArgumentNullException>(() => new TextQuery(null!));
        Assert.Throws<ArgumentException>(() => TextQuery.FromRegex(string.Empty));
        Assert.ThrowsAny<ArgumentException>(() => TextQuery.FromRegex("(unclosed"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextQuery("x").WithRegexTimeout(TimeSpan.Zero));
        Assert.Equal(Regex.InfiniteMatchTimeout, new TextQuery("x").WithRegexTimeout(Regex.InfiniteMatchTimeout).RegexTimeout);
    }

    [Fact]
    public void A_match_inside_an_ActualText_replacement_covers_every_glyph_it_stands_for()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-tagged-replacements.pdf"));
        TextPage page = document.Pages[0].ExtractText();

        // "Druk-" and "ker" are painted on two lines; the Span's ActualText "Drucker" stands for all eight glyphs (§14.9.4).
        foreach (string part in new[] { "Druck", "ker", "Drucker" })
        {
            TextMatch match = Assert.Single(page.Find(new TextQuery(part).WithWholeWord(part == "Drucker")));
            Assert.Equal(part, match.Text);
            Assert.Equal(8, match.GlyphCount);
            Assert.All(page.Glyphs.Skip(match.GlyphStart).Take(match.GlyphCount), glyph => Assert.True(glyph.Flags.HasFlag(TextGlyphFlags.ActualText)));

            // One quad per painted line, each along its own baseline.
            Assert.Equal(2, match.Quads.Count);
            Assert.True(match.Quads[0].BottomLeft.Y != match.Quads[1].BottomLeft.Y);
        }

        // Read in content order, the painted text is searched instead.
        TextPage painted = document.Pages[0].ExtractText(new TextExtractionOptions().WithReadingOrder(TextReadingOrder.Content));
        TextMatch druk = Assert.Single(painted.Find(new TextQuery("Druk-")));
        Assert.Equal(5, druk.GlyphCount);
        Assert.Empty(painted.Find(new TextQuery("Drucker")));
    }

    [Fact]
    public void Alternate_descriptions_and_expansions_are_searched_when_extraction_applies_them()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-tagged-replacements.pdf"));
        PdfPage page = document.Pages[0];
        TextExtractionOptions options = new TextExtractionOptions().WithAlternateDescriptions().WithExpansions();

        Assert.Empty(page.FindText(new TextQuery("Doctor")));
        TextMatch doctor = Assert.Single(page.FindText(new TextQuery("Doc"), options));
        Assert.Equal(3, doctor.GlyphCount); // the E Span over "Dr."
        TextMatch star = Assert.Single(page.FindText(new TextQuery("star"), options));
        Assert.Equal(1, star.GlyphCount); // the Figure's Alt over "*"
    }

    [Fact]
    public void Artifacts_and_hidden_content_follow_the_extraction_options()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-tagged-replacements.pdf"));
        PdfPage page = document.Pages[0];

        Assert.Empty(page.FindText(new TextQuery("12")));
        Assert.Empty(page.FindText(new TextQuery("Stray")));
        Assert.Single(page.FindText(new TextQuery("12"), new TextExtractionOptions().WithArtifacts()));
        Assert.Single(page.FindText(new TextQuery("Stray"), new TextExtractionOptions().WithArtifacts()));
    }

    [Fact]
    public void Quads_of_rotated_text_run_along_their_own_baselines()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-lines-rotated.pdf"));
        PdfPage page = document.Pages[0];

        TextMatch ninety = Assert.Single(page.FindText(new TextQuery("Ninety degrees")));
        Quad quad = Assert.Single(ninety.Quads);
        Assert.Equal(quad.BottomLeft.X, quad.BottomRight.X, 3); // along (0, 1)
        Assert.True(quad.BottomRight.Y > quad.BottomLeft.Y);

        TextMatch thirty = Assert.Single(page.FindText(new TextQuery("thirty degrees second")));
        Assert.Equal(2, thirty.Quads.Count);
        foreach (Quad line in thirty.Quads)
        {
            double dx = line.BottomRight.X - line.BottomLeft.X, dy = line.BottomRight.Y - line.BottomLeft.Y;
            Assert.Equal(0.5, dy / Math.Sqrt((dx * dx) + (dy * dy)), 3); // along (cos 30, sin 30)
        }

        Assert.Equal(3, page.FindText(new TextQuery("degrees")).Count);
        Assert.Equal(3, page.FindText(new TextQuery("second line")).Count);
    }

    [Fact]
    public void Document_search_is_lazy_page_by_page_and_honors_ranges_and_cancellation()
    {
        using PdfDocument document = PdfDocument.Open(Pages(["x one"], ["two"], ["x three x"]));

        Assert.Throws<ArgumentNullException>(() => document.FindText(null!, cancellationToken: Token));
        IEnumerable<TextMatch> all = document.FindText(new TextQuery("x"), cancellationToken: Token);
        Assert.Equal([0, 2, 2], all.Select(match => match.PageIndex));
        Assert.Equal([2, 2], document.FindText(new TextQuery("x"), 1.., Token).Select(match => match.PageIndex));
        Assert.Equal([0], document.FindText(new TextQuery("x"), ^3..^1, Token).Select(match => match.PageIndex));

        IEnumerable<TextMatch> outside = document.FindText(new TextQuery("x"), 2..5, Token); // checked when enumerated
        Assert.Throws<ArgumentOutOfRangeException>(() => outside.ToList());

        using var source = new CancellationTokenSource();
        using IEnumerator<TextMatch> enumerator = document.FindText(new TextQuery("x"), cancellationToken: source.Token).GetEnumerator();
        Assert.True(enumerator.MoveNext());
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void Concurrent_searches_of_one_text_page_agree()
    {
        string line = string.Join(' ', Enumerable.Range(0, 200).Select(i => $"word{i % 7} Café"));
        using PdfDocument document = PdfDocument.Open(Lines([line, line]));
        TextPage page = document.Pages[0].ExtractText();
        TextQuery[] queries = [new("cafe"), new TextQuery("Café").WithMatchCase().WithMatchDiacritics(), new TextQuery("word3").WithWholeWord(), TextQuery.FromRegex(@"word[0-2]\b")];
        int[] expected = [400, 400, 58, 174];

        Parallel.For(0, 64, i => Assert.Equal(expected[i % 4], page.Find(queries[i % 4]).Count));
    }

    /// <summary>The texts of a match's glyphs, one after the other.</summary>
    private static string GlyphText(TextPage page, TextMatch match) =>
        string.Concat(page.Glyphs.Skip(match.GlyphStart).Take(match.GlyphCount).Select(glyph => page.GetGlyphText(glyph).ToString()));

    private static byte[] Lines(string[] lines) => Pages(lines);

    /// <summary>A file of Letter pages, each showing its lines in Helvetica 12 (WinAnsiEncoding) from (72, 700) down 14 points a line.</summary>
    private static byte[] Pages(params string[][] pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pages.Select((_, i) => $"{4 + (2 * i)} 0 R"))}] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        foreach (string[] lines in pages)
        {
            var content = new StringBuilder("BT /F1 12 Tf 14 TL 72 700 Td");
            for (int i = 0; i < lines.Length; i++)
            {
                content.Append(i == 0 ? " (" : " T* (").Append(lines[i]).Append(") Tj");
            }

            content.Append(" ET");
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {objects.Count + 2} 0 R >>");
            objects.Add(Content.ContentPdf.Stream(content.ToString()));
        }

        return new TestPdf().Build([.. objects]);
    }
}
