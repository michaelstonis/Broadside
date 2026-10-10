using Broadside.Annotations;
using Broadside.Diagnostics;

namespace Broadside.Tests.Document;

/// <summary>
/// The document model reads every dictionary entry with one leniency policy (ADR 0005): null is absent, a neighbouring type with one
/// obvious reading is repaired with a diagnostic, anything else is ignored with a diagnostic. ISO 32000-2 §7.3.9, §7.9.2.2.
/// </summary>
public sealed class EntryLeniencyTests
{
    [Fact]
    public void An_annotation_text_entry_written_as_a_name_reads_as_its_text_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage(
            "/MediaBox [0 0 612 792] /Annots [<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] /Contents /Hello >>]"));

        PdfAnnotation annotation = Assert.Single(document.Pages[0].Annotations);

        Assert.Equal("Hello", annotation.Contents);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "AnnotationValueInvalid");
    }

    [Fact]
    public void An_action_text_entry_written_as_a_number_reads_as_its_text_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage(
            "/MediaBox [0 0 612 792] /Annots [<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /A << /S /Movie /T 42 >> >>]"));

        var link = (PdfLinkAnnotation)Assert.Single(document.Pages[0].Annotations);
        PdfMovieAction action = Assert.IsType<PdfMovieAction>(link.Action);

        Assert.Equal("42", action.Title);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "ActionEntryInvalid");
    }

    [Fact]
    public void An_annotation_integer_entry_written_as_a_whole_real_reads_as_that_integer_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage(
            "/MediaBox [0 0 612 792] /Annots [<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] /StructParent 4.0 >>]"));

        PdfAnnotation annotation = Assert.Single(document.Pages[0].Annotations);

        Assert.Equal(4, annotation.StructParent);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "AnnotationValueInvalid");
    }

    [Fact]
    public void A_null_entry_reads_as_absent_in_launch_actions_and_embedded_targets()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage(
            "/MediaBox [0 0 612 792] /Annots [<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /A << /S /Launch /F (a.pdf) /Mac null >> >> "
            + "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /A << /S /GoToE /D [0 /Fit] /T << /R /C /N null >> >> >>]"));

        IReadOnlyList<PdfAnnotation> annotations = document.Pages[0].Annotations;
        PdfLaunchAction launch = Assert.IsType<PdfLaunchAction>(((PdfLinkAnnotation)annotations[0]).Action);
        PdfEmbeddedGoToAction embedded = Assert.IsType<PdfEmbeddedGoToAction>(((PdfLinkAnnotation)annotations[1]).Action);

        Assert.Null(launch.Mac);
        Assert.Null(embedded.Target!.EmbeddedFileName);
    }

    [Fact]
    public void A_text_entry_written_as_a_name_throws_in_strict_mode()
    {
        using PdfDocument document = PdfDocument.Open(
            new TestPdf().Build(
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /Annots [4 0 R] >>",
                "<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] /P 3 0 R /Contents /Hello /AP << /N 5 0 R >> >>",
                "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Length 0 >>\nstream\n\nendstream"),
            new PdfOptions().UseStrict());

        PdfAnnotation annotation = Assert.Single(document.Pages[0].Annotations);

        Assert.Equal("AnnotationValueInvalid", Assert.Throws<DiagnosticException>(() => annotation.Contents).Diagnostic.Code);
    }
}
