using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Annotations;

/// <summary>
/// Marking areas with Redact annotations and applying them (issue #624, ISO 32000-2 §12.5.6.23, Table 195): the annotation's entries,
/// its removal once applied, the overlay drawn in its place, and the annotations and fields under it.
/// </summary>
public sealed class RedactionTests
{
    [Fact]
    public void AddRedactAnnotation_writes_the_quadrilaterals_the_fill_and_an_outline_appearance()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET");
        PdfPage page = document.Pages[0];
        var quad = new PdfQuadrilateral(new PathPoint(70, 715), new PathPoint(106, 715), new PathPoint(70, 690), new PathPoint(106, 690));

        PdfRedactAnnotation annotation = page.AddRedactAnnotation([quad]);

        Assert.Equal([quad], annotation.QuadPoints);
        Assert.Equal(new PdfDeviceColor(0), annotation.InteriorColor);
        Assert.Null(annotation.OverlayText);
        Assert.NotNull(annotation.GetAppearance());
        Assert.True(annotation.Rect.Left <= 70 && annotation.Rect.Right >= 106);
        Assert.Single(page.Annotations);
    }

    [Fact]
    public void AddRedactAnnotation_with_a_rectangle_writes_the_overlay_text_entries()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret) Tj ET");
        PdfPage page = document.Pages[0];

        PdfRedactAnnotation annotation = page.AddRedactAnnotation(
            new PdfRectangle(70, 690, 106, 715),
            new PdfRedactionOptions().WithFillColor(new PdfDeviceColor(1, 0, 0)).WithOverlayText("REDACTED").WithRepeatOverlayText(true));

        Assert.Empty(annotation.QuadPoints);
        Assert.Equal(new PdfDeviceColor(1, 0, 0), annotation.InteriorColor);
        Assert.Equal("REDACTED", annotation.OverlayText);
        Assert.True(annotation.RepeatsOverlayText);
        Assert.Equal(PdfTextJustification.Centered, annotation.Justification);
        Assert.NotNull(annotation.DefaultAppearance);
    }

    [Fact]
    public void PdfDocument_Redact_applies_every_annotation_removes_it_and_draws_the_fill_as_a_redaction_artifact()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET");
        PdfPage page = document.Pages[0];
        page.AddRedactAnnotation(new PdfRectangle(70, 690, 106, 715));

        PdfRedactionResult result = document.Redact();

        Assert.Equal(6, result.GlyphsRemoved);
        Assert.Equal(1, result.AnnotationsRemoved);
        Assert.Empty(page.Annotations);
        Assert.Equal("Public", page.ExtractText().Text.Trim());
        string content = RedactionSamples.Content(document, page);
        Assert.Contains("/Artifact << /Type /Pagination /Subtype /Redaction >> BDC", content, StringComparison.Ordinal);
        Assert.Contains("70 690 m", content, StringComparison.Ordinal);
    }

    [Fact]
    public void The_overlay_text_is_drawn_in_the_area_as_an_artifact()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET");
        PdfPage page = document.Pages[0];
        page.AddRedactAnnotation(new PdfRectangle(70, 690, 140, 715), new PdfRedactionOptions().WithOverlayText("WITHHELD"));

        document.Redact();

        Assert.Equal(string.Empty, page.ExtractText().Text.Trim());
        TextPage withArtifacts = page.ExtractText(new TextExtractionOptions { IncludeArtifacts = true });
        Assert.Contains("WITHHELD", withArtifacts.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_overlay_form_is_drawn_instead_of_the_fill()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET");
        PdfPage page = document.Pages[0];
        PdfRedactAnnotation annotation = page.AddRedactAnnotation(new PdfRectangle(70, 690, 106, 715));
        PdfFormXObject overlay = document.CreateFormXObject(new PdfRectangle(0, 0, 10, 10));
        using (PdfCanvas canvas = overlay.CreateCanvas())
        {
            canvas.Rectangle(0, 0, 10, 10);
            canvas.Fill();
        }

        annotation.Dictionary[new CosName("RO")] = overlay.Reference!;

        document.Redact();

        string content = RedactionSamples.Content(document, page);
        Assert.Contains(" Do", content, StringComparison.Ordinal);
        Assert.Contains("3.6 0 0 2.5 70 690 cm", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Annotations_under_the_area_are_removed_and_the_others_kept()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET");
        PdfPage page = document.Pages[0];
        page.AddTextAnnotation(new PdfRectangle(80, 695, 100, 710), "Secret note");
        page.AddTextAnnotation(new PdfRectangle(300, 300, 320, 320), "Other note");

        PdfRedactionResult result = page.Redact([RedactionSamples.Area(70, 690, 106, 715)]);

        Assert.Equal(1, result.AnnotationsRemoved);
        Assert.Equal("Other note", Assert.Single(page.Annotations).Contents);
    }

    [Fact]
    public void A_field_whose_widget_is_under_the_area_loses_its_value_and_keeps_its_widget()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [6 0 R] /DR << /Font << /Helv 5 0 R >> >> /DA (/Helv 10 Tf 0 g) >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 4 0 R /Annots [6 0 R] >>",
            ContentSamples.Stream(string.Empty),
            RedactionSamples.Helvetica,
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (ssn) /V (Secret) /Rect [100 100 200 120] /P 3 0 R /F 4 >>",
        ]));
        PdfPage page = document.Pages[0];

        PdfRedactionResult result = page.Redact([RedactionSamples.Area(90, 90, 150, 130)]);

        Assert.Equal(1, result.FieldsCleared);
        Assert.Null(((PdfTextField)document.AcroForm!.FindField("ssn")!).Value);
        Assert.IsType<PdfWidgetAnnotation>(Assert.Single(page.Annotations));
        byte[] saved = RedactionSamples.Save(document, result);
        RedactionSamples.AssertGone(saved, "Secret");
    }

    [Fact]
    public void The_page_thumbnail_is_removed()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret) Tj ET");
        PdfPage page = document.Pages[0];
        page.Dictionary[new CosName("Thumb")] = document.AddObject(new CosStream(new CosDictionary(), new byte[] { 1, 2, 3 }));

        page.Redact([RedactionSamples.Area(70, 690, 106, 715)]);

        Assert.False(page.Dictionary.ContainsKey(new CosName("Thumb")));
    }

    [Fact]
    public void A_signed_document_is_redacted_and_the_invalidated_signatures_are_reported()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [6 0 R] /SigFlags 3 >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R /Annots [6 0 R] >>",
            ContentSamples.Stream("BT /F1 12 Tf 72 700 Td (Secret) Tj ET"),
            RedactionSamples.Helvetica,
            "<< /Type /Annot /Subtype /Widget /FT /Sig /T (sig) /V 7 0 R /Rect [0 0 0 0] /P 3 0 R >>",
            "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange [0 0 0 0] /Contents <00> >>",
        ]));

        PdfRedactionResult result = document.Pages[0].Redact([RedactionSamples.Area(70, 690, 106, 715)]);

        Assert.Equal(6, result.GlyphsRemoved);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Kind == Diagnostics.RedactionDiagnostics.RedactionInvalidatesSignatures);
    }

    [Fact]
    public void Content_of_structure_elements_that_lost_content_loses_its_replacement_text()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R /StructParents 0 >>",
            ContentSamples.Stream("/Span <</MCID 0>> BDC BT /F1 12 Tf 72 700 Td (Secret) Tj ET EMC /Span <</MCID 1>> BDC BT /F1 12 Tf 72 600 Td (Other) Tj ET EMC"),
            RedactionSamples.Helvetica,
            "<< /Type /StructTreeRoot /K [7 0 R 8 0 R] /ParentTree << /Nums [0 [7 0 R 8 0 R]] >> >>",
            "<< /Type /StructElem /S /Span /P 6 0 R /Pg 3 0 R /K 0 /ActualText (Secret) /Alt (Secret) >>",
            "<< /Type /StructElem /S /Span /P 6 0 R /Pg 3 0 R /K 1 /ActualText (Other) >>",
        ]));

        PdfRedactionResult result = document.Pages[0].Redact([RedactionSamples.Area(70, 690, 106, 715)]);

        var removed = (CosDictionary)document.Resolve(new CosReference(7, 0));
        var kept = (CosDictionary)document.Resolve(new CosReference(8, 0));
        Assert.False(removed.ContainsKey(new CosName("ActualText")));
        Assert.False(removed.ContainsKey(new CosName("Alt")));
        Assert.True(kept.ContainsKey(new CosName("ActualText")));
        RedactionSamples.AssertGone(RedactionSamples.Save(document, result), "Secret");
    }

    [Fact]
    public void Strict_mode_throws_for_a_fallback_and_leaves_the_page_unchanged()
    {
        // The image fills 72 to 172 by 600 to 700; the area covers its top right part.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-smask.pdf"), new PdfOptions().UseStrict());
        PdfPage page = document.Pages[0];
        string before = RedactionSamples.Content(document, page);

        Assert.Throws<Diagnostics.DiagnosticException>(() => page.Redact([RedactionSamples.Area(150, 650, 300, 800)]));

        Assert.Equal(before, RedactionSamples.Content(document, page));
    }
}
