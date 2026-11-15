using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Viewer.Maui.Tests;

/// <summary>The logic behind the MAUI viewer's panels (#358), through the viewer core the view exposes.</summary>
public sealed class PanelModelTests
{
    [Fact]
    public void Document_facts_list_the_information_dictionary_and_skip_empty_entries()
    {
        using PdfDocument document = PdfDocument.Open(Shell.CorpusFile("info-dictionary.pdf"));
        IReadOnlyList<DocumentFact> facts = DocumentFacts.Read(document, "info-dictionary.pdf", out bool incomplete);

        Assert.False(incomplete);
        Assert.Contains(new DocumentFact("File", "info-dictionary.pdf"), facts);
        Assert.Contains(new DocumentFact("Pages", "1"), facts);
        Assert.Contains(new DocumentFact("Title", "Broadside – Info"), facts);
        Assert.Contains(new DocumentFact("Author", "Ada Lovelace"), facts);
        Assert.Contains(new DocumentFact("Tagged", "no"), facts);
        Assert.DoesNotContain(facts, f => f.Name == "Language");
    }

    [Fact]
    public void The_tag_tree_shows_roots_and_expands_children_on_toggle()
    {
        using PdfDocument document = PdfDocument.Open(Shell.CorpusFile("tagged-structure.pdf"));
        var tree = new TagTree(document);
        Assert.False(tree.IsEmpty);
        IReadOnlyList<TagRow> collapsed = tree.Rows();
        TagRow root = collapsed[0];
        Assert.Equal(0, root.Depth);
        Assert.True(root.HasChildren);
        Assert.False(root.IsExpanded);

        tree.Toggle(root.Element);
        IReadOnlyList<TagRow> expanded = tree.Rows();
        Assert.True(expanded.Count > collapsed.Count);
        Assert.True(expanded[0].IsExpanded);
        Assert.Equal(1, expanded[1].Depth);

        tree.Toggle(root.Element);
        Assert.Equal(collapsed.Count, tree.Rows().Count);

        using PdfDocument untagged = PdfDocument.Open(Shell.CorpusFile("info-dictionary.pdf"));
        Assert.True(new TagTree(untagged).IsEmpty);
    }

    [Fact]
    public void Annotation_tools_add_list_and_remove_annotations_through_the_layer() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(ManyPagesDocument.Create(3));
        AnnotationLayer layer = shell.Controller.Annotations!;
        Assert.Empty(await AnnotationEditing.ListAsync(layer, 1, CancellationToken.None));

        AnnotationTarget note = Assert.NotNull(AnnotationEditing.Add(layer, 1, AnnotationTool.Note, 0));
        Assert.Equal(note, layer.Selected);
        Assert.NotNull(AnnotationEditing.Add(layer, 1, AnnotationTool.Box, 1));
        Assert.NotNull(AnnotationEditing.Add(layer, 1, AnnotationTool.Text, 2));
        Assert.NotNull(AnnotationEditing.Add(layer, 1, AnnotationTool.Stamp, 3));

        IReadOnlyList<AnnotationItem> items = await AnnotationEditing.ListAsync(layer, 1, CancellationToken.None);
        Assert.Equal(["Text", "Square", "FreeText", "Stamp"], items.Select(i => i.Kind));
        Assert.All(items, i => Assert.True(i.Removable));
        Assert.Equal("A note added in the Broadside viewer.", items[0].Text);
        Assert.True(items[1].Rectangle.Top < items[0].Rectangle.Top);

        Assert.True(layer.Remove(items[0].Target));
        Assert.Equal(3, (await AnnotationEditing.ListAsync(layer, 1, CancellationToken.None)).Count);
        await shell.SettleAsync();
    });

    [Fact]
    public void Highlighting_the_selection_adds_a_highlight_over_its_quads_and_clears_it() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("text-render-modes.pdf"));
        AnnotationLayer layer = shell.Controller.Annotations!;
        TextSelection selection = shell.Controller.Selection!;
        Assert.Null(AnnotationEditing.HighlightSelection(layer, selection));

        Assert.True(shell.Controller.KeyDown("a", InputModifiers.Meta));
        AnnotationTarget highlight = Assert.NotNull(AnnotationEditing.HighlightSelection(layer, selection));
        Assert.True(selection.IsEmpty);
        var annotation = Assert.IsType<PdfHighlightAnnotation>(layer.GetAnnotation(highlight));
        Assert.NotEmpty(annotation.QuadPoints);
    });

    [Fact]
    public async Task Signature_status_reports_each_signed_field()
    {
        string path = Path.Combine(Corpus.Directory, "..", "..", "samples", "Broadside.Viewer.Blazor.Sample", "wwwroot", "documents", "signed.pdf");
        using PdfDocument document = await PdfDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        IReadOnlyList<SignatureRow> rows = await SignatureStatus.ValidateAsync(document, TestContext.Current.CancellationToken);
        SignatureRow row = Assert.Single(rows);
        Assert.False(string.IsNullOrEmpty(row.FieldName));
        Assert.Contains(row.Status, new[] { "Valid", "Indeterminate", "Invalid" });
        Assert.Contains(row.Facts, f => f.Name == "Covers" && f.Value == "the whole file");
        Assert.Contains(row.Facts, f => f.Name == "Signer");
    }

    [Fact]
    public void Rotation_turns_by_quarter_turns_and_wraps()
    {
        PageLayoutOptions options = LayoutChanges.Rotate(null, -1);
        Assert.Equal(270, options.Rotation);
        Assert.Equal(0, LayoutChanges.Rotate(options, 1).Rotation);
        Assert.Equal(PageLayoutMode.TwoUp, LayoutChanges.Copy(new PageLayoutOptions().WithMode(PageLayoutMode.TwoUp)).Mode);
    }
}
