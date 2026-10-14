using System.Globalization;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Tests.Navigation;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Merging documents and splitting one into ranges (spec story 30): pages, outlines, named destinations, form fields and page labels.
/// ISO 32000-2 §7.7.3, §12.3.2.4, §12.3.3, §12.4.2 and §12.7.
/// </summary>
public sealed class MergeSplitTests
{
    [Fact]
    public void Merging_outline_full_acroform_fields_and_name_tree_dests_keeps_every_outline_item_destination_and_field_working()
    {
        using PdfDocument dests = PdfDocument.Open(Corpus.Path("name-tree-dests.pdf"));
        using PdfDocument form = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        using PdfDocument outline = PdfDocument.Open(Corpus.Path("outline-full.pdf"));
        IReadOnlyList<string> outlineShape = OutlineShape.AssertConsistent(outline);
        (string Name, string Value, string Pages)[] fields = [.. Fields(form, pageOffset: 1)];

        using PdfDocument merged = PdfDocument.Merge(dests, form, outline);

        byte[] file = Save(merged);
        AssertQpdfCheck(file);
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal(4, reopened.Pages.Count);
        PageTreeShape.AssertBalanced(reopened, 32);

        // Every outline item, linked as before, each showing its page among the merged ones. outline-full's destination "alpha"
        // collides with name-tree-dests' and is renamed, and the item going to it follows.
        Assert.Equal(outlineShape, OutlineShape.AssertConsistent(reopened));
        string[] targets = [.. Flatten(reopened.Outline!.Items).Select(Describe)];
        Assert.Equal(
            ["page 3 Fit", "page 3 FitH", "page 3 Xyz", "uri https://example.org/chapter-2", "none", "name alpha_1 page 3 Xyz", "page 3 FitB"],
            targets);

        // Every named destination: name-tree-dests' three on page 0 and outline-full's renamed one on page 3.
        Assert.Equal(
            ["alpha page 0 Fit", "alpha_1 page 3 Xyz", "beta page 0 Xyz", "gamma page 0 FitH"],
            reopened.Names!.Dests!.Select(entry => $"{entry.Key.DecodeText()} {Describe(reopened.GetNamedDestination(entry.Key))}").Order(StringComparer.Ordinal));

        // Every field of acroform-fields, with its value, its widgets on its pages, one page later.
        Assert.Equal(fields, Fields(reopened, pageOffset: 0));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Two_documents_with_fields_of_the_same_name_merge_with_the_second_renamed_and_both_values_kept()
    {
        using PdfDocument first = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        using PdfDocument second = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        ((PdfTextField)second.AcroForm!.FindField("name")!).SetValue("Grace");
        string[] names = [.. first.AcroForm!.TerminalFields.Select(field => field.FullyQualifiedName)];

        using PdfDocument merged = PdfDocument.Merge(first, second);

        byte[] file = Save(merged);
        AssertQpdfCheck(file);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfAcroForm form = reopened.AcroForm!;
        Assert.Equal("Ada", ((PdfTextField)form.FindField("name")!).Value);
        Assert.Equal("Grace", ((PdfTextField)form.FindField("name_1")!).Value);

        // Every top-level field of the second copy is renamed, its descendants with it; each widget stays on its own copy's pages.
        Assert.Equal(
            names.Concat(names.Select(Renamed)).Order(StringComparer.Ordinal),
            form.TerminalFields.Select(field => field.FullyQualifiedName).Order(StringComparer.Ordinal));
        Assert.All(form.TerminalFields, field => Assert.All(
            field.Widgets,
            widget => Assert.Equal(field.FullyQualifiedName.Split('.')[0].EndsWith("_1", StringComparison.Ordinal), reopened.Pages.IndexOf(widget.Page!) >= 2)));

        // Field names given as text follow: the reset button's fields and the signature field's lock.
        Assert.Equal(["(person)", "R"], FieldsEntry(first, first.AcroForm.FindField("submit")!.Dictionary, new CosName("A")));
        Assert.Equal(["(person)", "R"], FieldsEntry(reopened, form.FindField("submit")!.Dictionary, new CosName("A")));
        Assert.Equal(["(person_1)", "R"], FieldsEntry(reopened, form.FindField("submit_1")!.Dictionary, new CosName("A")));
        Assert.Equal(["(name_1)", "(person_1.first)"], FieldsEntry(reopened, form.FindField("sig_1")!.Dictionary, new CosName("Lock")));
        Assert.Equal(2, form.CalculationOrder.Count);
        Assert.Empty(reopened.Diagnostics);

        static string Renamed(string name)
        {
            int dot = name.IndexOf('.', StringComparison.Ordinal);
            return dot < 0 ? name + "_1" : string.Concat(name.AsSpan(0, dot), "_1", name.AsSpan(dot));
        }
    }

    [Fact]
    public void Splitting_a_ten_page_document_into_three_ranges_yields_three_files_whose_outlines_hold_only_their_own_targets()
    {
        using PdfDocument source = PdfDocument.Open(TenPagesWithOutline());

        IReadOnlyList<PdfDocument> parts = source.Split(0..3, 3..7, 7..);

        try
        {
            Assert.Equal(3, parts.Count);
            (int Pages, string[] Shape, string[] Targets, string[] Names)[] expected =
            [
                (3, ["0 Part A 1", "1 A1 -"], ["page 0 Fit", "page 1 Fit"], ["p2 page 2 Fit"]),
                (4, ["0 Part A 1", "1 A2 -", "0 Part B -"], ["none", "page 1 Fit", "page 2 Fit"], []),
                (3, ["0 Part B -2", "1 B1 -", "1 B2 -", "0 Named 8 -"], ["none", "page 0 Fit", "page 2 Fit", "name p8 page 1 Fit"], ["p8 page 1 Fit"]),
            ];
            for (int index = 0; index < parts.Count; index++)
            {
                byte[] file = Save(parts[index]);
                AssertQpdfCheck(file);
                using PdfDocument part = PdfDocument.Open(file);
                Assert.Equal(expected[index].Pages, part.Pages.Count);
                Assert.Equal(expected[index].Shape, OutlineShape.AssertConsistent(part));
                Assert.Equal(expected[index].Targets, Flatten(part.Outline!.Items).Select(Describe));
                Assert.Equal(expected[index].Names, part.Names?.Dests?.Select(entry => $"{entry.Key.DecodeText()} {Describe(part.GetNamedDestination(entry.Key))}") ?? []);
                Assert.Equal(source.Pages.Skip(index == 0 ? 0 : index == 1 ? 3 : 7).Take(part.Pages.Count).Select(page => page.MediaBox), part.Pages.Select(page => page.MediaBox));
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
    public void Page_labels_are_kept_through_merging_and_splitting()
    {
        using PdfDocument labelled = PdfDocument.Open(Corpus.Path("page-labels.pdf"));
        using PdfDocument plain = PdfDocument.Open(Corpus.Path("name-tree-dests.pdf"));
        string[] labels = [.. labelled.PageLabels!.GetLabels()];
        Assert.Equal(["i", "ii", "iii", "1", "2", "IV", "V", "Z", "AA", "A-zz", "A-aaa", "Cover"], labels);

        using PdfDocument labelledFirst = PdfDocument.Merge(labelled, plain);
        using PdfDocument plainFirst = PdfDocument.Merge(plain, labelled, plain);
        using PdfDocument middle = labelled.ExtractPages(4..10);

        using PdfDocument plainOnly = PdfDocument.Merge(plain, plain);

        Assert.Equal([.. labels, "1"], LabelsAfterSaving(labelledFirst));
        Assert.Equal(["1", .. labels, "1"], LabelsAfterSaving(plainFirst));
        Assert.Equal(["2", "IV", "V", "Z", "AA", "A-zz"], LabelsAfterSaving(middle));
        Assert.Null(LabelsAfterSaving(plainOnly));

        static IReadOnlyList<string>? LabelsAfterSaving(PdfDocument document)
        {
            byte[] file = Save(document);
            AssertQpdfCheck(file);
            using PdfDocument reopened = PdfDocument.Open(file);
            Assert.Empty(reopened.Diagnostics);
            return reopened.PageLabels?.GetLabels();
        }
    }

    public static TheoryData<string> WellFormedFiles => [.. Corpus.WellFormedFileNames];

    [Theory]
    [MemberData(nameof(WellFormedFiles))]
    public void Every_corpus_file_merged_with_itself_saves_with_both_copies_of_its_pages_outline_destinations_and_fields(string fileName)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));
        int outlineItems = source.Outline is { } outline ? Flatten(outline.Items).Count() : 0;
        int fields = source.AcroForm?.TerminalFields.Count ?? 0;
        int names = source.Names?.Dests?.Count() ?? 0;

        using PdfDocument merged = PdfDocument.Merge(source, source);

        byte[] file = Save(merged);
        AssertQpdfCheck(file, keysComparedAsText: fileName == "name-tree-deep.pdf");
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.False(reopened.IsEncrypted);
        Assert.Equal(2 * source.Pages.Count, reopened.Pages.Count);
        Assert.Equal(2 * outlineItems, reopened.Outline is { } merged0 ? Flatten(merged0.Items).Count() : 0);
        Assert.Equal(2 * fields, reopened.AcroForm?.TerminalFields.Count ?? 0);
        Assert.Equal(2 * names, reopened.Names?.Dests?.Count() ?? 0);
        if (outlineItems > 0)
        {
            _ = OutlineShape.AssertConsistent(reopened);
        }

        PageTreeShape.AssertBalanced(reopened, 32);
    }

    [Fact]
    public void Links_and_go_to_actions_of_the_second_copy_go_to_its_own_renamed_destinations_and_pages()
    {
        byte[] original = LinkedDocument();
        using PdfDocument first = PdfDocument.Open(original);
        using PdfDocument second = PdfDocument.Open(original);

        using PdfDocument merged = PdfDocument.Merge(first, second);

        byte[] file = Save(merged);
        AssertQpdfCheck(file);
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal(4, reopened.Pages.Count);

        // Each copy's links on its first page go to its own second page: by string name (renamed in the tree), by name (renamed in
        // the catalog's Dests) and by a page number (given the copied page).
        Assert.Equal(["name s page 1 Fit", "name n page 1 Fit", "page 1 Fit"], Links(reopened.Pages[0]));
        Assert.Equal(["name s_1 page 3 Fit", "name n_1 page 3 Fit", "page 3 Fit"], Links(reopened.Pages[2]));
        Assert.Empty(reopened.Diagnostics);

        static string[] Links(PdfPage page) =>
            [.. page.Annotations.OfType<Broadside.Annotations.PdfLinkAnnotation>().Select(link => link.Destination is { } destination
                ? Describe(destination)
                : Describe(((PdfGoToAction)link.Action!).Destination))];
    }

    [Fact]
    public void Pages_appended_to_an_existing_document_follow_its_pages_and_outline_in_an_incremental_update()
    {
        byte[] original = Corpus.Bytes("outline.pdf");
        using PdfDocument target = PdfDocument.Open(original);
        using PdfDocument source = PdfDocument.Open(TenPagesWithOutline());
        int pages = target.Pages.Count;
        IReadOnlyList<string> shape = OutlineShape.AssertConsistent(target);

        target.AppendPages(source, 7..9);

        byte[] file = Save(target, new PdfSaveOptions().WithIncrementalUpdate(true));
        Assert.Equal(original, file.AsSpan(0, original.Length).ToArray());
        AssertQpdfCheck(file);
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal(pages + 2, reopened.Pages.Count);
        Assert.Equal([.. shape, "0 Part B -1", "1 B1 -", "0 Named 8 -"], OutlineShape.AssertConsistent(reopened));
        string[] targets = [.. Flatten(reopened.Outline!.Items).Select(Describe)];
        Assert.Equal(["none", $"page {pages} Fit", $"name p8 page {pages + 1} Fit"], targets[^3..]);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Ranges_outside_the_pages_a_document_s_own_pages_and_empty_requests_are_refused()
    {
        using PdfDocument document = PdfDocument.Open(TenPagesWithOutline());
        using PdfDocument other = PdfDocument.Open(Corpus.Path("outline.pdf"));

        Assert.Throws<ArgumentException>(() => document.AppendPages(document));
        Assert.Throws<ArgumentOutOfRangeException>(() => other.AppendPages(document, 8..11));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ExtractPages(^11..));
        Assert.Throws<ArgumentException>(() => document.ExtractPages(3..3));
        Assert.Throws<ArgumentException>(() => document.Split());
        Assert.Throws<ArgumentException>(() => document.Split(0..5, 5..5));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Split(0..5, 5..11));
        Assert.Throws<ArgumentException>(() => PdfDocument.Merge());
        Assert.Throws<ArgumentException>(() => PdfDocument.Merge(document, null!));
        Assert.Equal(10, document.Pages.Count);
    }

    [Fact]
    public void Appending_from_a_strict_source_whose_later_page_reads_its_annotations_from_a_node_throws_before_the_document_changes()
    {
        // The first page has its own (empty) Annots; the second reads the node's (Table 31: not inheritable), which strict mode refuses.
        byte[] original = new Broadside.Tests.Document.TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /Annots [5 0 R] /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /Type /Page /Parent 2 0 R /Annots [] >>",
            "<< /Type /Page /Parent 2 0 R >>",
            "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Contents (A) >>");
        using PdfDocument source = PdfDocument.Open(original, new PdfOptions().UseStrict());
        using PdfDocument target = PdfDocument.Open(Corpus.Path("outline.pdf"));
        string before = CosSnapshot.Of(target);

        Assert.Throws<Broadside.Diagnostics.DiagnosticException>(() => target.AppendPages(source));

        Assert.Equal(before, CosSnapshot.Of(target));
    }

    /// <summary>
    /// Two pages; the first has three links to the second: by the string name <c>s</c> (the <c>Dests</c> tree), by a go-to action to
    /// the name <c>n</c> (the catalog's PDF 1.1 <c>Dests</c>), and by the page number 1 (a producer error readers accept).
    /// </summary>
    private static byte[] LinkedDocument()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        PdfPage target = document.Pages[1];
        document.SetNamedDestination("s", document.CreateDestination(target, PdfDestinationView.Fit));
        document.Catalog[new CosName("Dests")] = document.AddObject(new CosDictionary
        {
            [new CosName("n")] = new CosArray([target.Reference!, new CosName("Fit")]),
        });
        var annots = new CosArray();
        foreach (CosDictionary link in new[]
        {
            new CosDictionary { [new CosName("Dest")] = new CosString("s"u8) },
            new CosDictionary { [new CosName("A")] = new CosDictionary { [new CosName("S")] = new CosName("GoTo"), [new CosName("D")] = new CosName("n") } },
            new CosDictionary { [new CosName("Dest")] = new CosArray([new CosInteger(1), new CosName("Fit")]) },
        })
        {
            link[new CosName("Type")] = new CosName("Annot");
            link[new CosName("Subtype")] = new CosName("Link");
            link[new CosName("Rect")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(10), new CosInteger(10)]);
            annots.Add(document.AddObject(link));
        }

        document.Pages[0].Dictionary[new CosName("Annots")] = annots;
        return Save(document);
    }

    /// <summary>
    /// Ten pages of different widths, with an outline: Part A (page 0; A1 on page 1, A2 on page 4), Part B (page 5, closed; B1 on
    /// page 7, B2 on page 9), a URI item and an item going to the named destination p8 (page 8); p2 names page 2.
    /// </summary>
    private static byte[] TenPagesWithOutline()
    {
        using PdfDocument document = PdfDocument.Create();
        for (int index = 1; index < 10; index++)
        {
            document.Pages.Add(new PdfRectangle(0, 0, 600 + index, 800));
        }

        PdfOutline outline = document.CreateOutline();
        PdfOutlineItem partA = Item(outline.Add("Part A"), 0);
        Item(partA.Add("A1"), 1);
        Item(partA.Add("A2"), 4);
        PdfOutlineItem partB = Item(outline.Add("Part B"), 5);
        Item(partB.Add("B1"), 7);
        Item(partB.Add("B2"), 9);
        partB.IsOpen = false;
        outline.Add("Website").Dictionary[new CosName("A")] = new CosDictionary
        {
            [new CosName("S")] = new CosName("URI"),
            [new CosName("URI")] = new CosString("https://example.org/"u8),
        };
        document.SetNamedDestination("p2", document.CreateDestination(document.Pages[2], PdfDestinationView.Fit));
        outline.Add("Named 8").Destination = document.SetNamedDestination("p8", document.CreateDestination(document.Pages[8], PdfDestinationView.Fit));
        return Save(document);

        PdfOutlineItem Item(PdfOutlineItem item, int page)
        {
            item.Destination = document.CreateDestination(document.Pages[page], PdfDestinationView.Fit);
            return item;
        }
    }

    private static string[] FieldsEntry(PdfDocument document, CosDictionary owner, CosName key)
    {
        var dictionary = (CosDictionary)document.Resolve(owner[key]);
        var fields = (CosArray)document.Resolve(dictionary[new CosName("Fields")]);
        return [.. fields.Select(field => field is CosReference ? "R" : field.ToString())];
    }

    private static IEnumerable<PdfOutlineItem> Flatten(IReadOnlyList<PdfOutlineItem> items)
    {
        foreach (PdfOutlineItem item in items)
        {
            yield return item;
            foreach (PdfOutlineItem child in Flatten(item.Children))
            {
                yield return child;
            }
        }
    }

    private static string Describe(PdfOutlineItem item) => item.Destination switch
    {
        { } destination => Describe(destination),
        null => item.Action switch
        {
            PdfGoToAction goTo => Describe(goTo.Destination),
            PdfUriAction uri => $"uri {uri.Uri}",
            null => "none",
            { } action => action.Kind.ToString(),
        },
    };

    private static string Describe(PdfDestination? destination) => destination switch
    {
        PdfExplicitDestination explicitDestination => explicitDestination.PageIndex is { } index
            ? string.Create(CultureInfo.InvariantCulture, $"page {index} {explicitDestination.View}")
            : "no page",
        PdfNamedDestination named => $"name {named.Text} {Describe(named.Resolve())}",
        _ => "none",
    };

    private static IEnumerable<(string Name, string Value, string Pages)> Fields(PdfDocument document, int pageOffset) =>
        document.AcroForm!.TerminalFields
            .Select(field => (
                field.FullyQualifiedName,
                field.ValueObject?.ToString() ?? "-",
                string.Join(',', field.Widgets.Select(widget => document.Pages.IndexOf(widget.Page!) + pageOffset))))
            .OrderBy(field => field.FullyQualifiedName, StringComparer.Ordinal);

    private static byte[] Save(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options ?? new PdfSaveOptions());
        return stream.ToArray();
    }

    private static void AssertQpdfCheck(byte[] file, bool keysComparedAsText = false)
    {
        // qpdf 12 compares name-tree keys as decoded text, not bytes as ISO 32000-2 §7.9.6 requires, and warns (exit code 3) on
        // name-tree-deep.pdf's PDFDocEncoding and UTF-16BE keys (as TreeWritingTests accepts); any other warning fails.
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        if (keysComparedAsText)
        {
            Assert.True(exitCode is 0 or 3, output);
            Assert.All(
                output.Split('\n').Where(line => line.StartsWith("WARNING", StringComparison.Ordinal)),
                line => Assert.Contains("keys are not sorted", line, StringComparison.Ordinal));
        }
        else
        {
            Assert.True(exitCode == 0, output);
        }
    }
}
