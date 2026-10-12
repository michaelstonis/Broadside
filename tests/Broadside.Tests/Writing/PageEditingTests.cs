using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;
using Broadside.Tests.Document;

namespace Broadside.Tests.Writing;

/// <summary>Inserting, removing, moving and rotating pages, and saving the edited page tree. ISO 32000-2 §7.7.3.</summary>
public sealed class PageEditingTests
{
    private static readonly CosName Contents = new("Contents");
    private static readonly CosName Type = new("Type");
    private static readonly CosName MediaBox = new("MediaBox");
    private static readonly CosName Parent = new("Parent");
    private static readonly CosName Rotate = new("Rotate");

    public static TheoryData<bool> Saves => [false, true];

    [Theory]
    [MemberData(nameof(Saves))]
    public void Reordering_pages_keeps_every_page_s_inherited_media_box_resources_and_rotation(bool incremental)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        Assert.False(source.Pages[0].Dictionary.ContainsKey(new CosName("MediaBox")));

        // The file's intermediate node gives MediaBox and Resources; give it Rotate too, so all three are inherited.
        ((CosDictionary)source.Resolve(source.Pages[0].Dictionary[Parent]))[Rotate] = new CosInteger(90);
        PageFacts[] before = [.. source.Pages.Select(page => PageFacts.Of(source, page))];

        source.Pages.Move(1, 0);

        byte[] file = Save(source, incremental);

        using PdfDocument saved = PdfDocument.Open(file);
        Assert.Equal(2, saved.Pages.Count);
        PageFacts[] after = [.. saved.Pages.Select(page => PageFacts.Of(saved, page))];
        Assert.Equal(before[1].Text, after[0].Text);
        Assert.Equal(before[0].Text, after[1].Text);
        foreach ((PageFacts expected, PageFacts actual, PdfPage page) in new[] { (before[1], after[0], saved.Pages[0]), (before[0], after[1], saved.Pages[1]) })
        {
            Assert.Equal(expected.MediaBox, actual.MediaBox);
            Assert.Equal(90, actual.Rotation);
            CosGraph.AssertIsomorphic(source, expected.Resources, saved, page.Resources!);
        }

        AssertQpdfCheck(file);
    }

    [Fact]
    public void A_removed_page_s_objects_are_absent_from_a_save_that_drops_unreachable_objects()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        CosReference removedPage = source.Pages[0].Reference!;
        byte[] removedContent = ((CosStream)source.Resolve(source.Pages[0].Dictionary[Contents])).EncodedData.ToArray();
        string keptText = PageFacts.Of(source, source.Pages[1]).Text;

        source.Pages.RemoveAt(0);

        Assert.Single(source.Pages);
        Assert.IsType<CosNull>(source.Resolve(removedPage));
        using var stream = new MemoryStream();
        source.Save(stream, new PdfSaveOptions().WithRemoveUnreachableObjects(true));
        byte[] file = stream.ToArray();
        using PdfDocument saved = PdfDocument.Open(file);
        Assert.Equal(keptText, PageFacts.Of(saved, Assert.Single(saved.Pages)).Text);
        CosObject[] written = [.. CosGraph.AllObjects(saved).Select(saved.Resolve)];
        Assert.DoesNotContain(written, value => value is CosStream content && content.EncodedData.Span.SequenceEqual(removedContent));
        Assert.Single(written, value => value is CosDictionary dictionary && dictionary.TryGetValue(Type, out CosObject? type) && new CosName("Page").Equals(type));
        Assert.Equal(CosGraph.Reachable(saved).Count, written.Length);
        AssertQpdfCheck(file);
    }

    [Fact]
    public void A_page_removed_and_saved_incrementally_is_a_free_object_of_the_update()
    {
        byte[] original = Corpus.Bytes("page-tree-inherited.pdf");
        using PdfDocument source = PdfDocument.Open(original);
        CosReference removedPage = source.Pages[1].Reference!;

        source.Pages.RemoveAt(1);
        byte[] file = Save(source, incremental: true);

        Assert.True(file.AsSpan(0, original.Length).SequenceEqual(original), "The original bytes changed.");
        using PdfDocument saved = PdfDocument.Open(file);
        Assert.Single(saved.Pages);
        Assert.IsType<CosNull>(saved.Resolve(removedPage));
        Assert.DoesNotContain(saved.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        AssertQpdfCheck(file);
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void An_outline_item_pointing_at_a_removed_page_is_reported_on_save(bool incremental)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline.pdf"));
        CosReference[] items = [.. document.Outline!.Items.Select(item => item.Reference!)];
        Assert.Equal(2, items.Length);
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        document.Pages.RemoveAt(0);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == "DestinationPageRemoved");

        byte[] file = Save(document, incremental);

        Diagnostic[] reported = [.. document.Diagnostics.Where(diagnostic => diagnostic.Code == "DestinationPageRemoved")];
        Assert.Equal(items, reported.Select(diagnostic => diagnostic.ObjectReference));
        Assert.All(reported, diagnostic => Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));
        using PdfDocument saved = PdfDocument.Open(file);
        Assert.Single(saved.Pages);
        Assert.All(saved.Outline!.Items, item => Assert.Null(((PdfExplicitDestination)item.Destination!).Page));
        AssertQpdfCheck(file);
    }

    [Fact]
    public void In_strict_mode_a_save_with_an_outline_item_pointing_at_a_removed_page_throws_before_writing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline.pdf"), new PdfOptions().UseStrict());
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        document.Pages.RemoveAt(0);
        using var stream = new MemoryStream();

        DiagnosticException thrown = Assert.Throws<DiagnosticException>(() => document.Save(stream));

        Assert.Equal("DestinationPageRemoved", thrown.Diagnostic.Code);
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void Blank_pages_inserted_anywhere_have_their_media_box_and_no_content(bool incremental)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        string[] texts = [.. document.Pages.Select(page => PageFacts.Of(document, page).Text)];

        PdfPage first = document.Pages.Insert(0, new PdfRectangle(0, 0, 200, 100));
        PdfPage middle = document.Pages.Insert(2, new PdfRectangle(0, 0, 595.5, 842));
        PdfPage last = document.Pages.Add(new PdfRectangle(10, 20, 30, 40));

        Assert.Equal([first, document.Pages[1], middle, document.Pages[3], last], document.Pages);
        Assert.Equal(new PdfRectangle(0, 0, 595.5, 842), middle.MediaBox);
        Assert.Equal(2, document.Pages.IndexOf(middle));
        byte[] file = Save(document, incremental);
        using PdfDocument saved = PdfDocument.Open(file);
        Assert.Equal(5, saved.Pages.Count);
        Assert.Equal(new PdfRectangle(0, 0, 200, 100), saved.Pages[0].MediaBox);
        Assert.Equal(new PdfRectangle(0, 0, 595.5, 842), saved.Pages[2].MediaBox);
        Assert.Equal(new PdfRectangle(10, 20, 30, 40), saved.Pages[4].MediaBox);
        Assert.Equal(texts, new[] { PageFacts.Of(saved, saved.Pages[1]).Text, PageFacts.Of(saved, saved.Pages[3]).Text });
        foreach (int blank in new[] { 0, 2, 4 })
        {
            Assert.False(saved.Pages[blank].Dictionary.ContainsKey(Contents));
            Assert.Empty(saved.Pages[blank].Resources!);
            Assert.Empty(saved.Pages[blank].ExtractText().Glyphs);
        }

        Assert.DoesNotContain(saved.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        PageTreeShape.AssertBalanced(saved, maxKids: 32);
        AssertQpdfCheck(file);
    }

    [Fact]
    public void A_page_object_added_to_the_document_is_inserted_into_the_page_tree()
    {
        using PdfDocument document = PdfDocument.Create();
        var page = new CosDictionary
        {
            [MediaBox] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(300), new CosInteger(300)]),
            [new CosName("Resources")] = new CosDictionary(),
        };
        CosReference reference = document.AddObject(page);

        PdfPage inserted = document.Pages.Insert(0, reference);

        Assert.Same(page, inserted.Dictionary);
        Assert.Equal(reference, inserted.Reference);
        Assert.Equal(new CosName("Page"), page[Type]);
        Assert.Equal(2, document.Pages.Count);
        using PdfDocument saved = PdfDocument.Open(Save(document, incremental: false));
        Assert.Equal(new PdfRectangle(0, 0, 300, 300), saved.Pages[0].MediaBox);
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), saved.Pages[1].MediaBox);
        PageTreeShape.AssertBalanced(saved, maxKids: 32);
    }

    [Fact]
    public void A_page_already_in_the_tree_or_an_object_that_is_not_a_page_is_refused()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        CosReference root = (CosReference)document.Catalog[new CosName("Pages")];
        CosReference font = document.AddObject(new CosDictionary { [Type] = new CosName("Font") });
        CosReference number = document.AddObject(new CosInteger(1));

        Assert.Throws<ArgumentException>(() => document.Pages.Insert(0, document.Pages[1].Reference!));
        Assert.Throws<ArgumentException>(() => document.Pages.Insert(0, root));
        Assert.Throws<ArgumentException>(() => document.Pages.Add(font));
        Assert.Throws<ArgumentException>(() => document.Pages.Add(number));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Pages.Insert(3, new PdfRectangle(0, 0, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Pages.Move(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Pages.RemoveAt(-1));
        Assert.Equal(2, document.Pages.Count);
        Assert.False(document.Pages[0].Dictionary.ContainsKey(MediaBox));
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void A_large_document_is_rebuilt_as_a_balanced_tree_and_keeps_its_order_through_moves_and_removals(bool incremental)
    {
        using PdfDocument document = PdfDocument.Create();
        for (int index = 1; index < 1000; index++)
        {
            document.Pages.Add(new PdfRectangle(0, 0, index + 1, 100));
        }

        PageTreeShape.AssertBalanced(document, maxKids: 32);
        document.Pages.Move(999, 0);
        document.Pages.Move(1, 500);
        document.Pages.RemoveAt(10);
        var expected = new List<double>(Enumerable.Range(1, 1000).Select(width => (double)(width == 1 ? 612 : width)));
        expected.Insert(0, expected[999]);
        expected.RemoveAt(1000);
        double moved = expected[1];
        expected.RemoveAt(1);
        expected.Insert(500, moved);
        expected.RemoveAt(10);

        Assert.Equal(expected, document.Pages.Select(page => page.MediaBox.Width));
        byte[] file = Save(document, incremental);
        using PdfDocument saved = PdfDocument.Open(file);
        Assert.Equal(expected, saved.Pages.Select(page => page.MediaBox.Width));
        Assert.Equal(saved.Pages.Select(page => page.Dictionary), PageTreeShape.AssertBalanced(saved, maxKids: 32), ReferenceEqualityComparer.Instance);
        Assert.DoesNotContain(saved.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        AssertQpdfCheck(file);
    }

    [Fact]
    public void Views_and_destinations_follow_a_page_through_an_edit()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("outline.pdf"));
        PdfPage original = document.Pages[0];
        var destination = (PdfExplicitDestination)document.Outline!.Items[0].Destination!;
        Assert.Equal(0, destination.PageIndex);

        document.Pages.Insert(0, new PdfRectangle(0, 0, 100, 100));

        Assert.Same(original, document.Pages[1]);
        Assert.Equal(1, destination.PageIndex);
        Assert.Same(original, destination.Page);
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), original.MediaBox);
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void Rotating_a_page_writes_its_own_rotation_normalized_to_a_quarter_turn(bool incremental)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        ((CosDictionary)document.Resolve(document.Pages[0].Dictionary[Parent]))[Rotate] = new CosInteger(90);

        document.Pages[0].Rotation = 0;
        document.Pages[1].Rotation = -90;

        Assert.Equal(new CosInteger(0), document.Pages[0].Dictionary[Rotate]);
        Assert.Equal(new CosInteger(270), document.Pages[1].Dictionary[Rotate]);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Pages[0].Rotation = 45);
        Assert.Equal(0, document.Pages[0].Rotation);
        using PdfDocument saved = PdfDocument.Open(Save(document, incremental));
        Assert.Equal([0, 270], saved.Pages.Select(page => page.Rotation));
    }

    [Fact]
    public void Rotating_a_page_back_to_no_rotation_removes_its_entry_when_nothing_is_inherited()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-rotation.pdf"));
        PdfPage page = document.Pages[0];
        Assert.Equal(90, page.Rotation);

        page.Rotation = 720;

        Assert.False(page.Dictionary.ContainsKey(Rotate));
        Assert.Equal(0, page.Rotation);
        page.Rotation = 450;
        Assert.Equal(90, page.Rotation);
    }

    [Fact]
    public void Links_and_named_destinations_to_a_removed_page_are_reported_on_save_and_others_are_not()
    {
        using PdfDocument document = PdfDocument.Create();
        CosReference removed = document.Pages.Add(new PdfRectangle(0, 0, 612, 792)).Reference!;
        CosReference kept = document.Pages[0].Reference!;
        CosReference brokenLink = Link(document, removed);
        Link(document, kept);
        CosReference namedTarget = document.AddObject(new CosArray([removed, new CosName("Fit")]));
        document.Catalog[new CosName("Dests")] = new CosDictionary
        {
            [new CosName("gone")] = namedTarget,
            [new CosName("here")] = new CosArray([kept, new CosName("Fit")]),
        };

        document.Pages.RemoveAt(1);
        using var stream = new MemoryStream();
        document.Save(stream);

        Diagnostic[] reported = [.. document.Diagnostics.Where(diagnostic => diagnostic.Code == "DestinationPageRemoved")];
        Assert.Equal([brokenLink, namedTarget], reported.Select(diagnostic => diagnostic.ObjectReference));
        Assert.Contains("gone", reported[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Moving_a_page_within_one_node_updates_only_that_node_in_an_incremental_save()
    {
        using PdfDocument document = PdfDocument.Create();
        for (int index = 1; index < 100; index++)
        {
            document.Pages.Add(new PdfRectangle(0, 0, 100, 100));
        }

        byte[] first = Save(document, incremental: true);
        CosReference node = (CosReference)document.Pages[99].Dictionary[Parent];

        document.Pages.Move(99, 98);
        byte[] second = Save(document, incremental: true);

        string update = System.Text.Encoding.Latin1.GetString(second.AsSpan(first.Length));
        string[] written = [.. System.Text.RegularExpressions.Regex.Matches(update, @"(\d+) (\d+) obj").Select(match => match.Value)];
        Assert.Equal([$"{node.ObjectNumber} {node.Generation} obj"], written);
        using PdfDocument saved = PdfDocument.Open(second);
        Assert.Same(saved.Pages[98].Dictionary, saved.Resolve(document.Pages[98].Reference));
        PageTreeShape.AssertBalanced(saved, maxKids: 32);
    }

    [Fact]
    public void A_root_that_is_itself_a_page_gets_a_page_tree_node_above_it_when_a_page_is_added()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Page /Resources << >> /MediaBox [0 0 612 792] >>"));

        document.Pages.Add(new PdfRectangle(0, 0, 100, 100));

        using PdfDocument saved = PdfDocument.Open(Save(document, incremental: false));
        Assert.Equal([new PdfRectangle(0, 0, 612, 792), new PdfRectangle(0, 0, 100, 100)], saved.Pages.Select(page => page.MediaBox));
        Assert.Empty(saved.Diagnostics);
        PageTreeShape.AssertBalanced(saved, maxKids: 32);
    }

    [Fact]
    public void Direct_kids_and_a_direct_inherited_resource_dictionary_become_indirect_and_shared_when_the_tree_is_rebuilt()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >> 3 0 R] /Count 2 /Resources << /Font << /F1 4 0 R >> >> >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        PdfPage direct = document.Pages[0];
        CosDictionary resources = direct.Resources!;
        Assert.Null(direct.Reference);

        document.Pages.Move(1, 0);

        Assert.NotNull(direct.Reference);
        Assert.Same(direct, document.Pages[1]);
        CosReference shared = Assert.IsType<CosReference>(document.Pages[0].Dictionary[new CosName("Resources")]);
        Assert.Equal(shared, document.Pages[1].Dictionary[new CosName("Resources")]);
        Assert.Same(resources, document.Resolve(shared));
        using PdfDocument saved = PdfDocument.Open(Save(document, incremental: true));
        Assert.Equal([new PdfRectangle(0, 0, 612, 792), new PdfRectangle(0, 0, 10, 10)], saved.Pages.Select(page => page.MediaBox));
        Assert.All(saved.Pages, page => Assert.NotNull(page.GetFont("F1")));
        Assert.DoesNotContain(saved.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
        PageTreeShape.AssertBalanced(saved, maxKids: 32);
    }

    private static byte[] Save(PdfDocument document, bool incremental)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithIncrementalUpdate(incremental));
        return stream.ToArray();
    }

    /// <summary>Adds a link annotation to the first page whose destination shows <paramref name="target"/>.</summary>
    private static CosReference Link(PdfDocument document, CosReference target)
    {
        CosDictionary page = document.Pages[0].Dictionary;
        if (!page.TryGetValue(new CosName("Annots"), out CosObject? annots))
        {
            annots = new CosArray();
            page[new CosName("Annots")] = annots;
        }

        CosReference link = document.AddObject(new CosDictionary
        {
            [Type] = new CosName("Annot"),
            [new CosName("Subtype")] = new CosName("Link"),
            [new CosName("Rect")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(10), new CosInteger(10)]),
            [new CosName("Dest")] = new CosArray([target, new CosName("Fit")]),
        });
        ((CosArray)annots).Add(link);
        return link;
    }

    private static void AssertQpdfCheck(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    /// <summary>What identifies a page and what it inherits.</summary>
    private sealed record PageFacts(string Text, PdfRectangle MediaBox, int Rotation, CosDictionary Resources)
    {
        public static PageFacts Of(PdfDocument document, PdfPage page)
        {
            var content = (CosStream)document.Resolve(page.Dictionary[Contents]);
            return new PageFacts(System.Text.Encoding.Latin1.GetString(content.EncodedData.Span), page.MediaBox, page.Rotation, page.Resources!);
        }
    }
}
