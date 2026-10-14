using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Form XObjects, transparency groups and soft masks written through <see cref="PdfCanvas"/>: what was written re-reads, after a save,
/// through the form events and the graphics state. ISO 32000-2 §8.10, §11.6.5 (soft masks), §11.6.6 (transparency group XObjects).
/// </summary>
public sealed class CanvasGroupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_written_group_re_reads_with_its_flags_through_the_form_events(bool isolated, bool knockout)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 100, 100));
        PdfTransparencyGroup group = form.CreateTransparencyGroup();
        group.IsIsolated = isolated;
        group.IsKnockout = knockout;
        DrawSquare(form);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawForm(form);
        }

        FormRecord recorded = Assert.Single(ReplayForms(document));

        Assert.True(recorded.IsTransparencyGroup);
        Assert.Equal(isolated, recorded.IsIsolated);
        Assert.Equal(knockout, recorded.IsKnockout);
        Assert.Null(recorded.ColorSpace);
    }

    private static void DrawSquare(PdfFormXObject form)
    {
        using PdfCanvas canvas = form.CreateCanvas();
        canvas.SetFillColor(PdfColor.FromRgb(1, 0, 0));
        canvas.Rectangle(10, 10, 50, 50);
        canvas.Fill();
    }

    /// <summary>Saves, reopens and records every form event of one page.</summary>
    private static List<FormRecord> ReplayForms(PdfDocument document, int page = 0)
    {
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        var recorder = new FormRecorder();
        reopened.Pages[page].ProcessContent(recorder);
        Assert.Empty(reopened.Diagnostics);
        return recorder.Forms;
    }

    private sealed record FormRecord(
        CosReference? Reference,
        bool IsTransparencyGroup,
        bool IsIsolated,
        bool IsKnockout,
        PdfColorSpaceFamily? ColorSpace,
        int? ColorSpaceComponents,
        double FillAlpha,
        BlendMode BlendMode,
        Matrix Matrix);

    private sealed class FormRecorder : ContentProcessor
    {
        public List<FormRecord> Forms { get; } = [];

        public override ContentEvents Events => ContentEvents.Forms;

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
        {
            Forms.Add(new FormRecord(
                form.Reference,
                form.IsTransparencyGroup,
                form.IsIsolated,
                form.IsKnockout,
                form.GroupColorSpace?.Family,
                form.GroupColorSpace?.ComponentCount,
                form.FillAlpha,
                form.BlendMode,
                form.Matrix));
            return ContentVisit.Enter;
        }
    }
}
