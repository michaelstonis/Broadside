using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.StructureTree;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Tagged pages keep their structure through copy, merge and split (issue #161): the structure elements of the moved pages are
/// copied, struct parents renumbered and the parent tree rebuilt. ISO 32000-2 §14.7.
/// </summary>
public sealed class TaggedMergeSplitTests
{
    [Fact]
    public void Merging_tagged_structure_pdf_with_itself_yields_one_tree_whose_reading_order_covers_both_copies()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        string projection = StructureProjection.Of(source, source.StructureTree!);
        string text = source.Pages[0].ExtractText().Text;

        using PdfDocument merged = PdfDocument.Merge(source, source);

        byte[] file = Save(merged);
        AssertQpdfCheck(file);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfStructureTreeRoot root = Assert.IsType<PdfStructureTreeRoot>(reopened.StructureTree);
        Assert.True(reopened.MarkInfo.Marked);

        // One tree: the source's, then the source's again on the second page, its IDs (and the table header cell the data cell names
        // through its Headers attribute) renamed so they stay unique.
        string second = projection.Replace(" page 1", " page 2", StringComparison.Ordinal)
            .Replace("ID=h1", "ID=h1_1", StringComparison.Ordinal)
            .Replace("ID=tbl1", "ID=tbl1_1", StringComparison.Ordinal)
            .Replace("(h1)", "(h1_1)", StringComparison.Ordinal);
        Assert.Equal(projection + second, StructureProjection.Of(reopened, root));

        // Each page's content items belong to its own copy, through the parent tree, and read in the source's logical order.
        Assert.Equal(text, reopened.Pages[0].ExtractText().Text);
        Assert.Equal(text, reopened.Pages[1].ExtractText().Text);
        PdfStructureElement[] firstCopy = [.. Enumerable.Range(0, 10).Select(mcid => root.FindElement(reopened.Pages[0], mcid)!)];
        PdfStructureElement[] secondCopy = [.. Enumerable.Range(0, 10).Select(mcid => root.FindElement(reopened.Pages[1], mcid)!)];
        Assert.Equal(firstCopy.Select(element => element.StructureType), secondCopy.Select(element => element.StructureType));
        Assert.Empty(firstCopy.Intersect(secondCopy));
        Assert.All(secondCopy, element => Assert.Same(root.Children[1].Dictionary, Top(element).Dictionary));
        Assert.Same(root.Children[1].Dictionary, Top(root.FindElementForObject(reopened.Pages[1].Annotations[0].Dictionary)!).Dictionary);
        Assert.Equal("TH", root.FindElementById("h1_1"u8)!.StructureType!.Value);
        Assert.NotEqual(reopened.Pages[0].Dictionary[new CosName("StructParents")], reopened.Pages[1].Dictionary[new CosName("StructParents")]);
        TaggedPdf.ReadEverything(reopened);
        Assert.Empty(reopened.Diagnostics);

        static PdfStructureElement Top(PdfStructureElement element)
        {
            while (element.Parent is { } parent)
            {
                element = parent;
            }

            return element;
        }
    }

    [Fact]
    public void Splitting_keeps_only_the_elements_whose_content_is_in_the_range_and_prunes_the_elements_spanning_both()
    {
        // text-tagged-spanning.pdf: a paragraph on page 1, a span and a figure each with content on both pages, a paragraph on page 2.
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-tagged-spanning.pdf"));

        IReadOnlyList<PdfDocument> parts = source.Split(0..1, 1..2);

        // The span's replacement text is shown where its first content is: on the source's first page, and in the second part (which
        // holds only the span's second half) on that part's page.
        string[] texts = [source.Pages[0].ExtractText().Text, "Drucker" + source.Pages[1].ExtractText().Text];

        try
        {
            string[] expected =
            [
                """
                Document (2.0) -> Document (2.0)
                  P (2.0) -> P (2.0)
                    MCID 0 page 1
                  Span (2.0) -> Span (2.0)
                    MCR MCID 1 page 1
                  Figure (2.0) -> Figure (2.0)
                    MCR MCID 2 page 1

                """,
                """
                Document (2.0) -> Document (2.0)
                  Span (2.0) -> Span (2.0)
                    MCR MCID 0 page 1
                  Figure (2.0) -> Figure (2.0)
                    MCR MCID 1 page 1
                  P (2.0) -> P (2.0)
                    MCID 2 page 1

                """,
            ];
            for (int index = 0; index < parts.Count; index++)
            {
                byte[] file = Save(parts[index]);
                AssertQpdfCheck(file);
                using PdfDocument part = PdfDocument.Open(file);
                PdfStructureTreeRoot root = Assert.IsType<PdfStructureTreeRoot>(part.StructureTree);
                Assert.Equal(expected[index].ReplaceLineEndings("\n"), StructureProjection.Of(part, root));
                Assert.Equal(texts[index], part.Pages[0].ExtractText().Text);
                Assert.Equal(3, root.GetMarkedContentElements(part.Pages[0]).Count);
                TaggedPdf.ReadEverything(part);
                Assert.Empty(part.Diagnostics);
            }
        }
        finally
        {
            foreach (PdfDocument part in parts)
            {
                part.Dispose();
            }
        }
    }

    [Fact]
    public void A_role_map_conflict_between_sources_renames_the_appended_type_with_a_diagnostic_and_both_keep_their_meaning()
    {
        using PdfDocument first = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        using PdfDocument second = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        second.StructureTree!.MapRole(new CosName("Para"), new CosName("H2"));

        using PdfDocument merged = PdfDocument.Merge(first, second);

        Diagnostic conflict = Assert.Single(merged.Diagnostics, diagnostic => diagnostic.Code == "StructureRoleConflict");
        Assert.Equal(DiagnosticSeverity.Information, conflict.Severity);
        Assert.Contains("/Para_1", conflict.Message, StringComparison.Ordinal);
        byte[] file = Save(merged);
        AssertQpdfCheck(file);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfStructureTreeRoot root = reopened.StructureTree!;
        Assert.Equal(conflict.ObjectReference, root.FindElement(reopened.Pages[1], 1)!.Reference);
        Assert.Equal(("Para", "P"), Types(root.FindElement(reopened.Pages[0], 1)!));
        Assert.Equal(("Para_1", "H2"), Types(root.FindElement(reopened.Pages[1], 1)!));
        Assert.Equal(new CosName("P"), root.RoleMap![new CosName("Para")]);
        Assert.Equal(new CosName("H2"), root.RoleMap![new CosName("Para_1")]);
        TaggedPdf.ReadEverything(reopened);
        Assert.Empty(reopened.Diagnostics);

        static (string, string) Types(PdfStructureElement element) => (element.StructureType!.Value, element.StandardType!.Name);
    }

    [Fact]
    public void An_attribute_class_defined_otherwise_in_the_appended_document_is_renamed_with_a_diagnostic()
    {
        using PdfDocument first = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        using PdfDocument second = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        second.StructureTree!.AddClassAttributes(new CosName("Centered"), new CosName("Layout")).SetValue(new CosName("SpaceBefore"), new CosInteger(3));

        using PdfDocument merged = PdfDocument.Merge(first, second);

        Diagnostic conflict = Assert.Single(merged.Diagnostics, diagnostic => diagnostic.Code == "StructureClassConflict");
        Assert.Equal(DiagnosticSeverity.Information, conflict.Severity);
        using PdfDocument reopened = PdfDocument.Open(Save(merged));
        PdfStructureTreeRoot root = reopened.StructureTree!;
        PdfStructureElement heading = root.FindElement(reopened.Pages[1], 0)!;
        Assert.Equal([new CosName("Centered_1")], heading.ClassNames);
        Assert.Equal(3, heading.GetAttributeValue(new CosName("Layout"), new CosName("SpaceBefore")) is CosInteger { Value: var value } ? value : 0);
        Assert.Equal([new CosName("Centered")], root.FindElement(reopened.Pages[0], 0)!.ClassNames);
        Assert.Single(root.FindElement(reopened.Pages[0], 0)!.ClassAttributes);
        Assert.Equal(2, heading.ClassAttributes.Count);
    }

    [Fact]
    public void Tagged_pages_appended_to_an_untagged_document_give_it_a_structure_tree_in_an_incremental_update()
    {
        byte[] original = Corpus.Bytes("outline.pdf");
        using PdfDocument target = PdfDocument.Open(original);
        using PdfDocument source = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        int pages = target.Pages.Count;
        string projection = StructureProjection.Of(source, source.StructureTree!);

        target.AppendPages(source);

        byte[] file = Save(target, new PdfSaveOptions().WithIncrementalUpdate(true));
        Assert.Equal(original, file.AsSpan(0, original.Length).ToArray());
        AssertQpdfCheck(file);
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.True(reopened.MarkInfo.Marked);
        Assert.Equal(
            projection.Replace(" page 1", $" page {pages + 1}", StringComparison.Ordinal),
            StructureProjection.Of(reopened, reopened.StructureTree!));
        Assert.Equal(source.Pages[0].ExtractText().Text, reopened.Pages[pages].ExtractText().Text);
        TaggedPdf.ReadEverything(reopened);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void An_outline_item_keeps_its_structure_element_when_the_element_is_copied_and_loses_it_when_not()
    {
        // Two items showing page 1: one names the paragraph on page 1, the other the paragraph on page 2.
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-tagged-spanning.pdf"));
        PdfStructureTreeRoot tree = source.StructureTree!;
        PdfOutline outline = source.CreateOutline();
        foreach ((string title, PdfPage page, int mcid) in new[] { ("first", source.Pages[0], 0), ("last", source.Pages[1], 2) })
        {
            PdfOutlineItem item = outline.Add(title);
            item.Dictionary[new CosName("Dest")] = new CosArray { source.Pages[0].Reference!, new CosName("Fit") };
            item.Dictionary[new CosName("SE")] = tree.FindElement(page, mcid)!.Reference!;
        }

        using PdfDocument whole = PdfDocument.Merge(source);
        using PdfDocument part = source.ExtractPages(0..1);

        using PdfDocument reopenedWhole = PdfDocument.Open(Save(whole));
        using PdfDocument reopenedPart = PdfDocument.Open(Save(part));
        PdfStructureTreeRoot wholeTree = reopenedWhole.StructureTree!;
        Assert.Same(wholeTree.FindElement(reopenedWhole.Pages[0], 0)!.Dictionary, reopenedWhole.Outline!.Items[0].StructureElement);
        Assert.Same(wholeTree.FindElement(reopenedWhole.Pages[1], 2)!.Dictionary, reopenedWhole.Outline.Items[1].StructureElement);
        Assert.Same(reopenedPart.StructureTree!.FindElement(reopenedPart.Pages[0], 0)!.Dictionary, reopenedPart.Outline!.Items[0].StructureElement);
        Assert.Equal("last", reopenedPart.Outline.Items[1].Title);
        Assert.Null(reopenedPart.Outline.Items[1].StructureElement);
        Assert.Empty(reopenedPart.Diagnostics);
    }

    public static TheoryData<string> TaggedFiles =>
        [.. Corpus.WellFormedFileNames.Where(name => { using PdfDocument document = PdfDocument.Open(Corpus.Path(name)); return document.StructureTree is not null; })];

    [Theory]
    [MemberData(nameof(TaggedFiles))]
    public void Every_tagged_corpus_file_merged_with_itself_has_both_copies_of_its_structure(string fileName)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));
        PdfStructureTreeRoot tree = source.StructureTree!;
        int elements = tree.Elements.Count;
        int[] items = [.. source.Pages.Select(page => tree.GetMarkedContentElements(page).Count)];

        using PdfDocument merged = PdfDocument.Merge(source, source);

        byte[] file = Save(merged);
        AssertQpdfCheck(file);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfStructureTreeRoot root = reopened.StructureTree!;
        Assert.Equal(2 * elements, root.Elements.Count);
        Assert.Equal([.. items, .. items], reopened.Pages.Select(page => root.GetMarkedContentElements(page).Count));
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options ?? new PdfSaveOptions());
        return stream.ToArray();
    }

    private static void AssertQpdfCheck(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }
}
