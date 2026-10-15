using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Tests.Writing;

/// <summary>
/// Objects copied from another document (<see cref="PdfObjectCopier"/>) carry the features they use, and the target's next save
/// raises its version to them, or refuses in strict mode, as for any feature written through the document's own APIs.
/// ISO 32000-2 §7.5.2, §7.12; ADR 0003; issue #290.
/// </summary>
public sealed class CopiedFeatureVersionTests
{
    private static PdfSaveOptions Options => new PdfSaveOptions().WithUpdateModificationDate(false);

    [Fact]
    public void A_page_with_associated_files_copied_from_pdf_2_0_raises_a_pdf_1_4_target_to_2_0()
    {
        using PdfDocument source = SourceWithAssociatedFile();
        using PdfDocument target = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4"));

        target.Pages.Add(new PdfObjectCopier(source, target).CopyPage(source.Pages[0]));
        byte[] saved = Save(target, Options);

        Assert.Equal("2.0", HeaderVersion(saved));
        Assert.Contains(
            "associated files (AF, AFRelationship) (ISO 32000-2 §14.13) needs PDF 2.0",
            Assert.Single(target.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message,
            StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        Assert.Equal("a.txt", Assert.Single(reopened.Pages[1].AssociatedFiles).FileName);
    }

    [Fact]
    public void Strict_mode_refuses_to_save_a_pdf_1_4_target_holding_a_copied_associated_file_before_writing()
    {
        using PdfDocument source = SourceWithAssociatedFile();
        using PdfDocument target = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4"), new PdfOptions().UseStrict());
        target.Pages.Add(new PdfObjectCopier(source, target).CopyPage(source.Pages[0]));
        using var output = new MemoryStream();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => target.Save(output, Options));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
        Assert.Contains("associated files (AF, AFRelationship)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("needs PDF 2.0", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void An_incremental_update_of_a_pdf_1_4_target_holding_a_copied_associated_file_raises_the_catalog_version()
    {
        using PdfDocument source = SourceWithAssociatedFile();
        byte[] original = Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4");
        using PdfDocument target = PdfDocument.Open(original);
        target.Pages.Add(new PdfObjectCopier(source, target).CopyPage(source.Pages[0]));

        byte[] saved = Save(target, Options.WithIncrementalUpdate(true));

        Assert.Equal(original, saved[..original.Length]);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        Assert.Equal("a.txt", Assert.Single(reopened.Pages[1].AssociatedFiles).FileName);
    }

    [Fact]
    public void A_structure_element_in_a_pdf_2_0_namespace_copied_into_a_pdf_1_7_target_raises_it_to_2_0()
    {
        using PdfDocument source = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-2.0"));
        PdfStructureElement element = source.CreateStructureTree().AddElement(new PdfStructureType("Document", PdfStructureNamespace.Pdf20));
        using PdfDocument target = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]"));

        new PdfObjectCopier(source, target).Copy(element.Reference!);
        byte[] saved = Save(target, Options);

        Assert.Equal("2.0", HeaderVersion(saved));
        Assert.Contains(
            "structure namespaces or other PDF 2.0 structure entries",
            Assert.Single(target.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Copying_between_pdf_2_0_documents_saves_without_a_raise()
    {
        using PdfDocument source = SourceWithAssociatedFile();
        using PdfDocument target = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-2.0"));

        target.Pages.Add(new PdfObjectCopier(source, target).CopyPage(source.Pages[0]));
        byte[] saved = Save(target, Options);

        Assert.Equal("2.0", HeaderVersion(saved));
        Assert.DoesNotContain(target.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal("a.txt", Assert.Single(reopened.Pages[1].AssociatedFiles).FileName);
    }

    [Fact]
    public void A_page_using_nothing_later_than_pdf_1_4_copied_from_pdf_2_0_keeps_a_pdf_1_4_target()
    {
        using PdfDocument source = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792] /Rotate 90", header: "%PDF-2.0"));
        using PdfDocument target = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4"), new PdfOptions().UseStrict());

        target.Pages.Add(new PdfObjectCopier(source, target).CopyPage(source.Pages[0]));
        byte[] saved = Save(target, Options);

        Assert.Equal("1.4", HeaderVersion(saved));
        Assert.Empty(target.Diagnostics);
    }

    /// <summary>One object per feature a copy can carry, the version the feature needs, and the feature's name in the diagnostic.</summary>
    public static TheoryData<string, string, string> CarriedObjects => new()
    {
        { "<< /Filter /FlateDecode >> stream", "1.2", "FlateDecode compression (ISO 32000-2 §7.4.4) needs PDF 1.2" },
        { "<< /Filter [/ASCIIHexDecode /FlateDecode] >> stream", "1.2", "FlateDecode compression (ISO 32000-2 §7.4.4) needs PDF 1.2" },
        { "[null /FitBH 700]", "1.1", "a FitB, FitBH or FitBV destination (ISO 32000-2 §12.3.2.2, Table 149) needs PDF 1.1" },
        { "<< /Type /EmbeddedFile >> stream", "1.3", "an embedded file stream (ISO 32000-2 §7.11.4) needs PDF 1.3" },
        { "<< /Type /StructElem /S /P >>", "1.3", "a structure tree and its entries (StructTreeRoot, MarkInfo, Lang, ActualText, E) (ISO 32000-2 §14.7.2, Tables 353 to 355) needs PDF 1.3" },
        { "<< /S /Span /P << >> /ActualText (x) >>", "1.4", "a structure tree and its entries (StructTreeRoot, MarkInfo, Lang, ActualText, E) (ISO 32000-2 §14.7.2, Tables 353 to 355) needs PDF 1.4" },
        { "<< /Type /StructElem /S /Span /E (x) >>", "1.5", "a structure tree and its entries (StructTreeRoot, MarkInfo, Lang, ActualText, E) (ISO 32000-2 §14.7.2, Tables 353 to 355) needs PDF 1.5" },
        { "<< /Title (Red) /Parent << >> /C [1 0 0] >>", "1.4", "an outline item's colour or style flags (C, F) (ISO 32000-2 §12.3.3, Table 151) needs PDF 1.4" },
        { "<< /Title (Bold) /Parent << >> /F 2 >>", "1.4", "an outline item's colour or style flags (C, F) (ISO 32000-2 §12.3.3, Table 151) needs PDF 1.4" },
        { "<< /Type /Filespec /F (a.txt) /Desc (A file) >>", "1.6", "a file specification's description (Desc) (ISO 32000-2 §7.11.3, Table 43) needs PDF 1.6" },
        { "<< /Type /Filespec /F (a.txt) /UF (a.txt) >>", "1.7", "a file specification's Unicode file name (UF) (ISO 32000-2 §7.11.3, Table 43) needs PDF 1.7" },
        { "<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /AF [] >> stream", "2.0", "associated files (AF, AFRelationship) (ISO 32000-2 §14.13) needs PDF 2.0" },
        { "<< /Type /Filespec /F (a.txt) /AFRelationship /Source >>", "2.0", "associated files (AF, AFRelationship) (ISO 32000-2 §14.13) needs PDF 2.0" },
        { "<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>", "2.0", "structure namespaces or other PDF 2.0 structure entries" },
        { "<< /S /Span /P << >> /Phoneme (x) >>", "2.0", "structure namespaces or other PDF 2.0 structure entries" },
        { "<< /Type /StructTreeRoot /Namespaces [] >>", "2.0", "structure namespaces or other PDF 2.0 structure entries" },
        { "<< /Type /Group /S /Transparency >>", "1.4", "a transparency group (Group) (ISO 32000-2 §11.6.6; Tables 31 and 93) needs PDF 1.4" },
        { "<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /Group << /S /Transparency /I true >> >> stream", "1.4", "a transparency group (Group) (ISO 32000-2 §11.6.6; Tables 31 and 93) needs PDF 1.4" },
        { "<< /Type /ExtGState /SMask /None >>", "1.4", "a soft mask (SMask) (ISO 32000-2 §11.6.5; Tables 57 and 87) needs PDF 1.4" },
        { "<< /Type /Mask /S /Luminosity /BC [0] >>", "1.4", "a soft mask (SMask) (ISO 32000-2 §11.6.5; Tables 57 and 87) needs PDF 1.4" },
        { "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /SMask 3 0 R >> stream", "1.4", "a soft mask (SMask) (ISO 32000-2 §11.6.5; Tables 57 and 87) needs PDF 1.4" },
        { "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask [0 0] >> stream", "1.3", "an image's Mask entry (explicit or colour key masking) (ISO 32000-2 §8.9.6.3 and §8.9.6.4; Table 87) needs PDF 1.3" },
        { "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 16 >> stream", "1.5", "16 bits per image component (ISO 32000-2 §8.9.5.1, Table 87) needs PDF 1.5" },
        { "<< /Type /ExtGState /CA 0.5 >>", "1.4", "transparency parameters (CA, ca, BM, AIS, TK) (ISO 32000-2 §11.6.3 and §11.6.4.4, Table 57) needs PDF 1.4" },
        { "<< /BM /Multiply /AIS true >>", "1.4", "transparency parameters (CA, ca, BM, AIS, TK) (ISO 32000-2 §11.6.3 and §11.6.4.4, Table 57) needs PDF 1.4" },
        { "<< /Filter /JBIG2Decode >> stream", "1.4", "JBIG2Decode compression (ISO 32000-2 §7.4.7, Table 6) needs PDF 1.4" },
        { "<< /Filter [/ASCII85Decode /JPXDecode] >> stream", "1.5", "JPXDecode compression (ISO 32000-2 §7.4.9, Table 6) needs PDF 1.5" },
        { "<< /Type /OCG /Name (Layer) >>", "1.5", "optional content (OCG, OCMD, OC, OCProperties) (ISO 32000-2 §8.11; Table 29) needs PDF 1.5" },
        { "<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /OC << /Type /OCMD /OCGs [] >> >> stream", "1.5", "optional content (OCG, OCMD, OC, OCProperties) (ISO 32000-2 §8.11; Table 29) needs PDF 1.5" },
        { "<< /Type /Annot /Subtype /Square /Rect [0 0 1 1] /CA 0.5 >>", "1.4", "transparency parameters (CA, ca, BM, AIS, TK) (ISO 32000-2 §11.6.3 and §11.6.4.4, Table 57) needs PDF 1.4" },
        { "<< /Type /Annot /Subtype /Link /Rect [0 0 1 1] /CA 0.5 >>", "2.0", "PDF 2.0 annotation entries (ca, BM, Lang, CA of a non-markup annotation, Projection and RichMedia types) (ISO 32000-2 §12.5.2, Table 166; §12.5.6.1, Table 171) needs PDF 2.0" },
        { "<< /Type /Annot /Subtype /Square /Rect [0 0 1 1] /ca 0.5 >>", "2.0", "PDF 2.0 annotation entries" },
        { "<< /Subtype /Highlight /Rect [0 0 1 1] /BM /Multiply >>", "2.0", "PDF 2.0 annotation entries" },
        { "<< /Subtype /Text /Rect [0 0 1 1] /Lang (en-GB) >>", "2.0", "PDF 2.0 annotation entries" },
        { "<< /Type /Annot /Subtype /Projection /Rect [0 0 1 1] >>", "2.0", "PDF 2.0 annotation entries" },
        { "<< /Type /Annot /Subtype /RichMedia /Rect [0 0 1 1] >>", "2.0", "PDF 2.0 annotation entries" },
        { "<< /FunctionType 0 /Domain [0 1] /Range [0 1] /Size [2] /BitsPerSample 8 >> stream", "1.2", "a sampled function (FunctionType 0) (ISO 32000-2 §7.10.2) needs PDF 1.2" },
        { "<< /FunctionType 2 /Domain [0 1] /N 1 >>", "1.3", "an exponential, stitching or PostScript calculator function (FunctionType 2, 3 or 4)" },
        { "<< /FunctionType 3 /Domain [0 1] /Functions [] /Bounds [] /Encode [] >>", "1.3", "an exponential, stitching or PostScript calculator function (FunctionType 2, 3 or 4)" },
        { "<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 1 0] /Function 4 0 R >>", "1.3", "a shading (sh, Shading resources, shading patterns)" },
        { "<< /ShadingType 4 /ColorSpace /DeviceGray /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 1 0 1 0 1] >> stream", "1.3", "a shading (sh, Shading resources, shading patterns)" },
        { "<< /PatternType 2 /Shading 4 0 R >>", "1.3", "a shading (sh, Shading resources, shading patterns)" },
        { "<< /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 1 1] /XStep 1 /YStep 1 /Resources << >> >> stream", "1.2", "a Pattern colour space or tiling pattern" },
        { "[/Pattern /DeviceRGB]", "1.2", "a Pattern colour space or tiling pattern" },
    };

    [Theory]
    [MemberData(nameof(CarriedObjects))]
    public void Each_feature_a_copied_object_carries_raises_a_pdf_1_0_target_to_its_minimum(string body, string version, string named)
    {
        using PdfDocument source = PdfDocument.Open(new Document.TestPdf { Header = "%PDF-2.0" }.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [] /Count 0 >>",
            body.EndsWith("stream", StringComparison.Ordinal) ? body + "\nx\nendstream" : body));
        using PdfDocument target = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.0"));

        new PdfObjectCopier(source, target).Copy(new CosReference(3, 0));
        byte[] saved = Save(target, Options.WithStreamCompression(PdfStreamCompression.None));

        Assert.Equal(version, HeaderVersion(saved));
        Assert.Contains(named, Assert.Single(target.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CarriedObjects))]
    public void A_copied_object_into_a_target_already_at_its_features_version_saves_without_a_raise(string body, string version, string named)
    {
        _ = named;
        using PdfDocument source = PdfDocument.Open(new Document.TestPdf { Header = "%PDF-2.0" }.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [] /Count 0 >>",
            body.EndsWith("stream", StringComparison.Ordinal) ? body + "\nx\nendstream" : body));
        using PdfDocument target = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-" + version));

        new PdfObjectCopier(source, target).Copy(new CosReference(3, 0));
        byte[] saved = Save(target, Options.WithStreamCompression(PdfStreamCompression.None));

        Assert.Equal(version, HeaderVersion(saved));
        Assert.DoesNotContain(target.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised");
    }

    /// <summary>A PDF 2.0 document whose page has an associated file (§14.13): an <c>AF</c> array and an <c>AFRelationship</c>.</summary>
    private static PdfDocument SourceWithAssociatedFile()
    {
        PdfDocument source = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-2.0"));
        PdfFileSpecification file = source.CreateEmbeddedFile(
            "a.txt", "attached"u8, new PdfEmbeddedFileOptions().WithRelationship(PdfFileRelationship.Data));
        source.AddAssociatedFile(source.Pages[0].Dictionary, file);
        return source;
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }

    /// <summary>The version the header states, read from the bytes (§7.5.2).</summary>
    private static string HeaderVersion(byte[] file)
    {
        string text = Encoding.Latin1.GetString(file, 0, 16);
        Assert.StartsWith("%PDF-", text, StringComparison.Ordinal);
        return text[5..8];
    }
}
