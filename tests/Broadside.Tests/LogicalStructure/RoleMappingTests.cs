using Broadside.Structure;
using Broadside.Tests.Document;

namespace Broadside.Tests.LogicalStructure;

/// <summary>
/// Resolving structure types to standard types through role maps and namespaces. ISO 32000-2 §14.7.3, §14.7.4, §14.8.4, §14.8.6,
/// Annex M; ISO/TS 32005 §5.
/// </summary>
public class RoleMappingTests
{
    private const string Pdf20 = "<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>";

    public static TheoryData<string, bool, bool> StandardTypeMatrix => new()
    {
        // type, standard in PDF 1.7 (the default namespace), standard in PDF 2.0 (Annex M).
        { "P", true, true },
        { "H", true, true },
        { "H1", true, true },
        { "H6", true, true },
        { "H7", false, true },
        { "H12", false, true },
        { "H0", false, false },
        { "Hx", false, false },
        { "Document", true, true },
        { "DocumentFragment", false, true },
        { "Aside", false, true },
        { "Title", false, true },
        { "FENote", false, true },
        { "Sub", false, true },
        { "Em", false, true },
        { "Strong", false, true },
        { "Artifact", false, true },
        { "Art", true, false },
        { "BlockQuote", true, false },
        { "TOC", true, false },
        { "TOCI", true, false },
        { "Index", true, false },
        { "Private", true, false },
        { "Quote", true, false },
        { "Note", true, false },
        { "Reference", true, false },
        { "BibEntry", true, false },
        { "Code", true, false },
        { "Table", true, true },
        { "Ruby", true, true },
        { "Chapter", false, false },
        { "p", false, false },
    };

    [Theory]
    [MemberData(nameof(StandardTypeMatrix))]
    public void Standard_types_differ_between_the_pdf_1_7_and_pdf_2_0_namespaces(string type, bool inPdf17, bool inPdf20)
    {
        Assert.Equal(inPdf17, PdfStructureNamespace.Pdf17.IsStandardType(type));
        Assert.Equal(inPdf20, PdfStructureNamespace.Pdf20.IsStandardType(type));
    }

    [Fact]
    public void An_element_without_ns_is_in_the_pdf_1_7_namespace_even_in_a_pdf_2_0_file()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R 6 0 R] /Namespaces [7 0 R]",
            "<< /S /Em /P 4 0 R >>",
            "<< /S /Em /P 4 0 R /NS 7 0 R >>",
            Pdf20));
        PdfStructureElement[] elements = [.. document.StructureTree!.Children];

        Assert.True(elements[0].Namespace.IsDefault);
        Assert.Null(elements[0].StandardType);
        Assert.Equal(new PdfStructureType("Em", PdfStructureNamespace.Pdf20), elements[1].StandardType);
        Assert.Equal("StructureTypeUnresolved", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void The_role_map_is_followed_as_a_chain_and_maps_even_standard_types()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R 6 0 R] /RoleMap << /Heading /Title /Title /H1 /P /H2 >>",
            "<< /S /Heading /P 4 0 R >>",
            "<< /S /P /P 4 0 R >>"));
        PdfStructureElement[] elements = [.. document.StructureTree!.Children];

        Assert.Equal(["Heading", "Title", "H1"], elements[0].RoleMapping.Select(static type => type.Name));
        Assert.Equal(new PdfStructureType("H1", PdfStructureNamespace.Pdf17), elements[0].StandardType);
        Assert.Equal("H2", elements[1].StandardType!.Name);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_role_map_cycle_ends_the_chain_at_the_first_standard_type_after_the_start()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R 6 0 R 7 0 R] /RoleMap << /A /B /B /A /P /H1 /H1 /P /Self /Self >>",
            "<< /S /A /P 4 0 R >>",
            "<< /S /P /P 4 0 R >>",
            "<< /S /Self /P 4 0 R >>"));
        PdfStructureElement[] elements = [.. document.StructureTree!.Children];

        Assert.Equal(["A", "B"], elements[0].RoleMapping.Select(static type => type.Name));
        Assert.Null(elements[0].StandardType);
        Assert.Equal(["P", "H1"], elements[1].RoleMapping.Select(static type => type.Name));
        Assert.Equal("H1", elements[1].StandardType!.Name);
        Assert.Equal(["Self"], elements[2].RoleMapping.Select(static type => type.Name));
        Assert.Null(elements[2].StandardType);
        Assert.Equal(["StructureTypeUnresolved"], document.Diagnostics.Select(static diagnostic => diagnostic.Code).Distinct());
    }

    [Fact]
    public void A_namespace_role_map_lands_a_name_in_the_default_namespace_and_an_array_in_the_named_namespace()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R 6 0 R 7 0 R] /Namespaces [8 0 R 9 0 R] /RoleMap << /Para /P /Box /Note >>",
            "<< /S /Para /P 4 0 R /NS 9 0 R >>",
            "<< /S /Box /P 4 0 R /NS 9 0 R >>",
            "<< /S /Aside2 /P 4 0 R /NS 9 0 R >>",
            Pdf20,
            "<< /Type /Namespace /NS (urn:example:custom) /RoleMapNS << /Box /Note /Aside2 [/Aside 8 0 R] >> >>"));
        PdfStructureElement[] elements = [.. document.StructureTree!.Children];

        // The root RoleMap is for the default namespace only: Para in the custom namespace has no mapping.
        Assert.Null(elements[0].StandardType);
        Assert.Equal(new PdfStructureType("Note", PdfStructureNamespace.Pdf17), elements[1].StandardType);
        Assert.Equal(new PdfStructureType("Aside", PdfStructureNamespace.Pdf20), elements[2].StandardType);
        Assert.Equal([PdfStructureNamespaceKind.Custom, PdfStructureNamespaceKind.Pdf20], elements[2].RoleMapping.Select(static type => type.Namespace.Kind));
        Assert.Equal("StructureTypeUnresolved", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_name_landing_in_the_default_namespace_continues_through_the_root_role_map()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R] /Namespaces [6 0 R] /RoleMap << /Block /Div >>",
            "<< /S /Section /P 4 0 R /NS 6 0 R >>",
            "<< /Type /Namespace /NS (urn:example:custom) /RoleMapNS << /Section /Block >> >>"));

        PdfStructureElement element = Assert.Single(document.StructureTree!.Children);

        Assert.Equal(["Section", "Block", "Div"], element.RoleMapping.Select(static type => type.Name));
        Assert.Equal(new PdfStructureType("Div", PdfStructureNamespace.Pdf17), element.StandardType);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Mathml_elements_are_standard_in_their_own_namespace_without_a_role_map()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R] /Namespaces [6 0 R]",
            "<< /S /math /P 4 0 R /NS 6 0 R >>",
            "<< /Type /Namespace /NS (http://www.w3.org/1998/Math/MathML) >>"));

        PdfStructureElement element = Assert.Single(document.StructureTree!.Children);

        Assert.Equal(PdfStructureNamespaceKind.MathML, element.Namespace.Kind);
        Assert.Equal(PdfStructureNamespace.MathML, element.Namespace);
        Assert.Equal(new PdfStructureType("math", PdfStructureNamespace.MathML), element.StandardType);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Two_namespace_dictionaries_with_a_standard_name_are_the_same_namespace()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R 6 0 R] /Namespaces [7 0 R 8 0 R]",
            "<< /S /P /P 4 0 R /NS 7 0 R >>",
            "<< /S /P /P 4 0 R /NS 8 0 R >>",
            Pdf20,
            Pdf20));
        PdfStructureElement[] elements = [.. document.StructureTree!.Children];

        Assert.Equal(elements[0].Namespace, elements[1].Namespace);
        Assert.NotSame(elements[0].Namespace.Dictionary, elements[1].Namespace.Dictionary);
        Assert.Equal(PdfStructureNamespace.Pdf20, elements[0].Namespace);
        Assert.NotEqual(PdfStructureNamespace.Pdf17, elements[0].Namespace);
    }

    [Fact]
    public void An_unresolved_type_in_an_untagged_document_is_not_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            "<< /Type /StructTreeRoot /K [5 0 R] >>",
            "<< /S /Chapter /P 4 0 R >>"));

        Assert.Null(Assert.Single(document.StructureTree!.Children).StandardType);
        Assert.Empty(document.Diagnostics);
    }
}
