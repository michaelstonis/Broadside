using System.Text.Json;
using Broadside.Objects;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Navigation;

/// <summary>
/// Writing the document outline (ISO 32000-2 §12.3.3, Tables 150 to 152): items created, edited, moved and removed with every link
/// and <c>Count</c> kept consistent, read back through <see cref="PdfOutline"/> after a save and checked by qpdf.
/// </summary>
public sealed class OutlineWritingTests
{
    [Fact]
    public void An_outline_built_three_levels_deep_re_reads_with_correct_count_signs_for_open_and_closed_items()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-labels.pdf"));
        PdfOutline outline = document.CreateOutline();

        PdfOutlineItem part1 = outline.Add("Part I");
        PdfOutlineItem chapter1 = part1.Add("Chapter 1");
        chapter1.Add("Section 1.1");
        chapter1.Add("Section 1.2");
        chapter1.IsOpen = false;
        PdfOutlineItem chapter2 = part1.Add("Chapter 2");
        chapter2.Add("Section 2.1");
        PdfOutlineItem part2 = outline.Add("Part II");
        PdfOutlineItem chapter3 = part2.Add("Chapter 3");
        chapter3.Add("Section 3.1");
        chapter3.Add("Section 3.3");
        chapter3.Insert(1, "Section 3.2");
        part2.IsOpen = false;
        outline.Add("Part III");

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);

        // Chapter 1 closed over 2; Chapter 2 open over 1; Part I open over Chapter 1, Chapter 2 and Section 2.1; Chapter 3 open over 3;
        // Part II closed over Chapter 3 and its 3 sections; the outline shows the 3 parts and Part I's 3 visible descendants.
        string[] expected =
        [
            "0 Part I 3",
            "1 Chapter 1 -2",
            "2 Section 1.1 -",
            "2 Section 1.2 -",
            "1 Chapter 2 1",
            "2 Section 2.1 -",
            "0 Part II -4",
            "1 Chapter 3 3",
            "2 Section 3.1 -",
            "2 Section 3.2 -",
            "2 Section 3.3 -",
            "0 Part III -",
        ];
        Assert.Equal(expected, OutlineShape.AssertConsistent(reopened));
        Assert.Equal(6, reopened.Outline!.Count);
        Assert.Equal(
            ["Part I open", "Chapter 1 closed", "Chapter 2 open", "Part II closed", "Chapter 3 open"],
            Flatten(reopened.Outline.Items).Where(item => item.Children.Count > 0).Select(item => $"{item.Title} {(item.IsOpen ? "open" : "closed")}"));
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
        Assert.Equal(["Part I open", "Chapter 1 closed", "Chapter 2 open", "Part II closed", "Chapter 3 open"], QpdfOpenStates(file));
    }

    [Fact]
    public void Moving_items_between_parents_keeps_first_last_prev_next_and_counts_consistent()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        IReadOnlyList<PdfOutlineItem> parts = document.Outline!.Items;
        PdfOutlineItem part1 = parts[0];
        PdfOutlineItem part2 = parts[1];
        PdfOutlineItem chapter1 = part1.Children[0];
        PdfOutlineItem chapter2 = part1.Children[1];
        PdfOutlineItem section11 = chapter1.Children[0];

        // Section 1.1 leaves closed Chapter 1 for the middle of closed Part II; Chapter 2 goes to the top; open Part I, with
        // Chapter 1 now a leaf, goes to the end of closed Part II.
        section11.MoveTo(part2, 1);
        chapter2.MoveTo(null, 0);
        part1.MoveTo(part2, 3);

        Assert.Equal((part2, 1), (section11.Parent, section11.Level));
        Assert.Equal((null, 0), (chapter2.Parent, chapter2.Level));
        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        string[] expected =
        [
            "0 Chapter 2 -",
            "0 Part II -5",
            "1 Chapter 3 – Übersicht -",
            "1 Section 1.1 -",
            "1 Chapter 4 -",
            "1 Part I 1",
            "2 Chapter 1 -",
        ];
        Assert.Equal(expected, OutlineShape.AssertConsistent(reopened));
        Assert.Equal(2, reopened.Outline!.Count);
        _ = Flatten(reopened.Outline.Items).ToList();
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
        Assert.Equal(["Part II closed", "Part I open"], QpdfOpenStates(file));
    }

    [Fact]
    public void Moving_an_item_within_its_level_reorders_it()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        PdfOutlineItem part2 = document.Outline!.Items[1];

        part2.Children[0].MoveTo(part2, 1);
        part2.MoveTo(null, 0);

        Assert.Equal(
            ["0 Part II -2", "1 Chapter 4 -", "1 Chapter 3 – Übersicht -", "0 Part I 2", "1 Chapter 1 -1", "2 Section 1.1 -", "1 Chapter 2 -"],
            OutlineShape.AssertConsistent(document));
        Assert.Equal(4, document.Outline.Count);
    }

    [Fact]
    public void An_item_cannot_move_under_itself_or_past_the_end_of_a_level_and_a_refused_move_changes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        PdfOutlineItem part1 = document.Outline!.Items[0];
        PdfOutlineItem section11 = part1.Children[0].Children[0];
        IReadOnlyList<string> before = OutlineShape.AssertConsistent(document);

        Assert.Throws<ArgumentException>(() => part1.MoveTo(section11, 0));
        Assert.Throws<ArgumentException>(() => part1.MoveTo(part1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => section11.MoveTo(null, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => part1.Children[1].MoveTo(part1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => part1.Insert(3, "Too far"));

        Assert.Equal(before, OutlineShape.AssertConsistent(document));
        Assert.False(document.Trailer.IsDirty || document.Catalog.IsDirty);
    }

    [Fact]
    public void Opening_and_closing_items_rewrites_their_count_signs_and_the_counts_above_them()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        IReadOnlyList<PdfOutlineItem> parts = document.Outline!.Items;

        parts[1].IsOpen = true;
        parts[0].Children[0].IsOpen = true;
        parts[0].Children[1].IsOpen = true;
        parts[0].IsOpen = false;

        // Part I closed over Chapter 1, Section 1.1 (Chapter 1 is open now) and Chapter 2; Part II open over its two chapters.
        Assert.Equal(
            ["0 Part I -3", "1 Chapter 1 1", "2 Section 1.1 -", "1 Chapter 2 -", "0 Part II 2", "1 Chapter 3 – Übersicht -", "1 Chapter 4 -"],
            OutlineShape.AssertConsistent(document));
        Assert.Equal(4, document.Outline.Count);
        Assert.False(parts[0].Children[1].IsOpen);
    }

    [Fact]
    public void Removing_an_item_removes_its_subtree_from_the_outline_and_the_document_in_an_incremental_update()
    {
        byte[] original = File.ReadAllBytes(Corpus.Path("outline-full.pdf"));
        using PdfDocument document = PdfDocument.Open(original);
        PdfOutlineItem part1 = document.Outline!.Items[0];
        CosReference[] removed = [part1.Reference!, part1.Children[0].Reference!, part1.Children[0].Children[0].Reference!, part1.Children[1].Reference!];
        PdfOutlineItem part2 = document.Outline.Items[1];

        part1.Remove();
        part2.Add("Appendix");

        byte[] file = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));
        Assert.Equal(original, file.AsSpan(0, original.Length).ToArray());
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal(["0 Part II -3", "1 Chapter 3 – Übersicht -", "1 Chapter 4 -", "1 Appendix -"], OutlineShape.AssertConsistent(reopened));
        Assert.Equal(1, reopened.Outline!.Count);
        Assert.All(removed, reference => Assert.IsType<CosNull>(reopened.Resolve(reference)));
        Assert.Equal(2, reopened.Revisions.Count);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void Removing_the_only_child_of_an_item_leaves_a_leaf_without_first_last_or_count()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        PdfOutlineItem chapter1 = document.Outline!.Items[0].Children[0];

        chapter1.Children[0].Remove();

        Assert.Equal(["0 Part I 2", "1 Chapter 1 -", "1 Chapter 2 -", "0 Part II -2", "1 Chapter 3 – Übersicht -", "1 Chapter 4 -"], OutlineShape.AssertConsistent(document));
        Assert.Equal(4, document.Outline.Count);
        Assert.Throws<InvalidOperationException>(() => chapter1.Children[0].Remove());
    }

    [Fact]
    public void An_items_title_colour_style_and_destination_are_written_and_re_read()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        IReadOnlyList<PdfOutlineItem> parts = document.Outline!.Items;
        PdfOutlineItem chapter3 = parts[1].Children[0];
        PdfOutlineItem part1 = parts[0];

        part1.Title = "Teil I – Einführung 第一部";
        part1.Color = new PdfRgbColor(0, 0.5, 1);
        part1.Flags = PdfOutlineItemFlags.Bold;
        part1.Destination = document.CreateDestination(document.Pages[0], PdfDestinationView.FitH, top: 700);
        Assert.NotNull(chapter3.Action);
        chapter3.Destination = document.SetNamedDestination("chapter-3", document.CreateDestination(document.Pages[0], PdfDestinationView.Fit));
        chapter3.Color = PdfRgbColor.Black;
        chapter3.Flags = PdfOutlineItemFlags.None;

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfOutlineItem rereadPart1 = reopened.Outline!.Items[0];
        PdfOutlineItem rereadChapter3 = reopened.Outline.Items[1].Children[0];
        Assert.Equal("Teil I – Einführung 第一部", rereadPart1.Title);
        Assert.Equal(new PdfRgbColor(0, 0.5, 1), rereadPart1.Color);
        Assert.True(rereadPart1.IsBold && !rereadPart1.IsItalic);
        PdfExplicitDestination explicitDestination = Assert.IsType<PdfExplicitDestination>(rereadPart1.Destination);
        Assert.Equal((0, PdfDestinationView.FitH, (double?)700), (explicitDestination.PageIndex, explicitDestination.View, explicitDestination.Top));
        PdfNamedDestination named = Assert.IsType<PdfNamedDestination>(rereadChapter3.Destination);
        Assert.Equal("chapter-3", named.Text);
        Assert.Equal(PdfDestinationView.Fit, named.Resolve()!.View);
        Assert.Null(rereadChapter3.Action);
        Assert.False(rereadChapter3.Dictionary.ContainsKey(new CosName("C")) || rereadChapter3.Dictionary.ContainsKey(new CosName("F")));
        _ = Flatten(reopened.Outline.Items).ToList();
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void Item_values_outside_the_clause_are_refused()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        using PdfDocument other = PdfDocument.Open(Corpus.Path("outline.pdf"));
        PdfOutlineItem item = document.Outline!.Items[0];

        Assert.Throws<ArgumentOutOfRangeException>(() => item.Color = new PdfRgbColor(0, 1.5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.Color = new PdfRgbColor(double.NaN, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.Flags = (PdfOutlineItemFlags)4);
        Assert.Throws<ArgumentNullException>(() => item.Title = null!);
        Assert.Throws<ArgumentException>(() => item.Destination = other.Outline!.Items[0].Destination);
        Assert.Throws<ArgumentException>(() => document.CreateDestination(other.Pages[0], PdfDestinationView.Fit));
    }

    [Fact]
    public void Adding_an_item_to_a_damaged_level_relinks_it_from_its_first_next_chain()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-broken.pdf"));
        PdfOutline outline = document.Outline!;

        // The top level's chain runs 4's First 5, 6, 7 and 7's Next back to 6; the outline's Last names 6.
        outline.Add("Appended");

        _ = outline.Items;
        Assert.Equal(["One", string.Empty, "Three", "Appended"], outline.Items.Select(item => item.Title));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "OutlineCycle" && diagnostic.ObjectReference!.ObjectNumber == 6);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "OutlineLinkInconsistent" && diagnostic.ObjectReference!.ObjectNumber == 4);
    }

    private static IEnumerable<PdfOutlineItem> Flatten(IEnumerable<PdfOutlineItem> items) =>
        items.SelectMany(item => (IEnumerable<PdfOutlineItem>)[item, .. Flatten(item.Children)]);

    private static byte[] Save(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Save(output, options ?? new PdfSaveOptions());
        return output.ToArray();
    }

    private static void AssertQpdfPasses(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    /// <summary>The open state qpdf reads for every item with children, depth first, as <c>"title open|closed"</c>.</summary>
    private static List<string> QpdfOpenStates(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--json", "--json-key=outlines");
        Assert.True(exitCode == 0, output);
        using JsonDocument json = JsonDocument.Parse(output);
        var states = new List<string>();
        Walk(json.RootElement.GetProperty("outlines"));
        return states;

        void Walk(JsonElement items)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                JsonElement kids = item.GetProperty("kids");
                if (kids.GetArrayLength() > 0)
                {
                    states.Add($"{item.GetProperty("title").GetString()} {(item.GetProperty("open").GetBoolean() ? "open" : "closed")}");
                }

                Walk(kids);
            }
        }
    }
}
