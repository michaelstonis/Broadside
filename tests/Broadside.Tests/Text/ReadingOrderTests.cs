using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Text of tagged pages in logical content order: the depth-first order of the structure tree, artifacts left out, element-level
/// replacement text applied. ISO 32000-2 §14.7, §14.8.2.2, §14.8.2.5 and §14.9.
/// </summary>
public sealed class ReadingOrderTests
{
    [Fact]
    public void A_tagged_page_extracts_in_structure_order_where_its_content_paints_the_table_first()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));

        TextPage text = document.Pages[0].ExtractText();

        // The content paints the table (MCIDs 6-9) before the heading; the tree reads H1, P, L, Table.
        Assert.Equal("Broadside\nA paragraph with a link.\n1. First item\n2. Second item\nName Value\nAlpha 1", text.PlainText);
    }

    [Fact]
    public void Content_order_on_request_keeps_the_order_the_content_paints()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));

        TextPage text = document.Pages[0].ExtractText(new TextExtractionOptions().WithReadingOrder(TextReadingOrder.Content));

        Assert.Equal("Name Value\nAlpha 1\nBroadside\nA paragraph with a link.\n1. First item\n2. Second item", text.PlainText);
    }

    [Fact]
    public void The_pagination_artifact_is_left_out_by_default_and_included_and_flagged_after_the_structure_on_request()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));

        TextPage without = document.Pages[0].ExtractText();
        TextPage with = document.Pages[0].ExtractText(new TextExtractionOptions().WithArtifacts());

        // "/Artifact <</Type /Pagination /Subtype /PageNum>> BDC BT /F1 10 Tf 300 40 Td (1) Tj ET EMC", painted first, is the only artifact.
        Assert.DoesNotContain(without.Glyphs, glyph => glyph.Flags.HasFlag(TextGlyphFlags.Artifact));
        TextGlyph artifact = Assert.Single(with.Glyphs, glyph => glyph.Flags.HasFlag(TextGlyphFlags.Artifact));
        Assert.Equal(with.Glyphs[^1], artifact);
        Assert.Equal("1", with.GetGlyphText(artifact).ToString());
        Assert.Equal(300, artifact.Origin.X, 6);
        Assert.Null(artifact.StructureElement);
        Assert.Equal(without.Glyphs.Count + 1, with.Glyphs.Count);
        Assert.Equal(without.PlainText + "\n1", with.PlainText);
    }

    [Fact]
    public void Each_glyph_carries_the_element_that_owns_its_sequence_and_the_language_inherited_from_the_catalog()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));

        TextPage text = document.Pages[0].ExtractText();

        Assert.Equal(["H1", "Para", "Lbl", "LBody", "TH", "TD"], text.Glyphs.Select(glyph => glyph.StructureElement!.StructureType!.Value).Distinct());
        Assert.All(text.Glyphs, glyph => Assert.Equal("en-US", glyph.Language));
    }

    [Fact]
    public void Element_ActualText_forms_and_artifacts_apply_in_logical_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(Replacements));

        TextPage text = document.Pages[0].ExtractText();

        // The tree reads H1, the ActualText Span over "Druk-" and "ker", the E Span and "Healwell", the Figure, the Artifact element
        // (left out), form Fm1 as an object content item, MCID 0 of form Fm2, then "Last"; "Stray" is in no element (an artifact).
        Assert.Equal("Title\nDrucker\nDr. Healwell\n*\nIn a form\nForm text\nLast", text.PlainText);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Content_order_shows_every_glyph_as_painted_with_no_structure_applied()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(Replacements));

        TextPage text = document.Pages[0].ExtractText(new TextExtractionOptions().WithReadingOrder(TextReadingOrder.Content));

        Assert.Equal("Last\nForm text\nStray\n*\nDr. Healwell\nTitle\nDruk-\nker\n12\nIn a form", text.PlainText);
        Assert.All(text.Glyphs, glyph => Assert.Null(glyph.StructureElement));
    }

    [Fact]
    public void An_element_ActualText_is_carried_by_its_first_glyph_and_the_rest_carry_none()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(Replacements));

        TextPage text = document.Pages[0].ExtractText();

        TextGlyph[] replaced = [.. text.Glyphs.Where(glyph => glyph.Flags.HasFlag(TextGlyphFlags.ActualText))];
        Assert.Equal(8, replaced.Length); // "Druk-" and "ker"
        Assert.Equal("Drucker", text.GetGlyphText(replaced[0]).ToString());
        Assert.All(replaced[1..], glyph => Assert.Equal(0, glyph.TextLength));
        Assert.All(replaced, glyph => Assert.Equal("Span", glyph.StructureElement!.StructureType!.Value));
        TextWord word = Assert.Single(text.Words, word => text.GetWordText(word).SequenceEqual("Drucker"));
        Assert.Equal(8, word.GlyphCount);
    }

    [Fact]
    public void Glyphs_take_the_language_of_their_nearest_element_with_one_else_the_catalog_language()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(Replacements));

        TextPage text = document.Pages[0].ExtractText();

        Assert.Equal(
            ["en-US", "de-DE", "en-US", "en-US", "en-US", "en-US", "en-US"],
            text.Lines.Select(line => text.Glyphs[line.GlyphStart].Language));
    }

    [Fact]
    public void Alternate_descriptions_and_expansions_replace_their_elements_text_as_whole_words_on_request()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(Replacements));

        TextPage text = document.Pages[0].ExtractText(new TextExtractionOptions().WithAlternateDescriptions().WithExpansions());

        Assert.Equal("Title\nDrucker\nDoctor Healwell\nA star\nIn a form\nForm text\nLast", text.PlainText);
        TextGlyph expansion = Assert.Single(text.Glyphs, glyph => glyph.Flags.HasFlag(TextGlyphFlags.Expansion) && glyph.TextLength > 0);
        Assert.Equal("Doctor", text.GetGlyphText(expansion).ToString());
        TextGlyph alternate = Assert.Single(text.Glyphs, glyph => glyph.Flags.HasFlag(TextGlyphFlags.AlternateDescription));
        Assert.Equal("A star", text.GetGlyphText(alternate).ToString());
    }

    [Fact]
    public void Artifacts_on_request_keep_the_artifact_element_in_place_and_put_content_outside_the_tree_last()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(Replacements));

        TextPage text = document.Pages[0].ExtractText(new TextExtractionOptions().WithArtifacts());

        Assert.Equal("Title\nDrucker\nDr. Healwell\n*\n12\nIn a form\nForm text\nLast\nStray", text.PlainText);
        Assert.Equal(
            ["1", "2", "S", "t", "r", "a", "y"],
            text.Glyphs.Where(glyph => glyph.Flags.HasFlag(TextGlyphFlags.Artifact)).Select(glyph => text.GetGlyphText(glyph).ToString()));
        Assert.Equal("Artifact", text.Glyphs.First(glyph => glyph.Flags.HasFlag(TextGlyphFlags.Artifact)).StructureElement!.StructureType!.Value);
    }

    [Fact]
    public void An_element_spanning_two_pages_gives_its_ActualText_once_on_the_page_where_its_content_starts()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(Spanning));

        string[] pages = [.. document.Pages.Select(page => page.ExtractText().PlainText)];

        // The Span's content is "Druk-" (last line of page 1) and "ker" (first line of page 2); §14.9.4: one replacement for both.
        Assert.Equal(["Before\nDrucker\n*", "*\nAfter"], pages);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(string.Join("\n", pages), "Drucker"));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_alternate_description_spanning_two_pages_is_applied_once_on_the_page_where_its_content_starts()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(Spanning));
        TextExtractionOptions options = new TextExtractionOptions().WithAlternateDescriptions();

        string[] pages = [.. document.Pages.Select(page => page.ExtractText(options).PlainText)];

        Assert.Equal(["Before\nDrucker\nTwo stars", "After"], pages);
    }

    [Fact]
    public void The_later_page_of_a_spanning_element_keeps_its_glyphs_flagged_with_no_text()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(Spanning));

        TextPage text = document.Pages[1].ExtractText(new TextExtractionOptions().WithAlternateDescriptions());

        // The continuation: "ker" and "*" are still glyphs of the page, in logical order, owned by their elements and flagged with
        // the replacement that stands for them, but each has an empty text range, so the page's text holds only "After".
        Assert.Equal("After", text.Text);
        Assert.Equal(9, text.Glyphs.Count);
        TextGlyph[] continuation = [.. text.Glyphs.Take(4)];
        Assert.All(continuation, glyph => Assert.Equal(0, glyph.TextLength));
        Assert.Equal(["Span", "Span", "Span", "Figure"], continuation.Select(glyph => glyph.StructureElement!.StructureType!.Value));
        Assert.All(continuation[..3], glyph => Assert.Equal(TextGlyphFlags.ActualText, glyph.Flags));
        Assert.Equal(TextGlyphFlags.AlternateDescription, continuation[3].Flags);
        Assert.Equal(700, continuation[0].Origin.Y, 6);
        TextWord word = Assert.Single(text.Words);
        Assert.Equal((4, 5), (word.GlyphStart, word.GlyphCount));
    }

    [Fact]
    public void An_element_whose_first_content_item_shows_no_glyph_gives_its_ActualText_once_on_the_page_of_its_first_glyph()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(SpanningImage));

        string[] pages = [.. document.Pages.Select(page => page.ExtractText().PlainText)];

        // The Span's content starts with an image on page 1 (no glyph to carry the replacement) and goes on with "ACME" on page 2;
        // §14.9.4: its ActualText stands for all of it once, on the page where it first shows a glyph.
        Assert.Equal(["Before", "ACME Corp\nAfter"], pages);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(string.Join("\n", pages), "ACME"));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_page_carrying_a_replacement_does_not_depend_on_the_order_the_pages_are_read_in()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(SpanningImage));

        TextPage second = document.Pages[1].ExtractText();
        TextPage first = document.Pages[0].ExtractText();

        Assert.Equal("ACME Corp\nAfter", second.PlainText);
        Assert.Equal(TextGlyphFlags.ActualText, second.Glyphs[0].Flags);
        Assert.Equal("Span", second.Glyphs[0].StructureElement!.StructureType!.Value);
        Assert.Equal("Before", first.PlainText);
    }

    [Fact]
    public void Content_edited_to_show_a_glyph_earlier_moves_the_replacement_to_that_page()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(SpanningImage));
        Assert.Equal("ACME Corp\nAfter", document.Pages[1].ExtractText().PlainText);

        // Page 1's MCID 1 now shows text instead of the image: the Span's first glyph, and so its replacement, is on page 1.
        var contents = (Broadside.Objects.CosStream)document.Resolve(document.Pages[0].Dictionary[new Broadside.Objects.CosName("Contents")]);
        contents.EncodedData = System.Text.Encoding.ASCII.GetBytes(
            "/P <</MCID 0>> BDC BT /F1 12 Tf 72 700 Td (Before) Tj ET EMC /Span <</MCID 1>> BDC BT /F1 12 Tf 72 680 Td (Logo) Tj ET EMC");

        Assert.Equal("After", document.Pages[1].ExtractText().PlainText);
        Assert.Equal("Before\nACME Corp", document.Pages[0].ExtractText().PlainText);
    }

    [Fact]
    public void An_element_removed_as_an_object_no_longer_gives_its_replacement()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(SpanningImage));
        Assert.Equal("ACME Corp\nAfter", document.Pages[1].ExtractText().PlainText);

        // §7.3.10: the Span carrying the ActualText (object 13) is removed; its parent's K and the parent tree still name it.
        Assert.True(document.RemoveObject(new Broadside.Objects.CosReference(13, 0)));

        Assert.DoesNotContain("Corp", document.Pages[1].ExtractText().PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_content_stream_removed_as_an_object_moves_the_replacement_back_to_its_later_glyph()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(SpanningImage));
        var contents = (Broadside.Objects.CosStream)document.Resolve(document.Pages[0].Dictionary[new Broadside.Objects.CosName("Contents")]);
        contents.EncodedData = System.Text.Encoding.ASCII.GetBytes(
            "/P <</MCID 0>> BDC BT /F1 12 Tf 72 700 Td (Before) Tj ET EMC /Span <</MCID 1>> BDC BT /F1 12 Tf 72 680 Td (Logo) Tj ET EMC");
        Assert.Equal("After", document.Pages[1].ExtractText().PlainText);

        // Page 1's content stream (object 4) is removed: page 1 shows nothing, so the Span's first glyph is "ACME" on page 2 again.
        Assert.True(document.RemoveObject(new Broadside.Objects.CosReference(4, 0)));

        Assert.Equal("ACME Corp\nAfter", document.Pages[1].ExtractText().PlainText);
    }

    [Fact]
    public void A_page_of_a_document_without_a_structure_tree_keeps_content_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-lines-rotated.pdf"));

        TextPage structure = document.Pages[0].ExtractText();
        TextPage content = document.Pages[0].ExtractText(new TextExtractionOptions().WithReadingOrder(TextReadingOrder.Content));

        Assert.Equal(content.Text, structure.Text);
        Assert.Equal(content.Glyphs, structure.Glyphs);
    }

    [Fact]
    public void Two_touching_elements_with_alternate_descriptions_read_as_two_words()
    {
        using PdfDocument document = PdfDocument.Open(TwoTouchingSpans("Alt"));

        TextPage text = document.Pages[0].ExtractText(new TextExtractionOptions().WithAlternateDescriptions());

        // §14.9.3: consecutive elements with Alt are treated as if a word break were present between them.
        Assert.Equal("x y", text.PlainText);
    }

    [Fact]
    public void Two_touching_elements_with_expansions_read_as_two_words()
    {
        using PdfDocument document = PdfDocument.Open(TwoTouchingSpans("E"));

        TextPage text = document.Pages[0].ExtractText(new TextExtractionOptions().WithExpansions());

        Assert.Equal("x y", text.PlainText);
    }

    [Fact]
    public void Two_elements_with_ActualText_read_as_one_word_even_with_a_gap_between_them()
    {
        using PdfDocument document = PdfDocument.Open(TwoTouchingSpans("ActualText", gap: 50));

        TextPage text = document.Pages[0].ExtractText();

        // §14.9.4: consecutive elements with ActualText are treated as if no word break were present between them.
        Assert.Equal("xy", text.PlainText);
    }

    [Fact]
    public void Without_the_option_alternate_descriptions_leave_the_shown_text()
    {
        using PdfDocument document = PdfDocument.Open(TwoTouchingSpans("Alt"));

        TextPage text = document.Pages[0].ExtractText();

        Assert.Equal("abcd", text.PlainText);
    }

    [Fact]
    public void Consecutive_extractions_on_one_thread_each_follow_their_own_artifact_and_reading_order_options()
    {
        // PdfPage.ExtractText reuses one extractor per thread (#284): each call must read its options afresh, never keep the last call's.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfPage page = document.Pages[0];
        TextExtractionOptions[] sequence =
        [
            new TextExtractionOptions().WithArtifacts().WithReadingOrder(TextReadingOrder.Content),
            new TextExtractionOptions(),
            new TextExtractionOptions().WithArtifacts(),
            new TextExtractionOptions().WithReadingOrder(TextReadingOrder.Content),
            new TextExtractionOptions().WithArtifacts().WithReadingOrder(TextReadingOrder.Content),
        ];

        // What each option set gives on a thread whose extractor has never run.
        string[] fresh = sequence.Select(options => OnNewThread(() => page.ExtractText(options).PlainText)).ToArray();

        string[] reused = sequence.Select(options => page.ExtractText(options).PlainText).ToArray();
        TextPage[] artifactCheck = sequence.Select(page.ExtractText).ToArray();

        Assert.Equal(fresh, reused);
        string structure = "Broadside\nA paragraph with a link.\n1. First item\n2. Second item\nName Value\nAlpha 1";
        Assert.Equal(structure, reused[1]);
        Assert.Equal(structure + "\n1", reused[2]);
        Assert.Equal("Name Value\nAlpha 1\nBroadside\nA paragraph with a link.\n1. First item\n2. Second item", reused[3]);
        Assert.Equal(reused[0], reused[4]);
        for (int i = 0; i < sequence.Length; i++)
        {
            Assert.Equal(sequence[i].IncludeArtifacts ? 1 : 0, artifactCheck[i].Glyphs.Count(glyph => glyph.Flags.HasFlag(TextGlyphFlags.Artifact)));
            Assert.Equal(sequence[i].ReadingOrder == TextReadingOrder.Structure, artifactCheck[i].Glyphs.Any(glyph => glyph.StructureElement is not null));
        }
    }

    /// <summary>Runs <paramref name="action"/> on a thread of its own, so it starts with none of this thread's reused extraction state.</summary>
    private static T OnNewThread<T>(Func<T> action) =>
        Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).GetAwaiter().GetResult();

    private const string Replacements = "text-tagged-replacements.pdf";

    private const string Spanning = "text-tagged-spanning.pdf";

    private const string SpanningImage = "text-tagged-spanning-image.pdf";

    /// <summary>A tagged page showing "ab" and "cd" as two Span elements (MCIDs 0 and 1) whose <paramref name="key"/> entries are "x" and "y".</summary>
    private static byte[] TwoTouchingSpans(string key, int gap = 0) => new Document.TestPdf { Header = "%PDF-2.0" }.Build(
        "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 6 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R /StructParents 0 >>",
        Content.ContentPdf.Stream($"BT /F1 12 Tf 72 700 Td /Span <</MCID 0>> BDC (ab) Tj EMC /Span <</MCID 1>> BDC [-{gap * 1000 / 12}] TJ (cd) Tj EMC ET"),
        Content.ContentPdf.Helvetica,
        "<< /Type /StructTreeRoot /K [7 0 R 8 0 R] /ParentTree << /Nums [0 [7 0 R 8 0 R]] >> >>",
        $"<< /Type /StructElem /S /Span /P 6 0 R /Pg 3 0 R /K 0 /{key} (x) >>",
        $"<< /Type /StructElem /S /Span /P 6 0 R /Pg 3 0 R /K 1 /{key} (y) >>");
}
