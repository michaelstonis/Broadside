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

    [Theory]
    [InlineData("gray", PdfColorSpaceFamily.DeviceGray, 1)]
    [InlineData("cmyk", PdfColorSpaceFamily.DeviceCmyk, 4)]
    [InlineData("icc", PdfColorSpaceFamily.IccBased, 3)]
    public void A_written_group_colour_space_re_reads_through_the_form_events(string space, PdfColorSpaceFamily family, int components)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 100, 100));
        form.CreateTransparencyGroup().ColorSpace = space switch
        {
            "gray" => PdfColor.FromGray(0).ColorSpace,
            "cmyk" => PdfColor.FromCmyk(0, 0, 0, 1).ColorSpace,
            _ => document.CreateIccBasedColorSpace(CanvasTests.RgbProfile()),
        };
        DrawSquare(form);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawForm(form);
        }

        FormRecord recorded = Assert.Single(ReplayForms(document));

        Assert.Equal(family, recorded.ColorSpace);
        Assert.Equal(components, recorded.ColorSpaceComponents);
    }

    [Fact]
    public void A_page_group_re_reads_with_its_flags_and_colour_space()
    {
        using PdfDocument document = PdfDocument.Create();
        Assert.Null(document.Pages[0].TransparencyGroup);
        PdfTransparencyGroup group = document.Pages[0].CreateTransparencyGroup();
        group.IsIsolated = true;
        group.ColorSpace = PdfColor.FromRgb(0, 0, 0).ColorSpace;

        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        PdfTransparencyGroup read = Assert.IsType<PdfTransparencyGroup>(reopened.Pages[0].TransparencyGroup);

        Assert.True(read.IsIsolated);
        Assert.False(read.IsKnockout);
        Assert.Equal(PdfColorSpaceFamily.DeviceRgb, read.ColorSpace?.Family);
    }

    [Fact]
    public void Creating_a_group_twice_returns_the_existing_group_and_clearing_a_flag_removes_its_entry()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 100, 100));
        form.CreateTransparencyGroup().IsKnockout = true;

        PdfTransparencyGroup again = form.CreateTransparencyGroup();

        Assert.True(again.IsKnockout);
        again.IsKnockout = false;
        Assert.False(again.Dictionary.ContainsKey(new CosName("K")));
    }

    [Fact]
    public void A_group_colour_space_that_cannot_blend_is_refused()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfTransparencyGroup group = document.CreateFormXObject(new PdfRectangle(0, 0, 10, 10)).CreateTransparencyGroup();

        Assert.Throws<ArgumentException>(() => group.ColorSpace = document.CreateSeparationColorSpace("Spot", PdfColor.FromGray(0)));
        Assert.Null(group.ColorSpace);
    }

    [Fact]
    public void A_form_drawn_on_three_pages_is_written_once()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 100, 100));
        DrawSquare(form);
        for (int page = 0; page < 3; page++)
        {
            using PdfCanvas canvas = document.Pages[page].CreateCanvas();
            canvas.DrawForm(form);
        }

        byte[] saved = CanvasTests.Save(document, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.None));
        using PdfDocument reopened = PdfDocument.Open(saved);
        var references = new List<CosReference?>();
        for (int page = 0; page < 3; page++)
        {
            var recorder = new FormRecorder();
            reopened.Pages[page].ProcessContent(recorder);
            references.Add(Assert.Single(recorder.Forms).Reference);
        }

        Assert.NotNull(references[0]);
        Assert.Single(references.Distinct());
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(saved), @"/Subtype\s*/Form\b"));
    }

    [Theory]
    [InlineData(PdfSoftMaskType.Luminosity)]
    [InlineData(PdfSoftMaskType.Alpha)]
    public void A_soft_mask_re_reads_with_its_type_backdrop_transfer_function_and_matrix(PdfSoftMaskType type)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject group = MaskGroup(document);
        var own = new Matrix(1, 0, 0, 1, 5, 7);
        group.Matrix = own;
        bool luminosity = type == PdfSoftMaskType.Luminosity;
        PdfSoftMask mask = document.CreateSoftMask(type, group, luminosity ? [0.5] : null, document.CreateExponentialFunction([1], [0], 1));
        var placed = new Matrix(2, 0, 0, 2, 10, 20);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.Transform(placed);
            canvas.SetSoftMask(mask);
            canvas.Rectangle(0, 0, 10, 10);
            canvas.Fill();
        }

        MaskRecord recorded = Assert.Single(ReplayMasks(document));

        Assert.Equal(type, recorded.Subtype);
        Assert.Equal(luminosity ? [0.5] : null, recorded.Backdrop);
        Assert.True(recorded.HasTransferFunction);
        Assert.Equal(placed, recorded.Matrix);
        Assert.Equal(own * placed, Assert.Single(recorded.GroupCtms));
    }

    [Fact]
    public void Removing_the_soft_mask_writes_none_and_restoring_the_state_brings_it_back()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfSoftMask mask = document.CreateSoftMask(PdfSoftMaskType.Alpha, MaskGroup(document));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetSoftMask(mask);
            canvas.SaveState();
            canvas.SetSoftMask(null);
            canvas.Rectangle(0, 0, 10, 10);
            canvas.Fill();
            canvas.RestoreState();
            canvas.Rectangle(0, 0, 10, 10);
            canvas.Fill();
        }

        List<MaskRecord> recorded = ReplayMasks(document);

        Assert.Null(recorded[0].Subtype);
        Assert.Equal(PdfSoftMaskType.Alpha, recorded[1].Subtype);
    }

    [Fact]
    public void Equal_soft_masks_are_written_once()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject group = MaskGroup(document);

        PdfSoftMask first = document.CreateSoftMask(PdfSoftMaskType.Luminosity, group, [0.5]);
        PdfSoftMask second = document.CreateSoftMask(PdfSoftMaskType.Luminosity, group, [0.5]);

        Assert.NotNull(first.Reference);
        Assert.Equal(first.Reference, second.Reference);
        Assert.NotEqual(first.Reference, document.CreateSoftMask(PdfSoftMaskType.Alpha, group).Reference);
    }

    [Fact]
    public void A_soft_mask_set_before_a_group_form_masks_the_form_as_a_whole()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfSoftMask mask = document.CreateSoftMask(PdfSoftMaskType.Alpha, MaskGroup(document));
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 100, 100));
        form.CreateTransparencyGroup();
        DrawSquare(form);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetSoftMask(mask);
            canvas.DrawForm(form);
        }

        FormRecord recorded = Assert.Single(ReplayForms(document));

        Assert.Equal(PdfSoftMaskType.Alpha, recorded.SoftMask);
    }

    [Fact]
    public void Soft_mask_arguments_are_validated()
    {
        using PdfDocument document = PdfDocument.Create();
        using PdfDocument other = PdfDocument.Create();
        PdfFormXObject group = MaskGroup(document);
        PdfFormXObject plain = document.CreateFormXObject(new PdfRectangle(0, 0, 10, 10));
        PdfFormXObject noSpace = document.CreateFormXObject(new PdfRectangle(0, 0, 10, 10));
        noSpace.CreateTransparencyGroup();

        Assert.Throws<ArgumentException>(() => document.CreateSoftMask(PdfSoftMaskType.Alpha, MaskGroup(other)));
        Assert.Throws<ArgumentException>(() => document.CreateSoftMask(PdfSoftMaskType.Alpha, plain));
        Assert.Throws<ArgumentException>(() => document.CreateSoftMask(PdfSoftMaskType.Luminosity, noSpace));
        Assert.Throws<ArgumentException>(() => document.CreateSoftMask(PdfSoftMaskType.Alpha, group, [0]));
        Assert.Throws<ArgumentException>(() => document.CreateSoftMask(PdfSoftMaskType.Luminosity, group, [0, 0]));
        Assert.Throws<ArgumentException>(() => document.CreateSoftMask(PdfSoftMaskType.Luminosity, group, [double.NaN]));
        Assert.Throws<ArgumentException>(() => document.CreateSoftMask(PdfSoftMaskType.Alpha, group, transferFunction: document.CreateExponentialFunction([0, 0], [1, 1], 1)));
        Assert.Throws<ArgumentException>(() => document.CreateSoftMask(PdfSoftMaskType.Alpha, group, transferFunction: other.CreateExponentialFunction([0], [1], 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.CreateSoftMask((PdfSoftMaskType)7, group));
        PdfSoftMask foreign = other.CreateSoftMask(PdfSoftMaskType.Alpha, MaskGroup(other));
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        Assert.Throws<ArgumentException>(() => canvas.SetSoftMask(foreign));
    }

    [Theory]
    [InlineData("group", "transparency group")]
    [InlineData("page group", "transparency group")]
    [InlineData("soft mask", "soft mask")]
    public void Groups_and_soft_masks_raise_a_pdf_1_3_document_to_1_4(string feature, string named)
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.3"));
        Write(document, feature);

        byte[] saved = CanvasTests.Save(document, new PdfSaveOptions().WithUpdateModificationDate(false));

        Assert.Equal("%PDF-1.4", System.Text.Encoding.ASCII.GetString(saved, 0, 8));
        Assert.Contains(named, Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_mode_refuses_to_save_a_soft_mask_in_a_pdf_1_3_document()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.3"), new PdfOptions().UseStrict());
        Write(document, "soft mask");

        using var output = new MemoryStream();
        Broadside.Diagnostics.DiagnosticException exception = Assert.Throws<Broadside.Diagnostics.DiagnosticException>(() => document.Save(output));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_page_with_groups_and_soft_masks_passes_qpdf_check_and_renders_in_poppler_without_warnings()
    {
        byte[] saved;
        using (PdfDocument document = DrawGroupsAndMasks())
        {
            saved = CanvasTests.Saved(document);
        }

        (int exitCode, string output) = Writing.ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        Assert.DoesNotContain("WARNING", output, StringComparison.OrdinalIgnoreCase);
        (int renderCode, byte[] image, string error) = Writing.ExternalTool.RunBinary("pdftoppm", saved, "-r", "36", "-f", "1", "-l", "1");
        Assert.True(renderCode == 0, error);
        Assert.Equal(string.Empty, error.Trim());
        Assert.NotEmpty(image);
        using PdfDocument reopened = PdfDocument.Open(saved, new PdfOptions().UseStrict());
        reopened.Pages[0].ProcessContent(new FormRecorder());
        Assert.Empty(reopened.Diagnostics);
    }

    /// <summary>
    /// A knockout group of two overlapping squares at fill alpha 0.5 (x 100 to 250), an isolated group of two overlapping opaque
    /// red squares drawn at fill alpha 0.5 (x 300 to 450) and a red square under a luminosity mask whose group paints grey 0.5 over
    /// its left half (x 500 to 600), all at y 100 to 200 on a 612 x 792 page.
    /// </summary>
    private static PdfDocument DrawGroupsAndMasks()
    {
        PdfDocument document = PdfDocument.Create();
        PdfFormXObject knockout = document.CreateFormXObject(new PdfRectangle(0, 0, 612, 792));
        knockout.CreateTransparencyGroup().IsKnockout = true;
        using (PdfCanvas canvas = knockout.CreateCanvas())
        {
            canvas.SetFillAlpha(0.5);
            canvas.SetFillColor(PdfColor.FromRgb(1, 0, 0));
            canvas.Rectangle(100, 100, 100, 100);
            canvas.Fill();
            canvas.SetFillColor(PdfColor.FromRgb(0, 0, 1));
            canvas.Rectangle(150, 100, 100, 100);
            canvas.Fill();
        }

        PdfFormXObject isolated = document.CreateFormXObject(new PdfRectangle(0, 0, 612, 792));
        isolated.CreateTransparencyGroup().IsIsolated = true;
        using (PdfCanvas canvas = isolated.CreateCanvas())
        {
            canvas.SetFillColor(PdfColor.FromRgb(1, 0, 0));
            canvas.Rectangle(300, 100, 100, 100);
            canvas.Fill();
            canvas.Rectangle(350, 100, 100, 100);
            canvas.Fill();
        }

        PdfFormXObject maskGroup = document.CreateFormXObject(new PdfRectangle(500, 100, 550, 200));
        maskGroup.CreateTransparencyGroup().ColorSpace = PdfColor.FromGray(0).ColorSpace;
        using (PdfCanvas canvas = maskGroup.CreateCanvas())
        {
            canvas.SetFillColor(PdfColor.FromGray(0.5f));
            canvas.Rectangle(500, 100, 50, 100);
            canvas.Fill();
        }

        PdfSoftMask mask = document.CreateSoftMask(PdfSoftMaskType.Luminosity, maskGroup);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawForm(knockout);
            canvas.SaveState();
            canvas.SetFillAlpha(0.5);
            canvas.DrawForm(isolated);
            canvas.RestoreState();
            canvas.SaveState();
            canvas.SetSoftMask(mask);
            canvas.SetFillColor(PdfColor.FromRgb(1, 0, 0));
            canvas.Rectangle(500, 100, 100, 100);
            canvas.Fill();
            canvas.RestoreState();
        }

        return document;
    }

    private static void Write(PdfDocument document, string feature)
    {
        switch (feature)
        {
            case "group":
                document.CreateFormXObject(new PdfRectangle(0, 0, 10, 10)).CreateTransparencyGroup();
                break;
            case "page group":
                document.Pages[0].CreateTransparencyGroup();
                break;
            default:
                PdfSoftMask mask = document.CreateSoftMask(PdfSoftMaskType.Alpha, MaskGroup(document));
                using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
                {
                    canvas.SetSoftMask(mask);
                }

                break;
        }
    }

    /// <summary>A mask group of 100 x 100: a DeviceGray transparency group painting grey 0.5 over its bounding box.</summary>
    private static PdfFormXObject MaskGroup(PdfDocument document)
    {
        PdfFormXObject group = document.CreateFormXObject(new PdfRectangle(0, 0, 100, 100));
        group.CreateTransparencyGroup().ColorSpace = PdfColor.FromGray(0).ColorSpace;
        using PdfCanvas canvas = group.CreateCanvas();
        canvas.SetFillColor(PdfColor.FromGray(0.5f));
        canvas.Rectangle(0, 0, 100, 100);
        canvas.Fill();
        return group;
    }

    /// <summary>Saves, reopens and records the soft mask in effect at every path paint of the first page.</summary>
    private static List<MaskRecord> ReplayMasks(PdfDocument document)
    {
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        var recorder = new MaskRecorder();
        reopened.Pages[0].ProcessContent(recorder);
        Assert.Empty(reopened.Diagnostics);
        return recorder.Masks;
    }

    private sealed record MaskRecord(PdfSoftMaskType? Subtype, double[]? Backdrop, bool HasTransferFunction, Matrix Matrix, List<Matrix> GroupCtms);

    private sealed class MaskRecorder : ContentProcessor
    {
        public List<MaskRecord> Masks { get; } = [];

        public override ContentEvents Events => ContentEvents.Paths;

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            PdfSoftMask? mask = context.State.SoftMask;
            var group = new CtmRecorder();
            context.RunSoftMaskGroup(group);
            Masks.Add(new MaskRecord(mask?.Subtype, mask?.Backdrop?.ToArray(), mask?.TransferFunction is not null, context.State.SoftMaskMatrix, group.Ctms));
        }
    }

    private sealed class CtmRecorder : ContentProcessor
    {
        public List<Matrix> Ctms { get; } = [];

        public override ContentEvents Events => ContentEvents.Paths;

        public override void PaintPath(in PathEvent path, ContentContext context) => Ctms.Add(context.State.Ctm);
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
        Matrix Matrix,
        PdfSoftMaskType? SoftMask);

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
                form.Matrix,
                form.SoftMask?.Subtype));
            return ContentVisit.Enter;
        }
    }
}
