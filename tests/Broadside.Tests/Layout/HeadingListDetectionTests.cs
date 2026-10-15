using Broadside.Layout;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>Headings and lists detected on untagged pages (issue #188): heading levels from type size and weight, list items from markers, nesting from indentation.</summary>
public sealed class HeadingListDetectionTests
{
    [Fact]
    public void Headings_get_levels_from_their_size_and_weight_relative_to_the_body_text()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-headings-lists.pdf"));

        LayoutTree tree = document.ExtractLayout();

        Assert.Equal(
            [
                "Heading1", "Paragraph", "Heading2", "Paragraph", "Heading3", "List:Disc", "Heading2", "Paragraph", "List:Decimal",
                "Paragraph", "List:UpperRoman", "Paragraph", "List:UpperAlpha", "Heading3", "Paragraph",
            ],
            tree.Root.Children.Select(Describe));
        Assert.Equal(
            ["Headings and Lists", "1 Bulleted items", "1.1 Three levels", "2 Numbered items", "2.1 Closing"],
            tree.Root.Children.OfType<LayoutHeading>().Select(static heading => heading.Text));
    }

    [Fact]
    public void A_three_level_list_nests_each_level_in_the_body_of_the_item_above()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-headings-lists.pdf"));

        LayoutTree tree = document.ExtractLayout();

        var bullets = Assert.IsType<LayoutList>(tree.Root.Children[5]);
        Assert.Equal(
            "List:Disc(Item[•|The first item has a body long enough to wrap onto|a second line under its own text.], "
            + "Item[•|The second item holds a nested list:|"
            + "List:LowerAlpha(Item[a)|a lettered item at the second level], "
            + "Item[b)|another lettered item, which nests deeper:|List:LowerRoman(Item[i.|a roman item at the third level], Item[ii.|and a second roman item])], "
            + "Item[c)|a third lettered item])], "
            + "Item[•|The third item closes the list.])",
            Shape(bullets));
        LayoutNode first = bullets.Children[0];
        Assert.Equal([LayoutNodeKind.Label, LayoutNodeKind.ListBody], first.Children.Select(static node => node.Kind));
        Assert.Equal("The first item has a body long enough to wrap onto\na second line under its own text.", first.Children[1].Text);
        Assert.Equal(first.Text, bullets.Text[..first.Text.Length]);
    }

    [Fact]
    public void Numbered_lists_with_decimal_roman_and_letter_markers_are_recognised()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-headings-lists.pdf"));

        LayoutTree tree = document.ExtractLayout();

        LayoutList[] lists = [.. tree.Root.Children.OfType<LayoutList>().Skip(1)];
        Assert.Equal(
            [
                "List:Decimal(Item[1.|Decimal numbers with a full stop], Item[2.|count the items of a list], Item[3.|one after the other.])",
                "List:UpperRoman(Item[I.|the first numeral], Item[II.|the second numeral], Item[III.|the third numeral], Item[IV.|the fourth numeral])",
                "List:UpperAlpha(Item[(A)|a first lettered item], Item[(B)|a second lettered item], Item[(C)|a third lettered item])",
            ],
            lists.Select(Shape));
    }

    [Fact]
    public void Headings_and_lists_repeat_page_after_page_and_are_detected_the_same_each_time()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.HeadingsAndLists(2));

        LayoutTree tree = document.ExtractLayout();

        Assert.Equal(18, tree.Root.Children.Count);
        Assert.All(tree.Root.Children.OfType<LayoutHeading>(), static heading => Assert.Equal(1, heading.Level));
        Assert.Equal(6, tree.Root.Children.OfType<LayoutHeading>().Count());
        Assert.All(tree.Root.Children.OfType<LayoutList>(), static list =>
        {
            Assert.Equal("Disc", list.Numbering);
            Assert.Equal(4, list.Children.Count);
            Assert.All(list.Children, static item => Assert.Equal("LowerAlpha", Assert.IsType<LayoutList>(Assert.Single(item.Children[1].Children)).Numbering));
        });
        Assert.Equal(Shape(tree.Root), Shape(document.ExtractLayout().Root));
    }

    [Fact]
    public void Text_in_a_font_without_a_name_is_weighed_as_regular()
    {
        // A Type 3 font has no BaseFont (found by the layout-detection fuzz target).
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3-recursive.pdf"));

        LayoutTree tree = document.ExtractLayout();

        Assert.True(tree.Pages[0].IsDetected);
    }

    [Fact]
    public void A_tagged_list_reports_its_list_numbering_attribute()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));

        LayoutTree tree = document.ExtractLayout();

        Assert.Equal("Decimal", Assert.IsType<LayoutList>(tree.Root.Children[0].Children[2]).Numbering);
    }

    private static string Describe(LayoutNode node) => node switch
    {
        LayoutHeading heading => $"Heading{heading.Level}",
        LayoutList list => $"List:{list.Numbering}",
        _ => node.Kind.ToString(),
    };

    /// <summary>A list as its items' label and body texts (lines joined by <c>|</c>), with nested lists in place.</summary>
    private static string Shape(LayoutNode node) => node switch
    {
        LayoutList list => $"List:{list.Numbering}({string.Join(", ", list.Children.Select(Shape))})",
        { Kind: LayoutNodeKind.ListItem } => $"Item[{string.Join('|', node.Children.Select(Shape))}]",
        { Kind: LayoutNodeKind.ListBody } => string.Join('|', [Own(node), .. node.Children.Select(Shape)]),
        { Kind: LayoutNodeKind.Label } => node.Text,
        _ => $"{node.Kind}[{node.Text.Replace('\n', '|')}]({string.Join(", ", node.Children.Select(Shape))})",
    };

    /// <summary>A body's own text: its text before the first nested list's.</summary>
    private static string Own(LayoutNode body)
    {
        string text = body.Text;
        if (body.Children.Count > 0)
        {
            text = text[..text.IndexOf(body.Children[0].Text, StringComparison.Ordinal)].TrimEnd('\n');
        }

        return text.Replace('\n', '|');
    }
}
