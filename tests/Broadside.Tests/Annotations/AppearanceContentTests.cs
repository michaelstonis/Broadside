using Broadside.Annotations;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Annotations;

/// <summary>Interpreting an annotation's appearance stream on its rectangle (ISO 32000-2 §12.5.5, Algorithm "Appearance streams").</summary>
public class AppearanceContentTests
{
    [Fact]
    public void An_appearance_runs_with_the_appearance_matrix_clipped_to_its_bounding_box()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));
        PdfAnnotation rotated = document.Pages[0].Annotations[0];
        var recorder = new RecordingProcessor(ContentEvents.Paths | ContentEvents.Clips);

        bool ran = rotated.ProcessAppearance(rotated.GetAppearance()!, recorder);

        Assert.True(ran);
        Assert.Equal(
            [
                "BeginRun Appearance",
                "Clip Rectangle NonZero #1<#0 M 0,0 L 100,0 L 100,50 L 0,50 Z ctm=[0 1 -1 0 150 100]",
                "Paint Fill NonZero M 0,0 L 100,0 L 100,50 L 0,50 Z ctm=[0 1 -1 0 150 100]",
                "EndRun",
            ],
            recorder.Lines);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Each_state_of_an_appearance_runs_on_request()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));
        PdfAnnotation square = document.Pages[0].Annotations[2];
        var recorder = new RecordingProcessor(ContentEvents.Operators);

        Assert.True(square.ProcessAppearance(square.GetAppearance(PdfAppearanceMode.Normal, new CosName("Off"))!, recorder, new ContentOptions()));

        Assert.Contains("Op 0.5 g", recorder.Lines);
    }

    [Fact]
    public void An_appearance_on_a_rectangle_without_area_is_not_run()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));
        PdfAnnotation circle = document.Pages[0].Annotations[1];
        PdfFormXObject form = circle.GetAppearance()!;
        circle.Dictionary[new CosName("Rect")] = new CosArray([new CosInteger(10), new CosInteger(10), new CosInteger(10), new CosInteger(10)]);
        var recorder = new RecordingProcessor();

        Assert.False(circle.ProcessAppearance(form, recorder));
        Assert.Empty(recorder.Lines);
    }

    [Fact]
    public void An_appearance_that_draws_itself_is_not_run_inside_itself()
    {
        const string content = "/Me Do 0 0 10 10 re f";
        byte[] file = new TestPdf().Build(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [4 0 R] >>",
            "<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] /AP << /N 5 0 R >> >>",
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources << /XObject << /Me 5 0 R >> >> /Length {content.Length} >>\nstream\n{content}\nendstream",
        ]);
        using PdfDocument document = PdfDocument.Open(file);
        PdfAnnotation annotation = document.Pages[0].Annotations[0];
        var recorder = new RecordingProcessor(ContentEvents.Paths | ContentEvents.Forms);

        Assert.True(annotation.ProcessAppearance(annotation.GetAppearance()!, recorder));

        Assert.Equal(1, recorder.Counts.GetValueOrDefault("PaintPath"));
        Assert.Equal(0, recorder.Counts.GetValueOrDefault("BeginForm"));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "ContentFormCycle");
    }
}
