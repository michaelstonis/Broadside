using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Tests.StructureTree;

/// <summary>Content items, their pages and streams, and the per-stream MCID index. ISO 32000-2 §14.7.5, Tables 357-359.</summary>
public class ContentItemTests
{
    private const string Form = "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /StructParents 1 /Length 0 >>\nstream\n\nendstream";

    [Fact]
    public void Mcids_are_scoped_to_their_content_stream_so_a_form_and_its_page_both_have_mcid_0()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R 6 0 R] /ParentTree << /Nums [0 [5 0 R] 1 [6 0 R]] >>",
            "<< /S /P /P 4 0 R /Pg 3 0 R /K 0 >>",
            "<< /S /Figure /P 4 0 R /Pg 3 0 R /K << /Type /MCR /MCID 0 /Stm 7 0 R /StmOwn 8 0 R >> >>",
            Form,
            "<< /Type /Annot /Subtype /Square /Rect [0 0 1 1] >>"));
        PdfStructureTreeRoot root = document.StructureTree!;
        var form = (CosStream)document.Resolve(new CosReference(7, 0));

        Assert.Equal("P", root.FindElement(document.Pages[0], 0)!.StructureType!.Value);
        Assert.Equal("Figure", root.FindElement(form, 0)!.StructureType!.Value);
        PdfMarkedContentReference reference = Assert.IsType<PdfMarkedContentReference>(Assert.Single(root.Elements[1].Children));
        Assert.Same(form, reference.ContentStream);
        Assert.Same(document.Resolve(new CosReference(8, 0)), reference.ContentStreamOwner);
        Assert.Same(document.Pages[0], reference.Page);
        Assert.NotNull(reference.Dictionary);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Without_the_k_walk_a_form_resolves_through_its_own_parent_tree_entry()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R] /ParentTree << /Nums [1 [5 0 R]] >>",
            "<< /S /Figure /P 4 0 R /K << /Type /MCR /MCID 0 /Stm 6 0 R >> >>",
            Form));
        var form = (CosStream)document.Resolve(new CosReference(6, 0));

        Assert.Equal("Figure", document.StructureTree!.GetMarkedContentElements(form)[0].StructureType!.Value);
        Assert.Empty(document.StructureTree!.GetMarkedContentElements(document.Pages[0]));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_mcr_page_overrides_the_element_page_and_an_objr_falls_back_to_the_annotation_page()
    {
        using PdfDocument document = PdfDocument.Open(new Document.TestPdf { Header = "%PDF-2.0", TrailerEntries = "/ID [<00> <00>]" }.Build(
            "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /StructParents 0 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /StructParents 1 >>",
            "<< /Type /StructTreeRoot /K [6 0 R] /ParentTree << /Nums [0 [6 0 R] 1 [null 6 0 R] 2 6 0 R] >> >>",
            "<< /S /P /P 5 0 R /Pg 3 0 R /K [0 << /Type /MCR /MCID 1 /Pg 4 0 R >> << /Type /OBJR /Obj 7 0 R >>] >>",
            "<< /Type /Annot /Subtype /Square /Rect [0 0 1 1] /P 4 0 R /StructParent 2 >>"));
        PdfStructureTreeRoot root = document.StructureTree!;
        IReadOnlyList<PdfStructureItem> items = root.Elements[0].GetContentItems();

        Assert.Same(document.Pages[0], Assert.IsType<PdfMarkedContentReference>(items[0]).Page);
        Assert.Same(document.Pages[1], Assert.IsType<PdfMarkedContentReference>(items[1]).Page);
        Assert.Same(document.Pages[0], Assert.IsType<PdfObjectReference>(items[2]).Page);
        Assert.Equal(new CosReference(7, 0), Assert.IsType<PdfObjectReference>(items[2]).ObjectReference);
        Assert.Same(root.Elements[0].Dictionary, root.FindElement(document.Pages[1], 1)!.Dictionary);
        Assert.Null(root.FindElement(document.Pages[1], 0));
        Assert.Same(root.Elements[0].Dictionary, root.FindElementForObject(new CosReference(7, 0))!.Dictionary);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_mcid_with_no_page_anywhere_takes_the_page_whose_parent_tree_entry_lists_its_element()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R] /ParentTree << /Nums [0 [null 5 0 R]] >>",
            "<< /S /P /P 4 0 R /K 1 >>"));

        PdfMarkedContentReference reference = Assert.IsType<PdfMarkedContentReference>(Assert.Single(document.StructureTree!.Elements[0].Children));

        Assert.Same(document.Pages[0], reference.Page);
        Assert.Equal("StructElemPageMissing", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Language_alternates_and_pronunciation_are_read_and_inherited_along_the_hierarchy()
    {
        using PdfDocument document = PdfDocument.Open(TaggedPdf.Build(
            "/K [5 0 R]",
            "<< /S /Sect /P 4 0 R /Lang (fr) /PhoneticAlphabet /x-sampa /K [6 0 R 7 0 R] >>",
            "<< /S /Span /P 5 0 R /T (Title) /Alt (alternate) /E (expansion) /ActualText (actual) /Phoneme (f@) /Ref [7 0 R] /AF [] >>",
            "<< /S /P /P 5 0 R /Lang (de) >>"));
        PdfStructureElement span = document.StructureTree!.Elements[1];
        PdfStructureElement section = document.StructureTree!.Elements[0];

        Assert.Equal("Title", span.Title);
        Assert.Equal("alternate", span.AlternateDescription);
        Assert.Equal("expansion", span.Expansion);
        Assert.Equal("actual", span.ActualText);
        Assert.Equal("f@", span.Phoneme);
        Assert.Null(span.Language);
        Assert.Equal("fr", span.EffectiveLanguage);
        Assert.Equal("x-sampa", span.PhoneticAlphabet);
        Assert.Equal("de", Assert.Single(span.References).EffectiveLanguage);
        Assert.Same(section, span.Parent);
        Assert.Empty(span.AssociatedFiles);
        Assert.Null(section.Page);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_property_list_is_typed_for_mcid_artifacts_and_span_entries()
    {
        var artifact = new PdfPropertyList(
            new CosName("Artifact"),
            (CosDictionary)CosObject.Parse("<< /Type /Pagination /Subtype /Header /BBox [0 700 612 792] /Attached [/Top /Left] >>"u8),
            document: null);
        var span = new PdfPropertyList(
            new CosName("Span"),
            (CosDictionary)CosObject.Parse("<< /MCID 4 /Lang (en-GB) /Alt (a) /ActualText (b) /E (c) >>"u8),
            document: null);
        var plain = new PdfPropertyList(new CosName("Tx"), properties: null, document: null);

        Assert.True(artifact.IsArtifact);
        Assert.Equal("Pagination", artifact.ArtifactType);
        Assert.Equal("Header", artifact.ArtifactSubtype);
        Assert.Equal(new PdfRectangle(0, 700, 612, 792), artifact.ArtifactBoundingBox);
        Assert.Equal(["Top", "Left"], artifact.ArtifactAttachments);
        Assert.Null(artifact.Mcid);

        Assert.False(span.IsArtifact);
        Assert.Equal(4, span.Mcid);
        Assert.Null(span.ArtifactType);
        Assert.Equal("en-GB", span.Language);
        Assert.Equal("a", span.AlternateDescription);
        Assert.Equal("b", span.ActualText);
        Assert.Equal("c", span.Expansion);

        Assert.Null(plain.Mcid);
        Assert.Empty(plain.ArtifactAttachments);
    }
}
