using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Tests.StructureTree;

/// <summary>
/// Damaged structure trees: each repair is made in the view, recorded once as a diagnostic in lenient mode, and thrown in strict mode.
/// ISO 32000-2 §14.7.2-§14.7.6 and ADR 0005.
/// </summary>
public sealed class StructureRepairTests
{
    /// <summary>Each case: the diagnostic code it must produce (and nothing else), the root entries, and the objects from 5 on.</summary>
    public static TheoryData<string, string, string[]> Damaged => new()
    {
        {
            "ParentTreeMissing",
            "/K [5 0 R]",
            ["<< /Type /StructElem /S /P /P 4 0 R /Pg 3 0 R /K 0 >>"]
        },
        {
            "ParentTreeEntryInvalid",
            "/K [5 0 R 6 0 R] /ParentTree << /Nums [0 [6 0 R]] >>",
            ["<< /Type /StructElem /S /P /P 4 0 R /Pg 3 0 R /K 0 >>", "<< /Type /StructElem /S /H1 /P 4 0 R >>"]
        },
        {
            "ParentTreeEntryInvalid",
            "/K [5 0 R] /ParentTree << /Nums [0 5 0 R] >>",
            ["<< /Type /StructElem /S /P /P 4 0 R /Pg 3 0 R /K 0 >>"]
        },
        {
            "McidDuplicate",
            "/K [5 0 R 6 0 R] /ParentTree << /Nums [0 [5 0 R]] >>",
            ["<< /Type /StructElem /S /P /P 4 0 R /Pg 3 0 R /K 0 >>", "<< /Type /StructElem /S /H1 /P 4 0 R /Pg 3 0 R /K 0 >>"]
        },
        {
            "StructElemPageMissing",
            "/K [5 0 R] /ParentTree << /Nums [0 [6 0 R]] >>",
            ["<< /Type /StructElem /S /Div /P 4 0 R /Pg 3 0 R /K [6 0 R] >>", "<< /Type /StructElem /S /P /P 5 0 R /K 0 >>"]
        },
        {
            "StructElemParentMismatch",
            "/K [5 0 R]",
            ["<< /Type /StructElem /S /Div /P 4 0 R /K [6 0 R] >>", "<< /Type /StructElem /S /P /P 4 0 R >>"]
        },
        {
            "IdTreeDuplicate",
            "/K [5 0 R 6 0 R] /IDTree << /Names [(id) 5 0 R] >>",
            ["<< /Type /StructElem /S /P /P 4 0 R /ID (id) >>", "<< /Type /StructElem /S /P /P 4 0 R /ID (id) >>"]
        },
        {
            "IdTreeEntryInvalid",
            "/K [5 0 R 6 0 R] /IDTree << /Names [(id) 5 0 R] >>",
            ["<< /Type /StructElem /S /P /P 4 0 R /ID (id) >>", "<< /Type /StructElem /S /P /P 4 0 R /ID (other) >>"]
        },
        {
            "StructTreeCycle",
            "/K [5 0 R]",
            ["<< /Type /StructElem /S /Div /P 4 0 R /K [6 0 R] >>", "<< /Type /StructElem /S /Div /P 5 0 R /K [5 0 R] >>"]
        },
        {
            "StructElemTypeUnknown",
            "/K [5 0 R]",
            ["<< /Type /Annot /S /P /P 4 0 R >>"]
        },
        {
            "NamespaceNotDeclared",
            "/K [5 0 R]",
            ["<< /Type /StructElem /S /P /P 4 0 R /NS 6 0 R >>", "<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>"]
        },
        {
            "StructureTypeUnresolved",
            "/K [5 0 R]",
            ["<< /Type /StructElem /S /Em /P 4 0 R >>"]
        },
        {
            "AttributeRevisionInvalid",
            "/K [5 0 R]",
            ["<< /Type /StructElem /S /P /P 4 0 R /A [1 << /O /Layout /TextAlign /End >> 2 3] >>"]
        },
        {
            "AttributeOwnerInvalid",
            "/K [5 0 R]",
            ["<< /Type /StructElem /S /P /P 4 0 R /A << /O /NSO /Foo 1 >> >>"]
        },
        {
            "StructTreeRootInvalid",
            "/K [0 5 0 R]",
            ["<< /Type /StructElem /S /P /P 4 0 R >>"]
        },
    };

    [Theory]
    [MemberData(nameof(Damaged))]
    public void A_damaged_tree_is_read_with_exactly_one_kind_of_diagnostic(string code, string rootEntries, string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(rootEntries, objects));

        TaggedPdf.ReadEverything(document);

        Assert.Equal([code], document.Diagnostics.Select(static diagnostic => diagnostic.Code).Distinct());
        Assert.All(document.Diagnostics, static diagnostic => Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));
    }

    [Theory]
    [MemberData(nameof(Damaged))]
    public void Strict_mode_throws_the_same_diagnostic(string code, string rootEntries, string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(rootEntries, objects), new PdfOptions().UseStrict());

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => TaggedPdf.ReadEverything(document));

        Assert.Equal(code, error.Diagnostic.Code);
    }

    [Fact]
    public void The_parent_tree_finds_an_element_the_root_does_not_reach()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [] /ParentTree << /Nums [0 [5 0 R]] >>",
            "<< /Type /StructElem /S /P /P 4 0 R /Pg 3 0 R /K 0 >>"));

        PdfStructureElement element = Assert.IsType<PdfStructureElement>(document.StructureTree!.FindElement(document.Pages[0], 0));

        Assert.Equal("P", element.StructureType!.Value);
        Assert.Null(element.Parent);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_parent_tree_entry_naming_the_wrong_element_gives_way_to_the_element_whose_k_holds_the_mcid()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R 6 0 R] /ParentTree << /Nums [0 [6 0 R 6 0 R]] >>",
            "<< /Type /StructElem /S /P /P 4 0 R /Pg 3 0 R /K 0 >>",
            "<< /Type /StructElem /S /H1 /P 4 0 R /Pg 3 0 R /K 1 >>"));
        PdfStructureTreeRoot root = document.StructureTree!;

        Assert.Equal("P", root.FindElement(document.Pages[0], 0)!.StructureType!.Value);
        Assert.Equal("H1", root.FindElement(document.Pages[0], 1)!.StructureType!.Value);
        Assert.Equal("ParentTreeEntryInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Without_a_parent_tree_the_k_hierarchy_answers_lookups()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R]",
            "<< /Type /StructElem /S /P /P 4 0 R /Pg 3 0 R /K [0 << /Type /OBJR /Obj 6 0 R >>] >>",
            "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /StructParent 1 >>"));
        PdfStructureTreeRoot root = document.StructureTree!;

        Assert.Equal("P", root.FindElement(document.Pages[0], 0)!.StructureType!.Value);
        Assert.Equal("P", root.FindElementForObject(new CosReference(6, 0))!.StructureType!.Value);
        Assert.Equal("ParentTreeMissing", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void The_id_tree_finds_an_element_the_root_does_not_reach_and_the_walk_finds_one_it_does_not_list()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [6 0 R] /IDTree << /Names [(far) 5 0 R] >>",
            "<< /Type /StructElem /S /Note /P 4 0 R /ID (far) >>",
            "<< /Type /StructElem /S /P /P 4 0 R /ID (near) >>"));
        PdfStructureTreeRoot root = document.StructureTree!;

        Assert.Equal("Note", root.FindElementById("far"u8)!.StructureType!.Value);
        Assert.Equal("P", root.FindElementById(Encoding.ASCII.GetBytes("near"))!.StructureType!.Value);
        Assert.Equal("IdTreeEntryInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_cycle_through_k_is_cut_so_a_recursive_walk_of_the_children_ends()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R]",
            "<< /Type /StructElem /S /Div /P 4 0 R /K [6 0 R] >>",
            "<< /Type /StructElem /S /Div /P 5 0 R /K [5 0 R 6 0 R] >>"));
        PdfStructureTreeRoot root = document.StructureTree!;

        Assert.Equal(2, CountRecursively(root.Children));
        Assert.Equal(2, root.Elements.Count);
        Assert.Equal("StructTreeCycle", Assert.Single(document.Diagnostics.Select(static diagnostic => diagnostic.Code).Distinct()));
    }

    [Fact]
    public void A_hierarchy_deeper_than_the_limit_is_cut_with_a_diagnostic()
    {
        var objects = new List<string>();
        for (int index = 0; index < 300; index++)
        {
            int number = 5 + index;
            int parent = index == 0 ? 4 : number - 1;
            objects.Add($"<< /Type /StructElem /S /Div /P {parent} 0 R /K [{number + 1} 0 R] >>");
        }

        objects.Add("<< /Type /StructElem /S /P /P 304 0 R >>");
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build("/K [5 0 R]", [.. objects]));
        PdfStructureTreeRoot root = document.StructureTree!;

        Assert.Equal(256, root.Elements.Count);
        Assert.Equal(256, CountRecursively(root.Children));
        Assert.Equal("StructTreeDepthExceeded", Assert.Single(document.Diagnostics.Select(static diagnostic => diagnostic.Code).Distinct()));
    }

    [Fact]
    public void A_k_dictionary_without_type_is_read_by_what_it_holds()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K 5 0 R /ParentTree << /Nums [0 [null 5 0 R]] >>",
            "<< /S /Div /P 4 0 R /K [<< /S /P /P 5 0 R /Pg 3 0 R /K 0 >> << /MCID 1 /Pg 3 0 R >> << /Obj 6 0 R >>] >>",
            "<< /Type /Annot /Subtype /Square /Rect [0 0 1 1] >>"));
        PdfStructureElement division = Assert.Single(document.StructureTree!.Children);

        Assert.Collection(
            division.Children,
            static item => Assert.Equal("P", Assert.IsType<PdfStructureElement>(item).StructureType!.Value),
            static item => Assert.Equal(1, Assert.IsType<PdfMarkedContentReference>(item).Mcid),
            static item => Assert.IsType<PdfObjectReference>(item));
        Assert.Equal(["StructElemTypeUnknown"], document.Diagnostics.Select(static diagnostic => diagnostic.Code).Distinct());
    }

    [Fact]
    public void An_untagged_document_has_no_structure_tree_and_no_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(TestPdfOnePage());

        Assert.Null(document.StructureTree);
        Assert.False(document.MarkInfo.Marked);
        Assert.Null(document.MarkInfo.Dictionary);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Mark_information_that_is_not_boolean_reads_false_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, "/MarkInfo << /Marked 1 /Suspects true >>"));

        Assert.False(document.MarkInfo.Marked);
        Assert.True(document.MarkInfo.Suspects);
        Assert.Equal("MarkInfoInvalid", Assert.Single(document.Diagnostics).Code);
    }

    private static byte[] TestPdfOnePage() => Document.TestPdf.OnePage(string.Empty);

    private static int CountRecursively(IReadOnlyList<PdfStructureItem> items) =>
        items.OfType<PdfStructureElement>().Sum(static element => 1 + CountRecursively(element.Children));
}
