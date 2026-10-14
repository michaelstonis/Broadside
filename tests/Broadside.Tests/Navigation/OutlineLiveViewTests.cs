using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Navigation;

/// <summary>
/// Outline item views are live views over the outline's links (ADR 0004; ISO 32000-2 §12.3.3, Tables 150 and 151): an edit made
/// through any view, or straight to the COS objects, shows in the <see cref="PdfOutlineItem.Children"/>, <see cref="PdfOutlineItem.Parent"/>
/// and <see cref="PdfOutlineItem.Level"/> of every other view at the next read (issue #286).
/// </summary>
public sealed class OutlineLiveViewTests
{
    private static readonly CosName First = new("First");
    private static readonly CosName Last = new("Last");
    private static readonly CosName Next = new("Next");
    private static readonly CosName Prev = new("Prev");
    private static readonly CosName Parent = new("Parent");
    private static readonly CosName Title = new("Title");
    private static readonly CosName Count = new("Count");

    [Fact]
    public void Items_added_and_moved_through_one_view_show_in_the_children_parent_and_level_of_views_read_before()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        IReadOnlyList<PdfOutlineItem> parts = document.Outline!.Items;
        PdfOutlineItem part1 = parts[0];
        PdfOutlineItem part2 = parts[1];
        PdfOutlineItem chapter1 = part1.Children[0];
        PdfOutlineItem section11 = chapter1.Children[0];

        // Edits through views other than the ones read afterwards: a new top-level item, a child of it, and a move under it.
        PdfOutlineItem appendix = document.Outline.Add("Appendix");
        appendix.Add("A.1");
        section11.MoveTo(appendix, 1);
        document.Outline.Items[0].Insert(0, "Preface");

        Assert.Empty(chapter1.Children);
        Assert.Equal(["Preface", "Chapter 1", "Chapter 2"], part1.Children.Select(item => item.Title));
        Assert.Equal(["A.1", "Section 1.1"], appendix.Children.Select(item => item.Title));
        Assert.Equal(("Appendix", 1), (section11.Parent?.Title, section11.Level));
        Assert.Equal(["Part I", "Part II", "Appendix"], document.Outline.Items.Select(item => item.Title));
        Assert.Same(appendix, document.Outline.Items[2]);
        Assert.Same(appendix, appendix.Children[0].Parent);

        // Moving an item moves its descendants: their level follows without being touched.
        part1.MoveTo(part2, 0);

        Assert.Equal(("Part II", 1), (part1.Parent?.Title, part1.Level));
        Assert.Equal(2, chapter1.Level);
        Assert.Same(part1, chapter1.Parent);
        Assert.Equal(["Part I", "Chapter 3 – Übersicht", "Chapter 4"], part2.Children.Select(item => item.Title));
        Assert.Equal(["Part II", "Appendix"], document.Outline.Items.Select(item => item.Title));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_removed_item_leaves_the_children_of_its_parent_and_reads_as_outside_the_outline()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        PdfOutlineItem part1 = document.Outline!.Items[0];
        PdfOutlineItem part2 = document.Outline.Items[1];
        PdfOutlineItem chapter4 = part2.Children[1];
        PdfOutlineItem chapter1 = part1.Children[0];

        document.Outline.Items[1].Children[1].Remove();
        chapter1.Remove();

        Assert.Equal(["Chapter 3 – Übersicht"], part2.Children.Select(item => item.Title));
        Assert.Equal(["Chapter 2"], part1.Children.Select(item => item.Title));
        Assert.Equal((null, 0), (chapter4.Parent, chapter4.Level));
        Assert.Empty(chapter1.Children);
        Assert.Throws<InvalidOperationException>(chapter4.Remove);
    }

    [Fact]
    public void An_item_removed_as_an_object_leaves_the_children_of_views_read_before()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        PdfOutlineItem part1 = document.Outline!.Items[0];
        PdfOutlineItem chapter2 = part1.Children[1];
        Assert.Equal(["Chapter 1", "Chapter 2"], part1.Children.Select(item => item.Title));

        // §7.3.10: Chapter 1's Next and Part I's Last now resolve to null; no dictionary the walk read has changed.
        Assert.True(document.RemoveObject(chapter2.Reference!));

        Assert.Equal(["Chapter 1"], part1.Children.Select(item => item.Title));
        Assert.Equal((null, 0), (chapter2.Parent, chapter2.Level));
    }

    [Fact]
    public void An_object_added_under_the_number_of_a_dangling_link_shows_in_the_children_of_views_read_before()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        PdfOutlineItem part1 = document.Outline!.Items[0];
        PdfOutlineItem chapter2 = part1.Children[1];

        // Links to an object the document does not hold yet, made after the first AddObject looked for such references.
        CosReference first = document.AddObject(new CosDictionary());
        var dangling = new CosReference(first.ObjectNumber + 1, 0);
        chapter2.Dictionary[Next] = dangling;
        part1.Dictionary[Last] = dangling;
        Assert.Equal(["Chapter 1", "Chapter 2"], part1.Children.Select(item => item.Title));

        // §7.3.10: the link resolves to the new object from now on; no dictionary the walk read has changed since.
        var chapter3 = new CosDictionary
        {
            [Title] = new CosString("Chapter 3"u8),
            [Parent] = part1.Reference!,
            [Prev] = chapter2.Reference!,
        };
        Assert.Equal(dangling, document.AddObject(chapter3));

        Assert.Equal(["Chapter 1", "Chapter 2", "Chapter 3"], part1.Children.Select(item => item.Title));
    }

    [Fact]
    public void Links_edited_in_the_cos_objects_are_seen_by_views_read_before_the_edit()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        PdfOutline outline = document.Outline!;
        PdfOutlineItem part2 = outline.Items[1];
        PdfOutlineItem chapter3 = part2.Children[0];
        PdfOutlineItem chapter4 = part2.Children[1];
        Assert.Empty(chapter3.Children);

        // Chapter 4 becomes Chapter 3's child, by hand.
        chapter3.Dictionary.Remove(Next);
        part2.Dictionary[Last] = chapter3.Reference!;
        part2.Dictionary[Count] = new CosInteger(-1);
        chapter4.Dictionary.Remove(Prev);
        chapter4.Dictionary[Parent] = chapter3.Reference!;
        chapter3.Dictionary[First] = chapter4.Reference!;
        chapter3.Dictionary[Last] = chapter4.Reference!;
        chapter3.Dictionary[Count] = new CosInteger(1);

        Assert.Equal(["Chapter 3 – Übersicht"], part2.Children.Select(item => item.Title));
        Assert.Equal(["Chapter 4"], chapter3.Children.Select(item => item.Title));
        Assert.Same(chapter3, chapter4.Parent);
        Assert.Equal(2, chapter4.Level);

        // A new item linked by hand at the top level, and a title changed by hand.
        var epilogue = new CosDictionary { [Title] = new CosString("Epilogue"u8.ToArray()), [Parent] = outline.Reference!, [Prev] = part2.Reference! };
        CosReference epilogueReference = document.AddObject(epilogue);
        part2.Dictionary[Next] = epilogueReference;
        outline.Dictionary[Last] = epilogueReference;
        chapter4.Dictionary[Title] = new CosString("Chapter 4, revised"u8.ToArray());

        PdfOutlineItem added = outline.Items[2];
        Assert.Equal(("Epilogue", 0, null), (added.Title, added.Level, added.Parent));
        Assert.Equal("Chapter 4, revised", chapter3.Children[0].Title);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_outline_replaced_in_the_catalog_leaves_the_items_of_the_old_one_outside_it()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        PdfOutlineItem part1 = document.Outline!.Items[0];
        PdfOutlineItem chapter1 = part1.Children[0];

        PdfOutline replacement = document.CreateOutline();
        PdfOutlineItem only = replacement.Add("Only");

        Assert.Equal((null, 0), (chapter1.Parent, chapter1.Level));
        Assert.Empty(part1.Children);
        Assert.Equal(["Only"], document.Outline!.Items.Select(item => item.Title));
        Assert.Same(only, document.Outline.Items[0]);
    }
}
