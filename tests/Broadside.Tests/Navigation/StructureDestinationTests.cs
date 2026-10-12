using Broadside.Annotations;
using Broadside.Tests.Document;

namespace Broadside.Tests.Navigation;

/// <summary>
/// Structure destinations: the page comes from the structure element's first content item, depth first, else the first page; a
/// named destination's dictionary value may carry one in <c>SD</c>. ISO 32000-2 §12.3.2.3 and §12.3.2.4.
/// </summary>
public sealed class StructureDestinationTests
{
    /// <summary>Two pages (objects 3 and 4); a structure tree rooted at 5 with the given elements from object 6 on; catalog Dests as given.</summary>
    private static byte[] Tagged(string dests, params string[] elements) => new TestPdf().Build(
    [
        $"<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R /MarkInfo << /Marked true >> /Dests << {dests} >> >>",
        "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
        "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /Annots [20 0 R] >>",
        "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
        "<< /Type /StructTreeRoot /K [6 0 R] >>",
        .. elements,
        .. Enumerable.Repeat("null", 14 - elements.Length),
        "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /P 3 0 R /Dest (named) >>",
    ]);

    [Fact]
    public void The_page_of_a_structure_destination_is_the_page_of_its_first_marked_content()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(
            "/a [7 0 R /Fit]",
            "<< /Type /StructElem /S /Document /P 5 0 R /K [7 0 R] >>",
            "<< /Type /StructElem /S /P /P 6 0 R /Pg 4 0 R /K 0 >>"));

        PdfExplicitDestination destination = document.GetNamedDestination("a")!;

        Assert.Equal((PdfDestinationTarget.StructureElement, 1), (destination.TargetKind, destination.PageIndex));
        Assert.Same(document.Pages[1], destination.Page);
    }

    [Fact]
    public void An_element_without_content_is_skipped_for_the_next_kid_s_object_reference()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(
            "/a [6 0 R /FitH 500]",
            "<< /Type /StructElem /S /Document /P 5 0 R /K [7 0 R 8 0 R] >>",
            "<< /Type /StructElem /S /Div /P 6 0 R >>",
            "<< /Type /StructElem /S /Figure /P 6 0 R /K << /Type /OBJR /Obj 9 0 R /Pg 4 0 R >> >>",
            "<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /Length 0 >>\nstream\n\nendstream"));

        PdfExplicitDestination destination = document.GetNamedDestination("a")!;

        Assert.Equal((1, 500.0), (destination.PageIndex, destination.Top));
    }

    [Fact]
    public void A_structure_destination_without_content_shows_the_first_page()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(
            "/a [7 0 R /Fit]",
            "<< /Type /StructElem /S /Document /P 5 0 R /K [7 0 R] >>",
            "<< /Type /StructElem /S /P /P 6 0 R >>"));

        PdfExplicitDestination destination = document.GetNamedDestination("a")!;

        Assert.Equal(0, destination.PageIndex);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_named_destination_dictionary_exposes_its_structure_destination()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(
            "/named << /D [3 0 R /Fit] /SD [7 0 R /XYZ 0 700 0] >>",
            "<< /Type /StructElem /S /Document /P 5 0 R /K [7 0 R] >>",
            "<< /Type /StructElem /S /P /P 6 0 R /Pg 4 0 R /K 0 >>"));
        var link = (PdfLinkAnnotation)document.Pages[0].Annotations[0];
        PdfNamedDestination named = Assert.IsType<PdfNamedDestination>(link.Destination);

        PdfExplicitDestination? structure = named.ResolveStructureDestination();

        Assert.Equal(0, named.Resolve()!.PageIndex);
        Assert.NotNull(structure);
        Assert.Equal((PdfDestinationTarget.StructureElement, 1, 700.0), (structure.TargetKind, structure.PageIndex, structure.Top));
    }

    [Fact]
    public void A_named_destination_array_has_no_structure_destination()
    {
        using PdfDocument document = PdfDocument.Open(Tagged(
            "/named [3 0 R /Fit]",
            "<< /Type /StructElem /S /Document /P 5 0 R >>"));
        var link = (PdfLinkAnnotation)document.Pages[0].Annotations[0];

        Assert.Null(Assert.IsType<PdfNamedDestination>(link.Destination).ResolveStructureDestination());
    }
}
