using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Files;

/// <summary>Metadata streams at the object-level locations of ISO 32000-2 §14.3.2 and PDF 2.0 Application Note 003.</summary>
public sealed class ObjectMetadataTests
{
    private static string Title(PdfObjectMetadata metadata) => metadata.Packet!.Title!;

    [Fact]
    public void Enumeration_finds_each_metadata_stream_with_its_location()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("object-metadata.pdf"));

        var found = document.EnumerateObjectMetadata().Select(metadata => (metadata.Location, Title(metadata))).ToList();

        Assert.Equal(
            [
                (PdfMetadataLocation.Document, "Document"),
                (PdfMetadataLocation.OptionalContentGroup, "Optional content"),
                (PdfMetadataLocation.EmbeddedFile, "Embedded file"),
                (PdfMetadataLocation.Page, "Page"),
                (PdfMetadataLocation.ImageXObject, "Image"),
                (PdfMetadataLocation.FormXObject, "Form"),
                (PdfMetadataLocation.IccProfile, "ICC profile"),
                (PdfMetadataLocation.FontProgram, "Font program"),
                (PdfMetadataLocation.TilingPattern, "Tiling pattern"),
                (PdfMetadataLocation.Shading, "Shading"),
                (PdfMetadataLocation.MarkedContent, "Marked content"),
                (PdfMetadataLocation.Annotation, "Annotation"),
            ],
            found);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Each_metadata_stream_knows_its_owner()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("object-metadata.pdf"));

        PdfObjectMetadata font = document.EnumerateObjectMetadata().Single(metadata => metadata.Location == PdfMetadataLocation.FontProgram);

        Assert.Equal(new CosReference(13, 0), font.OwnerReference);
        Assert.Equal(0, font.PageIndex);
        Assert.IsType<CosStream>(font.Owner);
        Assert.Equal(new CosReference(45, 0), font.Reference);
    }

    [Fact]
    public void Structure_elements_threads_and_document_parts_carry_metadata_too()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 4 0 R /Threads [6 0 R] /DPartRoot 7 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            "<< /Type /StructTreeRoot /K [5 0 R] >>",
            "<< /Type /StructElem /S /Document /P 4 0 R /Metadata 9 0 R >>",
            "<< /Type /Thread /Metadata 9 0 R >>",
            "<< /Type /DPartRoot /DPartRootNode 8 0 R >>",
            "<< /Type /DPart /Parent 7 0 R /Metadata 9 0 R >>",
            "<< /Type /Metadata /Subtype /XML /Length 3 >>\nstream\n<x>\nendstream");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(
            [PdfMetadataLocation.Thread, PdfMetadataLocation.StructureElement, PdfMetadataLocation.DocumentPart],
            document.EnumerateObjectMetadata().Select(metadata => metadata.Location));
    }

    [Fact]
    public void A_deep_scan_finds_metadata_anywhere_else()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            "<< /Type /Custom /Metadata 5 0 R >>",
            "<< /Type /Metadata /Subtype /XML /Length 3 >>\nstream\n<x>\nendstream");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Empty(document.EnumerateObjectMetadata());
        PdfObjectMetadata found = Assert.Single(document.EnumerateObjectMetadata(deep: true));
        Assert.Equal((PdfMetadataLocation.Other, new CosReference(4, 0)), (found.Location, found.OwnerReference));
    }

    [Fact]
    public void A_metadata_stream_without_Type_Metadata_and_Subtype_XML_is_reported()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /Metadata 4 0 R >>",
            "<< /Length 3 >>\nstream\n<x>\nendstream"));

        Assert.Single(document.EnumerateObjectMetadata());
        Assert.Equal(["MetadataStreamInvalid"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }
}
