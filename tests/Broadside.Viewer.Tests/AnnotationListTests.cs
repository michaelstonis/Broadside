using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

public sealed class AnnotationListTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_page_is_listed_in_page_then_focus_order_with_authors_and_replies()
    {
        await using DocumentSession session = DocumentSession.Open(PanelSamples.Comments());
        var layer = new AnnotationLayer(session);
        var list = new AnnotationList(layer);
        int changes = 0;
        list.Changed += (_, _) => changes++;
        Assert.Empty(list.Items);
        Assert.False(list.IsComplete);

        await list.RefreshAsync(Token);

        Assert.True(list.IsComplete);
        Assert.Equal(3, changes); // one per page as it arrives
        // The popup and the hidden note take no part.
        Assert.Equal(
            [(0, "Text", "Alice", "First comment", 1), (0, "Text", "Bob", "A reply", 0), (1, "Square", null, "Box", 0)],
            list.Items.Select(i => (i.Target.PageIndex, i.Kind, i.Author, i.Text, i.ReplyCount)));
        AnnotationListItem first = list.Items[0];
        Assert.Equal("D:20240102030405Z", first.Modified);
        Assert.Equal(new PdfRectangle(100, 700, 120, 720), first.Rectangle);
        Assert.True(first.IsRemovable);
        Assert.Same(first, list.FindItem(first.Target));
        Assert.Null(list.FindItem(new AnnotationTarget(2, 0)));
    }

    [Fact]
    public async Task The_list_follows_edits_through_the_layer_but_not_hover_or_selection()
    {
        await using DocumentSession session = DocumentSession.Open(PanelSamples.Comments());
        var layer = new AnnotationLayer(session);
        var list = new AnnotationList(layer);
        await list.RefreshAsync(Token);
        int changes = 0;
        list.Changed += (_, _) => changes++;

        layer.Select(list.Items[0].Target);
        Assert.Equal(0, changes);

        Assert.True(layer.Remove(list.Items[2].Target));
        Assert.Equal(1, changes);
        Assert.Equal(2, list.Items.Count);
        Assert.DoesNotContain(list.Items, i => i.Kind == "Square");

        AnnotationTarget? added = layer.Add(2, page => page.AddSquareAnnotation(new PdfRectangle(10, 10, 50, 50), new Annotations.PdfAnnotationOptions().WithContents("New")));
        Assert.NotNull(added);
        Assert.Equal((2, "New"), (list.Items[^1].Target.PageIndex, list.Items[^1].Text));
    }

    [Fact]
    public async Task Revealing_an_item_selects_it_and_scrolls_the_viewer_to_it()
    {
        await using DocumentSession session = DocumentSession.Open(PanelSamples.Comments());
        var layer = new AnnotationLayer(session);
        var list = new AnnotationList(layer);
        await list.RefreshAsync(Token);
        var viewer = new RecordingViewer(session);

        list.Reveal(list.Items[2], viewer);

        Assert.Equal(list.Items[2].Target, layer.Selected);
        Assert.Equal((1, new PdfRectangle(100, 100, 200, 200)), viewer.Revealed);
    }

    [Fact]
    public async Task Fields_are_listed_as_form_fields_and_cannot_be_removed()
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes("acroform-fields.pdf"));
        var list = new AnnotationList(new AnnotationLayer(session));
        await list.RefreshAsync(Token);

        Assert.NotEmpty(list.Items);
        Assert.All(list.Items.Where(i => i.Role == AnnotationRole.Field), i => Assert.Equal(("Form field", false), (i.Kind, i.IsRemovable)));
    }
}
