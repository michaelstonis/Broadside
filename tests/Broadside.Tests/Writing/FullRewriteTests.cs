using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>A full rewrite that numbers objects densely and drops those the trailer does not reach. ISO 32000-2 §7.5, §7.3.10.</summary>
public sealed class FullRewriteTests
{
    private static readonly CosName Type = new("Type");
    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName Kids = new("Kids");
    private static readonly CosName Count = new("Count");
    private static readonly CosName Annots = new("Annots");
    private static readonly CosName Parent = new("Parent");

    [Fact]
    public void A_page_removed_from_the_page_tree_is_absent_from_a_rewrite_that_drops_unreachable_objects()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        Assert.Equal(2, source.Pages.Count);
        byte[] removedContent = ((CosStream)source.Resolve(source.Pages[1].Dictionary[new CosName("Contents")])).EncodedData.ToArray();
        RemoveLastPage(source);

        using PdfDocument saved = PdfDocument.Open(Save(source, Rewrite()));

        Assert.Single(saved.Pages);
        CosGraph.AssertIsomorphic(source, saved);
        CosObject[] written = [.. CosGraph.AllObjects(saved).Select(saved.Resolve)];
        Assert.Single(written, value => value is CosDictionary dictionary && dictionary.TryGetValue(Type, out CosObject? type) && new CosName("Page").Equals(type));
        Assert.DoesNotContain(written, value => value is CosStream stream && stream.EncodedData.Span.SequenceEqual(removedContent));
        Assert.Equal(CosGraph.Reachable(saved).Count, written.Length);
    }

    [Fact]
    public void An_annotation_removed_from_its_page_is_absent_from_a_rewrite_that_drops_unreachable_objects()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        var annotations = (CosArray)source.Resolve(source.Pages[0].Dictionary[Annots]);
        int square = annotations.ToList().FindIndex(item => IsAnnotation(source, item, "Square"));
        Assert.True(square >= 0);
        annotations.RemoveAt(square);

        using PdfDocument saved = PdfDocument.Open(Save(source, Rewrite()));

        CosGraph.AssertIsomorphic(source, saved);
        CosObject[] written = [.. CosGraph.AllObjects(saved).Select(saved.Resolve)];
        Assert.DoesNotContain(written, value => IsAnnotation(saved, value, "Square"));
        Assert.Contains(written, value => IsAnnotation(saved, value, "Circle"));
        Assert.Equal(CosGraph.Reachable(saved).Count, written.Length);
    }

    public static TheoryData<string, PdfCrossReferenceLayout> FilesAndLayouts
    {
        get
        {
            var data = new TheoryData<string, PdfCrossReferenceLayout>();
            foreach (string file in Corpus.WellFormedFileNames.Where(name => !name.StartsWith("encrypted-", StringComparison.Ordinal)))
            {
                foreach (PdfCrossReferenceLayout layout in Enum.GetValues<PdfCrossReferenceLayout>())
                {
                    data.Add(file, layout);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FilesAndLayouts))]
    public void A_compact_rewrite_holds_exactly_a_graph_isomorphic_to_the_one_the_source_reaches(string fileName, PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));

        using PdfDocument saved = PdfDocument.Open(Save(source, Rewrite(layout)));

        IReadOnlyDictionary<CosReference, CosReference> bijection = CosGraph.AssertIsomorphic(source, saved);
        Assert.Equal(CosGraph.Reachable(source).Count, bijection.Count);
        Assert.Equal(bijection.Count, CosGraph.AllObjects(saved).Count(reference => !IsFileStructure(saved.Resolve(reference))));
        Assert.Equal(source.Pages.Count, saved.Pages.Count);
        Assert.DoesNotContain(saved.Diagnostics, diagnostic => diagnostic.Severity > Broadside.Diagnostics.DiagnosticSeverity.Information);
    }

    [Theory]
    [MemberData(nameof(FilesAndLayouts))]
    public void A_compact_rewrite_passes_qpdf_check(string fileName, PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));

        (int exitCode, string output) = ExternalTool.Run("qpdf", Save(source, Rewrite(layout)), "--check");

        // qpdf 12 compares name-tree keys as decoded text, not bytes as ISO 32000-2 §7.9.6 requires, and warns (exit code 3) on
        // name-tree-deep.pdf (see SaveTests).
        Assert.True(exitCode == 0 || (fileName == "name-tree-deep.pdf" && exitCode == 3 && output.Contains("keys are not sorted", StringComparison.Ordinal)), output);
    }

    [Theory]
    [InlineData("annotations-subtypes.pdf", "Annot", "Subtype", "3D")]
    [InlineData("annotations-subtypes.pdf", "Annot", "Subtype", "RichMedia")]
    [InlineData("annotations-subtypes.pdf", "Annot", "Subtype", "Movie")]
    [InlineData("annotations-subtypes.pdf", "Annot", "Subtype", "Screen")]
    [InlineData("actions-all.pdf", "Action", "S", "JavaScript")]
    [InlineData("actions-all.pdf", "Action", "S", "RichMediaExecute")]
    [InlineData("actions-all.pdf", "Action", "S", "Rendition")]
    [InlineData("acroform-xfa.pdf", "AcroForm", "XFA", null)]
    public void Objects_the_library_does_not_interpret_survive_a_compact_rewrite_unchanged(string fileName, string kind, string key, string? value)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));
        CosObject[] expected = [.. Find(source, key, value)];
        Assert.NotEmpty(expected);

        using PdfDocument saved = PdfDocument.Open(Save(source, Rewrite()));

        CosObject[] actual = [.. Find(saved, key, value)];
        Assert.True(expected.Length == actual.Length, $"{expected.Length} {kind} objects became {actual.Length}.");
        for (int index = 0; index < expected.Length; index++)
        {
            CosGraph.AssertIsomorphic(source, expected[index], saved, actual[index]);
        }
    }

    [Fact]
    public void Automatic_numbering_keeps_the_numbers_and_bytes_of_what_remains_and_frees_the_numbers_of_what_was_dropped()
    {
        byte[] file = Corpus.Bytes("page-tree-inherited.pdf");
        using PdfDocument source = PdfDocument.Open(file);
        CosReference removedPage = PageReference(source, 1);
        RemoveLastPage(source);

        byte[] output = Save(source, new PdfSaveOptions().WithRemoveUnreachableObjects(true));

        using PdfDocument saved = PdfDocument.Open(output);
        Assert.IsType<CosNull>(saved.Resolve(removedPage));
        Dictionary<(int Number, int Generation), byte[]> before = ObjectBodies.Read(file);
        Dictionary<(int Number, int Generation), byte[]> after = ObjectBodies.Read(output);
        Assert.Equal(CosGraph.Reachable(source).Select(reference => (reference.ObjectNumber, reference.Generation)).Order(), after.Keys.Order());
        foreach ((int number, int generation) in after.Keys)
        {
            var reference = new CosReference(number, generation);
            Assert.True(CosObject.DeepEquals(source.Resolve(reference), saved.Resolve(reference)), $"Object {reference} differs.");
            if (!source.Resolve(reference).IsDirty)
            {
                Assert.Equal(before[(number, generation)], after[(number, generation)]);
            }
        }
    }

    [Fact]
    public void Compact_numbering_without_garbage_collection_numbers_every_object_from_1_and_keeps_unreachable_ones()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        int objects = CosGraph.AllObjects(source).Count;
        RemoveLastPage(source);

        using PdfDocument saved = PdfDocument.Open(Save(source, new PdfSaveOptions().WithObjectNumbering(PdfObjectNumbering.Compact)));

        Assert.Equal(objects + 1, ((CosInteger)saved.Trailer[new CosName("Size")]).Value);
        Assert.Equal(objects, CosGraph.AllObjects(saved).Count);
        Assert.Equal(2, objects - CosGraph.Reachable(saved).Count);
        CosGraph.AssertIsomorphic(source, saved);
    }

    /// <summary>Every object the document reaches whose <paramref name="key"/> is the name <paramref name="value"/>, or which has the key when the value is null.</summary>
    private static IEnumerable<CosObject> Find(PdfDocument document, string key, string? value)
    {
        var name = new CosName(key);
        foreach (CosReference reference in CosGraph.Reachable(document).OrderBy(reference => reference.ObjectNumber))
        {
            var pending = new Stack<(CosObject Value, CosObject Handle)>();
            CosObject resolved = document.Resolve(reference);
            pending.Push((resolved, reference));
            while (pending.TryPop(out (CosObject Value, CosObject Handle) item))
            {
                CosDictionary? dictionary = item.Value as CosDictionary ?? (item.Value as CosStream)?.Dictionary;
                if (dictionary is not null && dictionary.TryGetValue(name, out CosObject? found) && (value is null || new CosName(value).Equals(found)))
                {
                    yield return value is null ? found : item.Handle;
                }

                IEnumerable<CosObject> children = item.Value switch
                {
                    CosArray array => array,
                    CosDictionary direct => direct.Values,
                    _ => [],
                };
                foreach (CosObject child in children.Reverse().Where(child => child is CosArray or CosDictionary))
                {
                    pending.Push((child, child));
                }
            }
        }
    }

    private static bool IsAnnotation(PdfDocument document, CosObject value, string subtype) =>
        document.Resolve(value) is CosDictionary dictionary
        && dictionary.TryGetValue(Subtype, out CosObject? found)
        && new CosName(subtype).Equals(found);

    /// <summary>Whether an object is one the writer adds to hold others: an object stream or a cross-reference stream (§7.5.7, §7.5.8).</summary>
    private static bool IsFileStructure(CosObject value) =>
        value is CosStream stream
        && stream.Dictionary.TryGetValue(Type, out CosObject? type)
        && (new CosName("ObjStm").Equals(type) || new CosName("XRef").Equals(type));

    private static CosReference PageReference(PdfDocument document, int index)
    {
        CosDictionary page = document.Pages[index].Dictionary;
        var parent = (CosDictionary)document.Resolve(page[Parent]);
        return ((CosArray)document.Resolve(parent[Kids])).OfType<CosReference>().Single(kid => ReferenceEquals(document.Resolve(kid), page));
    }

    /// <summary>The options of a compact rewrite: dense numbers and no unreachable objects.</summary>
    private static PdfSaveOptions Rewrite(PdfCrossReferenceLayout layout = PdfCrossReferenceLayout.Table) =>
        new PdfSaveOptions().WithCrossReferenceLayout(layout).WithObjectNumbering(PdfObjectNumbering.Compact).WithRemoveUnreachableObjects(true);

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }

    /// <summary>
    /// Removes the last page from its parent's <c>Kids</c> and lowers every ancestor's <c>Count</c> (ISO 32000-2 §7.7.3.2), through
    /// the COS layer.
    /// </summary>
    private static void RemoveLastPage(PdfDocument document)
    {
        CosDictionary page = document.Pages[^1].Dictionary;
        var parent = (CosDictionary)document.Resolve(page[Parent]);
        var kids = (CosArray)document.Resolve(parent[Kids]);
        kids.RemoveAt(kids.ToList().FindIndex(kid => ReferenceEquals(document.Resolve(kid), page)));
        for (CosObject node = parent; node is CosDictionary ancestor; node = document.Resolve(ancestor.TryGetValue(Parent, out CosObject? up) ? up : null))
        {
            ancestor[Count] = new CosInteger(((CosInteger)document.Resolve(ancestor[Count])).Value - 1);
        }
    }
}
