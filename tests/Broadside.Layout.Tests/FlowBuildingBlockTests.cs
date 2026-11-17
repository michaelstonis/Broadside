using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Layout.Flow;
using Broadside.Structure;
using Broadside.Tests.Writing;

namespace Broadside.Layout.Tests;

/// <summary>
/// The container basics of flow layout (issue #397, E7 and E5): page breaks, spacers, rules, groups kept together, blocks kept with the
/// next, and columns side by side. Each is laid out through <see cref="FlowDocument"/>, saved and re-read; tagged output keeps every painted
/// mark tagged or an artifact (ISO 32000-2 §14.8.2.2), rules are layout artifacts, a tagged group is one <c>Div</c>, and each column one
/// <c>Div</c> (§14.8.4.4) that stays one element when it continues on the next page.
/// </summary>
public sealed class FlowBuildingBlockTests
{
    /// <summary>Lines of 10-point text (12-point advance) on a Letter page with one-inch margins: 648 / 12.</summary>
    private const int LinesPerPage = 54;

    [Fact]
    public void A_page_break_starts_the_next_block_on_a_new_page()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph("One", body)).Add(new PageBreak()).Add(new Paragraph("Two", body));

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(["One", "Two"], result.Pages.Select(PageText));
    }

    [Fact]
    public void A_page_break_before_any_content_adds_no_page_and_two_in_a_row_leave_a_blank_page()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new PageBreak()).Add(new Paragraph("One", body)).Add(new PageBreak()).Add(new PageBreak()).Add(new Paragraph("Two", body));
        flow.StartSection(new PageTemplate(PageSetup.Letter));
        flow.Add(new PageBreak()).Add(new Paragraph("Three", body));

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(["One", string.Empty, "Two", "Three"], result.Pages.Select(PageText));
    }

    [Fact]
    public void A_spacer_moves_what_follows_down_and_is_dropped_at_the_top_of_a_page()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Spacer(30)).Add(new Paragraph("A", body)).Add(new Spacer(100)).Add(new Paragraph("B", body));
        var plain = new FlowDocument(PageSetup.Letter);
        plain.Add(new Paragraph("A", body)).Add(new Paragraph("B", body));

        PdfPage spaced = flow.Render(document).Pages[0];
        PdfPage reference = plain.Render(document).Pages[0];

        FlowContentRecorder withSpace = FlowContentRecorder.Record(spaced);
        FlowContentRecorder without = FlowContentRecorder.Record(reference);
        Assert.Equal(without.Glyphs[0].Y, withSpace.Glyphs[0].Y, 4);
        Assert.Equal(without.Glyphs[1].Y - 100, withSpace.Glyphs[1].Y, 4);
    }

    [Fact]
    public void A_rule_strokes_the_content_width_as_a_layout_artifact_and_adds_no_element()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new Paragraph("Above", body)).Add(new Rule { Thickness = 2 }).Add(new Paragraph("Below", body));

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[0]);
        (PdfRectangle bounds, double lineWidth) = Assert.Single(recorder.Strokes);
        Assert.Equal(72, bounds.Left, 4);
        Assert.Equal(540, bounds.Right, 4);
        Assert.Equal(2, lineWidth, 4);
        Assert.Contains("Layout/", recorder.Artifacts);
        Assert.Equal(0, recorder.Untagged);
        Assert.Equal("Document(P,P)", Shape(reopened.StructureTree!.Children));
    }

    [Fact]
    public void A_group_kept_together_moves_to_the_next_page_instead_of_splitting()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        FlowDocument Build(bool keep)
        {
            var flow = new FlowDocument(PageSetup.Letter);
            AddLines(flow, body, "Line", LinesPerPage - 4);
            flow.Add(new BlockGroup(Lines(body, "Kept", 6)) { KeepTogether = keep });
            return flow;
        }

        FlowLayoutResult split = Build(keep: false).Render(document);
        FlowLayoutResult kept = Build(keep: true).Render(document);

        Assert.Contains("Kept 1", PageText(split.Pages[0]), StringComparison.Ordinal);
        Assert.StartsWith("Kept 5", PageText(split.Pages[1]), StringComparison.Ordinal);
        Assert.DoesNotContain("Kept", PageText(kept.Pages[0]), StringComparison.Ordinal);
        Assert.StartsWith("Kept 1", PageText(kept.Pages[1]), StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_group_kept_together_taller_than_a_page_breaks_anyway_and_is_reported()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph("Before", body));
        flow.Add(new BlockGroup(Lines(body, "Kept", LinesPerPage + 6)) { KeepTogether = true });

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.StartsWith("Before Kept 1", PageText(result.Pages[0]), StringComparison.Ordinal);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(LayoutDiagnostics.KeepTogetherTooTall, diagnostic.Kind);
        Assert.Same(diagnostic, Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void A_heading_kept_with_the_next_block_moves_with_it_to_the_next_page()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        FlowDocument Build(bool keep)
        {
            var flow = new FlowDocument(PageSetup.Letter);
            AddLines(flow, body, "Line", LinesPerPage - 2);
            flow.Add(new Paragraph("Chapter", body) { StructureType = "H1", KeepWithNext = keep });
            flow.Add(new Paragraph("Section", body) { StructureType = "H2", KeepWithNext = keep });
            flow.Add(new Paragraph("Body text", body));
            return flow;
        }

        FlowLayoutResult loose = Build(keep: false).Render(document);
        FlowLayoutResult kept = Build(keep: true).Render(document);

        Assert.EndsWith("Chapter Section", PageText(loose.Pages[0]), StringComparison.Ordinal);
        Assert.Equal("Body text", PageText(loose.Pages[1]));
        Assert.EndsWith($"Line {LinesPerPage - 2}", PageText(kept.Pages[0]), StringComparison.Ordinal);
        Assert.Equal("Chapter Section Body text", PageText(kept.Pages[1]));
    }

    [Fact]
    public void A_block_kept_with_the_next_inside_a_list_item_moves_too()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        AddLines(flow, body, "Line", LinesPerPage - 1);
        var list = new ListBlock(new ListStyle(new TextStyle(body.Font, 10)));
        list.Add(new ListItem(new Paragraph("Term", body) { KeepWithNext = true }, new Paragraph("Definition", body)));
        flow.Add(list);

        FlowLayoutResult result = flow.Render(document);

        Assert.DoesNotContain("Term", PageText(result.Pages[0]), StringComparison.Ordinal);
        Assert.Contains("Term Definition", PageText(result.Pages[1]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tagged_group_is_one_Div_and_an_untagged_group_adds_no_element()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new BlockGroup(new Paragraph("Name", body), new Paragraph("Street", body)) { IsTagged = true });
        flow.Add(new BlockGroup(new Paragraph("Loose", body)));
        flow.Add(new BlockGroup(new Paragraph("Aside", body)) { IsTagged = true, StructureType = "Aside" });

        flow.Render(document);

        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal("Document(Div(P,P),P,Aside(P))", Shape(reopened.StructureTree!.Children));
        Assert.Equal(0, FlowContentRecorder.Record(reopened.Pages[0]).Untagged);
    }

    [Fact]
    public void Columns_sit_side_by_side_from_one_top_and_the_flow_continues_below_the_longest()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(new ColumnsBlock(
            new FlowColumn(ColumnWidth.Fixed(100), new Paragraph("Left", body), new Paragraph("Left again", body)),
            new FlowColumn(new Paragraph("Right", body)))
        { Gap = 20 });
        flow.Add(new Paragraph("After", body));

        flow.Render(document);

        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[0]);
        RecordedGlyph left = recorder.Glyphs.First(glyph => glyph.Text == "L");
        RecordedGlyph right = recorder.Glyphs.First(glyph => glyph.Text == "R");
        RecordedGlyph after = recorder.Glyphs.First(glyph => glyph.Text == "A");
        Assert.Equal(72, left.X, 4);
        Assert.Equal(72 + 100 + 20, right.X, 4);
        Assert.Equal(left.Y, right.Y, 4);
        Assert.Equal(left.Y - 24, after.Y, 4);
        Assert.Equal(0, recorder.Untagged);
        Assert.Equal("Document(Div(P,P),Div(P),P)", Shape(reopened.StructureTree!.Children));
    }

    [Fact]
    public void A_column_longer_than_the_page_continues_on_the_next_and_stays_one_Div()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        AddLines(flow, body, "Line", LinesPerPage - 4);
        flow.Add(new ColumnsBlock(new FlowColumn(Lines(body, "Left", 10)), new FlowColumn(Lines(body, "Right", 2))));
        flow.Add(new Paragraph("After", body));

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Contains("Left 4", PageText(result.Pages[0]), StringComparison.Ordinal);
        Assert.Contains("Right 2", PageText(result.Pages[0]), StringComparison.Ordinal);
        Assert.Equal("Left 5 Left 6 Left 7 Left 8 Left 9 Left 10 After", PageText(result.Pages[1]));
        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfStructureElement[] divs = [.. reopened.StructureTree!.Elements.Where(element => element.StructureType?.Value == "Div")];
        Assert.Equal(2, divs.Length);
        Assert.Equal(10, divs[0].Children.OfType<PdfStructureElement>().Count());
        Assert.Equal(2, divs[1].Children.OfType<PdfStructureElement>().Count());
        Assert.All(reopened.Pages, page => Assert.Equal(0, FlowContentRecorder.Record(page).Untagged));
    }

    [Fact]
    public void The_same_columns_added_twice_are_two_sets_of_elements()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var columns = new ColumnsBlock(new FlowColumn(new Paragraph("Left", body)), new FlowColumn(new Paragraph("Right", body)));
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = FlowTagging.Default };
        flow.Add(columns).Add(columns);

        flow.Render(document);

        Assert.Equal("Document(Div(P),Div(P),Div(P),Div(P))", Shape(document.StructureTree!.Children));
    }

    [Fact]
    public void Columns_whose_first_lines_do_not_fit_start_on_the_next_page()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        AddLines(flow, body, "Line", LinesPerPage - 1);
        flow.Add(new Spacer(6));
        flow.Add(new ColumnsBlock(new FlowColumn(new Paragraph("Left", body)), new FlowColumn(new Paragraph("Right", body))));

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal("Left Right", PageText(result.Pages[1]));
    }

    [Fact]
    public void A_page_break_inside_a_column_does_nothing()
    {
        using PdfDocument document = NewDocument();
        ParagraphStyle body = Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new ColumnsBlock(new FlowColumn(new Paragraph("Left", body), new PageBreak(), new Paragraph("More", body))));

        Assert.Single(flow.Render(document).Pages);
    }

    private static ParagraphStyle Body(PdfDocument document) => new(document.GetStandard14Font(Standard14Font.Helvetica), 10);

    private static Paragraph[] Lines(ParagraphStyle style, string prefix, int count) =>
        [.. Enumerable.Range(1, count).Select(number => new Paragraph($"{prefix} {number}", style))];

    private static void AddLines(FlowDocument flow, ParagraphStyle style, string prefix, int count)
    {
        foreach (Paragraph line in Lines(style, prefix, count))
        {
            flow.Add(line);
        }
    }

    private static string PageText(PdfPage page) =>
        string.Join(' ', page.ExtractText().PlainText.Split((char[])['\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries));

    internal static string Shape(IEnumerable<PdfStructureItem> items) => string.Join(',', items.OfType<PdfStructureElement>().Select(element =>
    {
        string children = Shape(element.Children);
        return children.Length == 0 ? element.StructureType!.Value : $"{element.StructureType!.Value}({children})";
    }));

    internal static void AssertQpdfPasses(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    internal static PdfDocument NewDocument()
    {
        PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        return document;
    }

    internal static byte[] Saved(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
