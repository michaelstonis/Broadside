using System.Text;
using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Tests.Forms;
using Broadside.TestSupport;

namespace Broadside.Tests.Annotations;

/// <summary>
/// Exporting and importing markup annotations as XFDF (Adobe XFDF 3.0, ISO 19444-1) and FDF (ISO 32000-2 §12.7.8.3.4): subtypes,
/// replies, pop-ups and replacement by name.
/// </summary>
public sealed class XfdfAnnotationTests
{
    private static readonly PdfFormDataOptions XfdfAnnotations = new PdfFormDataOptions().WithFields(false).WithAnnotations();
    private static readonly PdfFormDataOptions FdfAnnotations = new PdfFormDataOptions().WithFormat(PdfFormDataFormat.Fdf).WithFields(false).WithAnnotations();

    [Fact]
    public void A_highlight_round_trips_with_its_quadrilaterals_name_color_author_and_contents()
    {
        using PdfDocument source = PdfDocument.Create();
        source.Pages[0].AddHighlightAnnotation(
            [new PdfQuadrilateral(new(72, 760), new(272, 760), new(72, 740), new(272, 740))],
            new PdfAnnotationOptions { Name = "h1", Title = "Ada", Subject = "Review", Contents = "Check this", Color = new PdfDeviceColor(0, 1, 0) });

        byte[] file = FormDataFixture.Export(source, XfdfAnnotations);
        using PdfDocument target = PdfDocument.Create();
        PdfFormDataImport import = FormDataFixture.Import(target, file);

        Assert.Contains("<highlight page=\"0\"", Encoding.UTF8.GetString(file), StringComparison.Ordinal);
        PdfHighlightAnnotation highlight = Assert.IsType<PdfHighlightAnnotation>(Assert.Single(import.AnnotationsAdded));
        Assert.Same(highlight, Assert.Single(target.Pages[0].Annotations));
        Assert.Equal(("h1", "Ada", "Review", "Check this"), (highlight.Name, highlight.Title, highlight.Subject, highlight.Contents));
        Assert.Equal(new PdfDeviceColor(0, 1, 0), highlight.Color);
        PdfQuadrilateral quad = Assert.Single(highlight.QuadPoints);
        Assert.Equal((72d, 760d, 272d, 740d), (quad.Point1.X, quad.Point1.Y, quad.Point4.X, quad.Point4.Y));
        Assert.NotNull(highlight.GetAppearance());
        Assert.Same(target.Pages[0].Dictionary, highlight.PageObject);
        Assert.Empty(import.Diagnostics);
    }

    [Fact]
    public void An_ink_annotation_round_trips_with_its_strokes()
    {
        using PdfDocument source = PdfDocument.Create();
        source.Pages[0].AddInkAnnotation([[new(100, 100), new(120, 130), new(140, 100)], [new(200, 200), new(210, 220)]], new PdfAnnotationOptions { Name = "ink" });

        using PdfDocument target = PdfDocument.Create();
        PdfFormDataImport import = FormDataFixture.Import(target, FormDataFixture.Export(source, XfdfAnnotations));

        PdfInkAnnotation ink = Assert.IsType<PdfInkAnnotation>(Assert.Single(import.AnnotationsAdded));
        Assert.Equal([3, 2], ink.InkList.Select(stroke => stroke.Count));
        Assert.Equal((120d, 130d), (ink.InkList[0][1].X, ink.InkList[0][1].Y));
        Assert.NotNull(ink.GetAppearance());
    }

    [Fact]
    public void A_text_annotation_and_its_reply_round_trip_with_the_reply_naming_its_parent()
    {
        using PdfDocument source = PdfDocument.Create();
        PdfTextAnnotation parent = source.Pages[0].AddTextAnnotation(new PdfRectangle(300, 700, 320, 720), "Question?", new PdfAnnotationOptions { Name = "parent", Title = "Ada" });
        PdfTextAnnotation reply = source.Pages[0].AddTextAnnotation(new PdfRectangle(300, 700, 320, 720), "Answer.", new PdfAnnotationOptions { Name = "reply", Title = "Bob" });
        reply.Dictionary[AnnotationNames.IRT] = parent.Reference!;

        foreach (PdfFormDataOptions options in new[] { XfdfAnnotations, FdfAnnotations })
        {
            using PdfDocument target = PdfDocument.Create();
            PdfFormDataImport import = FormDataFixture.Import(target, FormDataFixture.Export(source, options));

            Assert.Equal(["parent", "reply"], import.AnnotationsAdded.Select(annotation => annotation.Name));
            var imported = (PdfTextAnnotation)import.AnnotationsAdded[1];
            Assert.Same(import.AnnotationsAdded[0].Dictionary, imported.InReplyTo!.Dictionary);
            Assert.Equal(("Answer.", "Bob"), (imported.Contents, imported.Title));
            Assert.Empty(import.Diagnostics);
        }
    }

    [Fact]
    public void An_annotation_whose_name_matches_one_on_its_page_replaces_it_in_place()
    {
        using PdfDocument target = PdfDocument.Create();
        PdfTextAnnotation existing = target.Pages[0].AddTextAnnotation(new PdfRectangle(300, 700, 320, 720), "Old", new PdfAnnotationOptions { Name = "n1" });
        target.Pages[0].AddTextAnnotation(new PdfRectangle(100, 700, 120, 720), "Other", new PdfAnnotationOptions { Name = "n2" });
        using PdfDocument source = PdfDocument.Create();
        source.Pages[0].AddTextAnnotation(new PdfRectangle(310, 710, 330, 730), "New", new PdfAnnotationOptions { Name = "n1" });

        PdfFormDataImport import = FormDataFixture.Import(target, FormDataFixture.Export(source, XfdfAnnotations));

        PdfAnnotation replaced = Assert.Single(import.AnnotationsReplaced);
        Assert.Empty(import.AnnotationsAdded);
        Assert.Same(existing.Dictionary, replaced.Dictionary);
        Assert.Equal(existing.Reference, replaced.Reference);
        Assert.Equal(["New", "Other"], target.Pages[0].Annotations.Select(annotation => annotation.Contents));
        Assert.Equal(new PdfRectangle(310, 710, 330, 730), replaced.Rect);
    }

    [Fact]
    public void Fdf_annotations_copy_with_their_pop_up_and_reply_remapped_inside_the_file_and_back()
    {
        using PdfDocument target = PdfDocument.Create();

        PdfFormDataImport import = FormDataFixture.Import(target, Corpus.Bytes("form-data.fdf"));

        Assert.Equal(["Text", "Popup", "Highlight"], import.AnnotationsAdded.Select(annotation => annotation.Kind.ToString()));
        var note = (PdfTextAnnotation)import.AnnotationsAdded[0];
        var highlight = (PdfHighlightAnnotation)import.AnnotationsAdded[2];
        Assert.Same(import.AnnotationsAdded[1].Dictionary, note.Popup!.Dictionary);
        Assert.Same(note.Dictionary, ((PdfPopupAnnotation)import.AnnotationsAdded[1]).Parent!.Dictionary);
        Assert.Same(note.Dictionary, highlight.InReplyTo!.Dictionary);
        Assert.Equal(3, target.Pages[0].Annotations.Count);
        Assert.All(import.AnnotationsAdded, annotation => Assert.False(annotation.Dictionary.ContainsKey(new CosName("Page"))));

        CosDictionary fdf = FdfExportTests.ReadFdfDictionary(FormDataFixture.Export(target, FdfAnnotations));
        Assert.Equal(3, ((CosArray)fdf[new CosName("Annots")]).Count);
    }

    [Fact]
    public void An_annotation_on_a_page_the_document_lacks_or_of_a_subtype_fdf_cannot_carry_is_skipped()
    {
        using PdfDocument target = PdfDocument.Create();
        string file = FdfImportTests.Fdf("<< /FDF << /Annots [ << /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Page 3 >> << /Type /Annot /Subtype /Link /Rect [0 0 10 10] /Page 0 >> << /Type /Annot /Subtype /Square /Rect [0 0 10 10] >> ] >> >>");

        PdfFormDataImport import = FormDataFixture.Import(target, file);

        Assert.Empty(import.AnnotationsAdded);
        // Each skip is recorded once per code (the diagnostics of an import are deduplicated like a document's).
        Assert.Equal("FormDataAnnotationSkipped", Assert.Single(import.Diagnostics).Code);
        Assert.Empty(target.Pages[0].Annotations);
    }

    [Fact]
    public void Annotations_are_not_imported_when_the_options_leave_them_out()
    {
        using PdfDocument target = PdfDocument.Create();

        PdfFormDataImport import = FormDataFixture.Import(target, Corpus.Bytes("form-data.xfdf"), new PdfFormDataOptions());

        Assert.Empty(import.AnnotationsAdded);
        Assert.Empty(target.Pages[0].Annotations);
    }
}
