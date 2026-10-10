using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// Form XObjects (ISO 32000-2 §8.10.1): <c>Do</c> saves the state, concatenates the form matrix, clips to the bounding box, runs
/// the form's content with its own (or the inherited) resources and restores the state; nesting is bounded and cycles are refused.
/// </summary>
public sealed class FormTests
{
    [Fact]
    public void Nested_forms_run_with_their_matrix_bounding_box_and_resources()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("form-xobject-nested.pdf"));
        var recorder = new FormRecorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.Equal(
        [
            "q 1",
            "BeginForm FA depth=1 ctm=[1 0 0 1 0 0] matrix=[2 0 0 2 100 100] bbox=0,0,50,50 structParents=3",
            "q 2",
            "Clip Rectangle M 0,0 L 50,0 L 50,50 L 0,50 Z ctm=[2 0 0 2 100 100]",
            "Paint Fill ctm=[2 0 0 2 100 100] run=Form depth=1 structParents=3",
            "BeginForm FB depth=2 ctm=[2 0 0 2 100 100] matrix=[1 0 0 1 0 0] bbox=0,0,20,20 structParents=",
            "q 3",
            "Clip Rectangle M 0,0 L 20,0 L 20,20 L 0,20 Z ctm=[2 0 0 2 100 100]",
            "Glyph 66 Helvetica tm=[12 0 0 12 1 2]",
            "Paint Stroke ctm=[2 0 0 2 100 100] run=Form depth=2 structParents=",
            "Q 2",
            "EndForm FB",
            "Q 1",
            "EndForm FA",
            "Q 0",
            "BeginForm FC depth=1 ctm=[1 0 0 1 0 0] matrix=[1 0 0 1 0 0] bbox=0,0,30,30 structParents=",
            "q 1",
            "Clip Rectangle M 0,0 L 30,0 L 30,30 L 0,30 Z ctm=[1 0 0 1 0 0]",
            "Paint Stroke ctm=[1 0 0 1 0 0] run=Form depth=1 structParents=",
            "Q 0",
            "EndForm FC",
        ],
            recorder.Lines);
        Assert.Equal(["ContentFormCycle"], ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void Forms_nested_deeper_than_the_limit_are_not_run()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("form-xobject-nested.pdf"));
        var recorder = new FormRecorder();

        document.Pages[0].ProcessContent(recorder, new ContentOptions().WithMaxNestingDepth(1));

        Assert.Contains("BeginForm FA depth=1 ctm=[1 0 0 1 0 0] matrix=[2 0 0 2 100 100] bbox=0,0,50,50 structParents=3", recorder.Lines);
        Assert.DoesNotContain(recorder.Lines, line => line.StartsWith("BeginForm FB", StringComparison.Ordinal));
        Assert.Contains("ContentNestingTooDeep", ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void A_processor_that_skips_a_form_can_run_it_through_the_context()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("form-xobject-nested.pdf"));
        var inner = new FormRecorder();
        var skipping = new SkippingProcessor(inner);

        document.Pages[0].ProcessContent(skipping);

        Assert.Equal(0, skipping.Paints);
        Assert.Contains("Paint Fill ctm=[2 0 0 2 100 100] run=Form depth=1 structParents=3", inner.Lines);
        Assert.Contains("EndForm ", inner.Lines);
    }

    [Fact]
    public void A_form_without_resources_on_the_page_uses_the_pages_and_a_missing_name_is_reported()
    {
        string form = ContentPdf.Stream("BT /F1 10 Tf (a) Tj ET", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/Font << /F1 5 0 R >> /XObject << /Fm 6 0 R >>", "/Fm Do /Missing Do", ContentPdf.Helvetica, form));
        var recorder = new GlyphRecorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.Equal("Helvetica", Assert.Single(recorder.Glyphs).Font?.BaseFont);
        Assert.Equal(["ContentXObjectMissing"], ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void Unbalanced_q_and_marked_content_inside_a_form_are_closed_at_its_end()
    {
        string form = ContentPdf.Stream("q q /Tag BMC 0 0 1 1 re f", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith("/XObject << /Fm 5 0 R >>", "/Outer BMC /Fm Do 0 0 1 1 re f EMC", form));
        var recorder = new RecordingProcessor(ContentEvents.Paths | ContentEvents.MarkedContent | ContentEvents.StateStack);

        document.Pages[0].ProcessContent(recorder);

        Assert.Equal(["BMC", "q 1", "q 2", "q 3", "BMC", "Paint", "EMC", "Q 2", "Q 1", "Q 0", "Paint", "EMC"], recorder.Body.Select(Short));
        Assert.Equal(["ContentMarkedContentUnbalanced", "ContentUnbalancedSave"], ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void A_transparency_group_reports_the_outer_compositing_parameters_and_starts_inside_from_the_initial_ones()
    {
        string form = ContentPdf.Stream(
            "0 0 1 1 re f",
            "/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Group << /S /Transparency /I true /K false /CS /DeviceRGB >>");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/XObject << /Fm 5 0 R >> /ExtGState << /G 6 0 R >>",
            "/G gs /Fm Do",
            form,
            "<< /BM /Multiply /ca 0.5 /CA 0.25 >>"));
        var recorder = new GroupRecorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.True(recorder.IsTransparencyGroup);
        Assert.True(recorder.IsIsolated);
        Assert.False(recorder.IsKnockout);
        Assert.Equal(PdfColorSpaceFamily.DeviceRgb, recorder.GroupFamily);
        Assert.Equal((BlendMode.Multiply, 0.5, 0.25), recorder.Outer);
        Assert.Equal((BlendMode.Normal, 1.0, 1.0), recorder.Inside);
        Assert.Equal(ContentRunKind.Group, recorder.InsideRunKind);
    }

    [Fact]
    public void A_form_whose_optional_content_is_off_has_no_events()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("optional-content.pdf"));
        var recorder = new FormRecorder();

        document.Pages[0].ProcessContent(recorder);

        Assert.DoesNotContain(recorder.Lines, line => line.StartsWith("BeginForm", StringComparison.Ordinal));
    }

    [Fact]
    public void An_image_xobject_is_painted_into_the_unit_square_of_the_ctm()
    {
        string image = ContentPdf.Stream("\u0080", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /StructParent 7");
        string mask = ContentPdf.Stream("\u0080", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ImageMask true");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/XObject << /Im 5 0 R /Mk 6 0 R >>", "q 10 0 0 20 30 40 cm /Im Do /Mk Do Q", image, mask));
        var images = new ImageRecorder();

        document.Pages[0].ProcessContent(images);

        Assert.Equal(2, images.Images.Count);
        (bool inline, string name, bool stencil, Matrix ctm, int? structParent, CosStream? stream) = images.Images[0];
        Assert.False(inline);
        Assert.Equal("Im", name);
        Assert.False(stencil);
        Assert.Equal(new Matrix(10, 0, 0, 20, 30, 40), ctm);
        Assert.Equal(7, structParent);
        Assert.NotNull(stream);
        Assert.True(images.Images[1].Stencil);
        Assert.Equal([(1, 1, false), (1, 1, true)], images.Models);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_pattern_selected_in_a_form_is_relative_to_the_form_space_at_do()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-in-form.pdf"));
        var recorder = new PatternRecorder();

        document.Pages[0].ProcessContent(recorder);

        // §8.7.2: CTM at Do (1 0 0 1 50 50) x form Matrix (0.5 0 0 0.5 100 100); the cell runs in that space.
        var expected = new Matrix(0.5, 0, 0, 0.5, 150, 150);
        Assert.Equal(ContentRunKind.Form, recorder.RunKind);
        Assert.Equal(expected, recorder.PatternMatrix);
        Assert.Equal(expected, recorder.CellCtm);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Images_and_colour_parameters_are_ignored_in_an_uncoloured_cell_and_image_masks_are_painted()
    {
        string pattern = ContentPdf.Stream(
            "/G gs /Im Do /Mk Do 0 0 5 5 re f",
            "/PatternType 1 /PaintType 2 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10 /Resources << /XObject << /Im 6 0 R /Mk 7 0 R >> /ExtGState << /G 8 0 R >> >>");
        string image = ContentPdf.Stream("\u0080", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8");
        string mask = ContentPdf.Stream("\u0080", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ImageMask true");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/ColorSpace << /Cs [/Pattern /DeviceGray] >> /Pattern << /P1 5 0 R >>",
            "/Cs cs 0.5 /P1 scn 0 0 100 100 re f",
            pattern,
            image,
            mask,
            "<< /OP true /RI /Saturation /LW 4 >>"));
        var cell = new CellContent();
        var runner = new CellStarter(cell);

        document.Pages[0].ProcessContent(runner);

        Assert.Equal([true], cell.ImageStencils);
        GraphicsState state = Assert.Single(cell.States);
        Assert.Equal(4, state.LineWidth);
        Assert.False(state.StrokeOverprint);
        Assert.Equal(RenderingIntent.RelativeColorimetric, state.RenderingIntent);
        Assert.Contains("ContentColorOperatorIgnored", ContentPdf.Codes(document.Diagnostics));
    }

    private static string Short(string line) => line.Split(' ')[0] is "q" or "Q" ? line : line.Split(' ')[0];

    internal sealed class FormRecorder : ContentProcessor
    {
        public List<string> Lines { get; } = [];

        public override ContentEvents Events => ContentEvents.All & ~ContentEvents.Operators;

        public override void SaveState(ContentContext context) => Lines.Add($"q {context.StateDepth}");

        public override void RestoreState(ContentContext context) => Lines.Add($"Q {context.StateDepth}");

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
        {
            string box = $"{form.BoundingBox.Left},{form.BoundingBox.Bottom},{form.BoundingBox.Right},{form.BoundingBox.Top}";
            Lines.Add($"BeginForm {System.Text.Encoding.Latin1.GetString(form.ResourceName)} depth={form.Depth} ctm={RecordingProcessor.Matrix(context.State.Ctm)} "
                + $"matrix={RecordingProcessor.Matrix(form.Matrix)} bbox={box} structParents={form.StructParents}");
            return ContentVisit.Enter;
        }

        public override void EndForm(in FormEvent form, ContentContext context) => Lines.Add($"EndForm {System.Text.Encoding.Latin1.GetString(form.ResourceName)}");

        public override void IntersectClip(in ClipEvent clip, ContentContext context) =>
            Lines.Add($"Clip {clip.Kind} {RecordingProcessor.Path(clip.Path)} ctm={RecordingProcessor.Matrix(clip.Ctm)}");

        public override void PaintPath(in PathEvent path, ContentContext context) =>
            Lines.Add($"Paint {path.Paint} ctm={RecordingProcessor.Matrix(context.State.Ctm)} run={context.RunKind} depth={context.Depth} structParents={context.StructParents}");

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context) =>
            Lines.Add($"Glyph {glyph.CharacterCode} {glyph.Font?.BaseFont} tm={RecordingProcessor.Matrix(glyph.TextMatrix)}");
    }

    private sealed class SkippingProcessor(ContentProcessor inner) : ContentProcessor
    {
        public int Paints { get; private set; }

        public override ContentEvents Events => ContentEvents.Forms | ContentEvents.Paths;

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
        {
            if (form.Depth == 1 && form.ResourceName.SequenceEqual("FA"u8))
            {
                context.RunForm(form.Form!, inner);
            }

            return ContentVisit.Skip;
        }

        public override void PaintPath(in PathEvent path, ContentContext context) => Paints++;
    }

    private sealed class PatternRecorder : ContentProcessor
    {
        public ContentRunKind RunKind { get; private set; }

        public Matrix PatternMatrix { get; private set; }

        public Matrix? CellCtm { get; private set; }

        public override ContentEvents Events => ContentEvents.Paths;

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            PdfColor color = context.State.FillColor;
            if (context.RunKind == ContentRunKind.Pattern)
            {
                CellCtm ??= context.StreamBaseMatrix;
                return;
            }

            if (color.ColorSpace is PdfPatternColorSpace)
            {
                RunKind = context.RunKind;
                PatternMatrix = color.PatternMatrix;
                context.RunPatternCell(color, this);
            }
        }
    }

    private sealed class CellStarter(ContentProcessor cell) : ContentProcessor
    {
        public override ContentEvents Events => ContentEvents.Paths;

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            PdfColor color = context.State.FillColor;
            if (color.ColorSpace is PdfPatternColorSpace)
            {
                context.RunPatternCell(color, cell);
            }
        }
    }

    private sealed class CellContent : ContentProcessor
    {
        public List<bool> ImageStencils { get; } = [];

        public List<GraphicsState> States { get; } = [];

        public override ContentEvents Events => ContentEvents.Paths | ContentEvents.Images;

        public override void PaintImage(in ImageEvent image, ContentContext context) => ImageStencils.Add(image.IsStencil);

        public override void PaintPath(in PathEvent path, ContentContext context) => States.Add(context.State);
    }

    private sealed class GroupRecorder : ContentProcessor
    {
        public bool IsTransparencyGroup { get; private set; }

        public bool IsIsolated { get; private set; }

        public bool IsKnockout { get; private set; }

        public PdfColorSpaceFamily? GroupFamily { get; private set; }

        public (BlendMode, double, double) Outer { get; private set; }

        public (BlendMode, double, double) Inside { get; private set; }

        public ContentRunKind InsideRunKind { get; private set; }

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
        {
            IsTransparencyGroup = form.IsTransparencyGroup;
            IsIsolated = form.IsIsolated;
            IsKnockout = form.IsKnockout;
            GroupFamily = form.GroupColorSpace?.Family;
            Outer = (form.BlendMode, form.FillAlpha, form.StrokeAlpha);
            return ContentVisit.Enter;
        }

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            Inside = (context.State.BlendMode, context.State.FillAlpha, context.State.StrokeAlpha);
            InsideRunKind = context.RunKind;
        }
    }
}

/// <summary>Records image events.</summary>
internal sealed class ImageRecorder : ContentProcessor
{
    public List<(bool Inline, string Name, bool Stencil, Matrix Ctm, int? StructParent, CosStream? Stream)> Images { get; } = [];

    public List<(byte[] Data, int Entries)> Inline { get; } = [];

    public List<(int? Width, int? Height, bool? Stencil)> Models { get; } = [];

    public override ContentEvents Events => ContentEvents.Images;

    public override void PaintImage(in ImageEvent image, ContentContext context)
    {
        Images.Add((image.IsInline, System.Text.Encoding.Latin1.GetString(image.ResourceName), image.IsStencil, image.Ctm, image.StructParent, image.Stream));
        Models.Add((image.Image?.Width, image.Image?.Height, image.Image?.IsStencil));
        if (image.IsInline)
        {
            Inline.Add((image.InlineData.ToArray(), image.InlineDictionary.Items.Count / 2));
        }
    }
}
