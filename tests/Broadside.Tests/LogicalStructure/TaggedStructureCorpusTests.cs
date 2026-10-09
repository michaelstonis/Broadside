using System.Text;
using System.Text.RegularExpressions;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.TestSupport;

namespace Broadside.Tests.LogicalStructure;

/// <summary>The tagged corpus file read through the structure tree. ISO 32000-2 §14.6-§14.8; ISO/TS 32005 §5.</summary>
public partial class TaggedStructureCorpusTests
{
    [Fact]
    public Task The_tagged_file_yields_the_expected_tree()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));

        PdfStructureTreeRoot root = Assert.IsType<PdfStructureTreeRoot>(document.StructureTree);
        string tree = StructureProjection.Of(document, root);

        Assert.Empty(document.Diagnostics);
        return Verify(tree);
    }

    [Fact]
    public void Every_marked_content_sequence_of_the_page_maps_to_the_element_tagged_like_it_through_the_parent_tree()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfStructureTreeRoot root = document.StructureTree!;
        PdfPage page = document.Pages[0];

        // The content stream is the independent source: each "/Tag <</MCID n>> BDC" names the element type that owns n.
        string content = Encoding.Latin1.GetString(document.DecodeStream((CosStream)document.Resolve(page.Dictionary[new CosName("Contents")])).Span);
        MatchCollection sequences = MarkedContentWithMcid().Matches(content);
        Assert.Equal(10, sequences.Count);

        IReadOnlyDictionary<int, PdfStructureElement> index = root.GetMarkedContentElements(page);
        Assert.Equal(10, index.Count);
        foreach (Match sequence in sequences)
        {
            int mcid = int.Parse(sequence.Groups["mcid"].Value, System.Globalization.CultureInfo.InvariantCulture);
            string tag = sequence.Groups["tag"].Value;
            PdfStructureElement element = Assert.IsType<PdfStructureElement>(root.FindElement(page, mcid));
            Assert.Equal(tag, element.StandardType!.Name);
            Assert.Same(element.Dictionary, index[mcid].Dictionary);
            Assert.Contains(element.GetContentItems(), item => item is PdfMarkedContentReference reference && reference.Mcid == mcid && reference.Page == page);
        }

        Assert.Null(root.FindElement(page, 10));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_link_annotation_finds_its_paragraph_through_its_struct_parent()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfStructureTreeRoot root = document.StructureTree!;
        CosObject annotation = ((CosArray)document.Pages[0].Dictionary[new CosName("Annots")])[0];

        PdfStructureElement paragraph = Assert.IsType<PdfStructureElement>(root.FindElementForObject(annotation));

        Assert.Equal("Para", paragraph.StructureType!.Value);
        PdfObjectReference link = Assert.Single(paragraph.Children.OfType<PdfObjectReference>());
        Assert.Same(document.Resolve(annotation), link.ReferencedObject);
        Assert.Same(document.Pages[0], link.Page);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Elements_are_found_by_id_through_the_id_tree()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfStructureTreeRoot root = document.StructureTree!;

        Assert.Equal("Table", root.FindElementById("tbl1"u8)!.StructureType!.Value);
        Assert.Equal("TH", root.FindElementById("h1"u8)!.StructureType!.Value);
        Assert.Null(root.FindElementById("missing"u8));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Lookups_return_elements_that_know_their_parents()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfStructureTreeRoot root = document.StructureTree!;

        PdfStructureElement cell = root.FindElement(document.Pages[0], 9)!;

        Assert.Equal(["TD", "TR", "Table", "Chapter", "Document"], Ancestry(cell));
        Assert.Equal("en-US", cell.EffectiveLanguage);
        Assert.Equal("ipa", cell.PhoneticAlphabet);
        Assert.Equal(18, root.Elements.Count);
        Assert.Equal(["Document", "Chapter", "H1", "Para", "L", "LI", "Lbl", "LBody", "LI", "Lbl", "LBody", "Table", "TR", "TH", "TH", "TR", "TD", "TD"], root.Elements.Select(static element => element.StructureType!.Value));
    }

    [Fact]
    public void Mark_information_says_the_document_is_tagged()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));

        Assert.True(document.MarkInfo.Marked);
        Assert.False(document.MarkInfo.UserProperties);
        Assert.False(document.MarkInfo.Suspects);
    }

    [Fact]
    public void Attributes_resolve_with_a_over_c_inheritance_and_defaults()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfStructureTreeRoot root = document.StructureTree!;
        var layout = new CosName("Layout");
        var list = new CosName("List");
        var table = new CosName("Table");
        PdfStructureElement heading = root.FindElement(document.Pages[0], 0)!;
        PdfStructureElement label = root.FindElement(document.Pages[0], 2)!;
        PdfStructureElement header = root.FindElementById("h1"u8)!;
        PdfStructureElement cell = root.FindElement(document.Pages[0], 8)!;

        // C /Centered gives TextAlign Center and SpaceAfter 6; A gives SpaceAfter 12, which wins.
        Assert.Equal(new CosName("Center"), heading.GetAttributeValue(layout, new CosName("TextAlign")));
        Assert.Equal(new CosInteger(12), heading.GetAttributeValue(layout, new CosName("SpaceAfter")));
        Assert.Equal(6, Assert.IsType<PdfLayoutAttributes>(Assert.Single(heading.ClassAttributes)).SpaceAfter);
        Assert.Equal(12, Assert.IsType<PdfLayoutAttributes>(Assert.Single(heading.Attributes)).SpaceAfter);

        // ListNumbering is inheritable: the label inherits Decimal from L; TextAlign falls back to its default.
        Assert.Equal(new CosName("Decimal"), label.GetAttributeValue(list, new CosName("ListNumbering")));
        Assert.Equal(new CosName("Start"), label.GetAttributeValue(layout, new CosName("TextAlign")));
        Assert.True(Assert.IsType<PdfListAttributes>(Assert.Single(label.Parent!.Parent!.Attributes)).IsOrdered);

        // Table attributes are not inheritable: RowSpan defaults to 1, Scope applies only to the header itself.
        PdfTableAttributes scope = Assert.IsType<PdfTableAttributes>(Assert.Single(header.Attributes));
        Assert.Equal("Column", scope.Scope);
        Assert.Equal(0, scope.Revision);
        Assert.Equal(new CosInteger(1), cell.GetAttributeValue(table, new CosName("RowSpan")));
        Assert.Null(cell.GetAttributeValue(table, new CosName("Scope")));
        Assert.Equal("h1", Encoding.ASCII.GetString(Assert.Single(Assert.IsType<PdfTableAttributes>(Assert.Single(cell.Attributes)).Headers!).Bytes));
        Assert.Equal("Names and values", Assert.IsType<PdfTableAttributes>(Assert.Single(root.FindElementById("tbl1"u8)!.Attributes)).Summary);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Role_mapping_follows_the_root_role_map_and_the_custom_namespace_role_map()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfStructureTreeRoot root = document.StructureTree!;
        PdfStructureElement chapter = root.Children[0].Children.OfType<PdfStructureElement>().Single();
        PdfStructureElement paragraph = root.FindElement(document.Pages[0], 1)!;

        Assert.Equal(PdfStructureNamespaceKind.Custom, chapter.Namespace.Kind);
        Assert.Equal("https://example.com/broadside-corpus", chapter.Namespace.Name);
        Assert.Equal([new PdfStructureType("Chapter", chapter.Namespace), new PdfStructureType("Sect", PdfStructureNamespace.Pdf20)], chapter.RoleMapping);
        Assert.True(paragraph.Namespace.IsDefault);
        Assert.Equal([new PdfStructureType("Para", PdfStructureNamespace.Pdf17), new PdfStructureType("P", PdfStructureNamespace.Pdf17)], paragraph.RoleMapping);
        Assert.Equal([PdfStructureNamespaceKind.Pdf20, PdfStructureNamespaceKind.Custom], root.Namespaces.Select(static ns => ns.Kind));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Reading_the_whole_tree_changes_no_object()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        PdfStructureTreeRoot root = document.StructureTree!;

        _ = StructureProjection.Of(document, root);
        foreach (PdfStructureElement element in root.Elements)
        {
            _ = (element.StandardType, element.EffectiveLanguage, element.PhoneticAlphabet, element.ClassAttributes, element.References, element.Page);
        }

        _ = root.GetMarkedContentElements(document.Pages[0]);
        Assert.False(document.Catalog.IsDirty);
        Assert.False(root.Dictionary.IsDirty);
        Assert.All(root.Elements, static element => Assert.False(element.Dictionary.IsDirty));
    }

    [Fact]
    public void Concurrent_first_lookups_build_each_index_once_and_agree()
    {
        for (int round = 0; round < 20; round++)
        {
            using PdfDocument document = PdfDocument.Open(Corpus.Bytes("tagged-structure.pdf"));
            PdfStructureTreeRoot root = document.StructureTree!;
            PdfPage page = document.Pages[0];
            var answers = new System.Collections.Concurrent.ConcurrentBag<string>();
            Parallel.For(0, Environment.ProcessorCount * 4, index =>
            {
                var text = new StringBuilder();
                for (int mcid = 0; mcid < 10; mcid++)
                {
                    int probe = (mcid + index) % 10;
                    text.Append(probe).Append('=').Append(root.FindElement(page, probe)!.Reference).Append(';');
                }

                text.Append(root.FindElementById("tbl1"u8)!.Reference).Append(';');
                foreach (PdfStructureElement element in root.Elements)
                {
                    text.Append(element.Reference).Append('<').Append(element.Parent?.Reference).Append(';');
                }

                answers.Add(string.Join(';', text.ToString().Split(';').Order(StringComparer.Ordinal)));
            });

            Assert.Single(answers.Distinct());
            Assert.Empty(document.Diagnostics);
        }
    }

    private static List<string> Ancestry(PdfStructureElement element)
    {
        var types = new List<string>();
        for (PdfStructureElement? current = element; current is not null; current = current.Parent)
        {
            types.Add(current.StructureType!.Value);
        }

        return types;
    }

    [GeneratedRegex(@"/(?<tag>\w+) <</MCID (?<mcid>\d+)>> BDC")]
    private static partial Regex MarkedContentWithMcid();
}
