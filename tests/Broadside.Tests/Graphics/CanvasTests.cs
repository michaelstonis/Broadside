using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Drawing on a page through <see cref="PdfCanvas"/>: what was drawn re-reads, after a save, as the same path, colour and state
/// events. ISO 32000-2 §8.4 (graphics state), §8.5 (path construction and painting), §8.6 (colour spaces).
/// </summary>
public sealed class CanvasTests
{
    private static readonly CosName ColorSpaceKey = new("ColorSpace");
    private static readonly CosName ExtGStateKey = new("ExtGState");
    private static readonly CosName XObjectKey = new("XObject");

    [Fact]
    public void A_filled_rectangle_saved_and_reopened_replays_as_the_same_path_paint()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.Rectangle(10, 20, 100, 50);
            canvas.Fill();
        }

        RecordingProcessor recorder = Replay(document);

        Assert.Equal(["Paint Fill NonZero M 10,20 L 110,20 L 110,70 L 10,70 Z ctm=[1 0 0 1 0 0]"], recorder.Semantic);
    }

    [Fact]
    public void Every_path_construction_painting_and_clipping_call_replays_as_its_path_event()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.MoveTo(0, 0);
            canvas.LineTo(10, 0);
            canvas.CurveTo(15, 0, 20, 5, 20, 10);
            canvas.ClosePath();
            canvas.Stroke();

            canvas.MoveTo(1, 1);
            canvas.LineTo(2, 2);
            canvas.CloseAndStroke();

            canvas.Rectangle(0, 0, 4, 4);
            canvas.FillAndStroke(FillRule.EvenOdd);

            canvas.MoveTo(5, 5);
            canvas.LineTo(6, 5);
            canvas.LineTo(6, 6);
            canvas.CloseFillAndStroke();

            canvas.Rectangle(1, 2, 3, 4);
            canvas.Fill(FillRule.EvenOdd);

            canvas.Rectangle(0, 0, 50, 50);
            canvas.Clip(FillRule.EvenOdd);
            canvas.EndPath();
        }

        RecordingProcessor recorder = Replay(document);

        Assert.Equal(
            [
                "Paint Stroke NonZero M 0,0 L 10,0 C 15,0 20,5 20,10 Z ctm=[1 0 0 1 0 0]",
                "Paint Stroke NonZero closed M 1,1 L 2,2 Z ctm=[1 0 0 1 0 0]",
                "Paint FillAndStroke EvenOdd M 0,0 L 4,0 L 4,4 L 0,4 Z ctm=[1 0 0 1 0 0]",
                "Paint FillAndStroke NonZero closed M 5,5 L 6,5 L 6,6 Z ctm=[1 0 0 1 0 0]",
                "Paint Fill EvenOdd M 1,2 L 4,2 L 4,6 L 1,6 Z ctm=[1 0 0 1 0 0]",
                "Paint None NonZero clip=EvenOdd M 0,0 L 50,0 L 50,50 L 0,50 Z ctm=[1 0 0 1 0 0]",
                "Clip Path EvenOdd #1<#0 M 0,0 L 50,0 L 50,50 L 0,50 Z ctm=[1 0 0 1 0 0]",
            ],
            recorder.Semantic);
    }

    [Fact]
    public void Transforms_line_attributes_and_dashes_replay_in_the_graphics_state_and_restore_with_the_state()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SaveState();
            canvas.Translate(100, 200);
            canvas.Scale(2, 3);
            canvas.Rotate(90);
            canvas.Transform(new Matrix(1, 0, 0, 1, 5, 5));
            canvas.SetLineWidth(2.5);
            canvas.SetLineCap(LineCap.Round);
            canvas.SetLineJoin(LineJoin.Bevel);
            canvas.SetMiterLimit(4);
            canvas.SetDashPattern([3, 1.5], 0.5);
            canvas.SetFlatness(5);
            canvas.SetRenderingIntent(RenderingIntent.Saturation);
            canvas.Rectangle(0, 0, 1, 1);
            canvas.Stroke();
            Assert.Equal(1, canvas.StateDepth);
            canvas.RestoreState();
            canvas.Rectangle(0, 0, 1, 1);
            canvas.Stroke();
        }

        List<GraphicsState> states = ReplayStates(document, out List<double[]> dashes);

        Assert.Equal(2, states.Count);
        GraphicsState drawn = states[0];
        Assert.Equal(new Matrix(0, 3, -2, 0, 90, 215), Round(drawn.Ctm));
        Assert.Equal(2.5, drawn.LineWidth);
        Assert.Equal(LineCap.Round, drawn.LineCap);
        Assert.Equal(LineJoin.Bevel, drawn.LineJoin);
        Assert.Equal(4, drawn.MiterLimit);
        Assert.Equal([3, 1.5], dashes[0]);
        Assert.Equal(0.5, drawn.DashPhase);
        Assert.Equal(5, drawn.Flatness);
        Assert.Equal(RenderingIntent.Saturation, drawn.RenderingIntent);
        GraphicsState restored = states[1];
        Assert.Equal(Matrix.Identity, restored.Ctm);
        Assert.Equal(1, restored.LineWidth);
        Assert.Empty(dashes[1]);
    }

    [Fact]
    public void Device_colours_replay_as_the_fill_and_stroke_colours_drawn()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFillColor(PdfColor.FromGray(0.25f));
            canvas.SetStrokeColor(PdfColor.FromRgb(1, 0.5f, 0));
            canvas.Rectangle(0, 0, 1, 1);
            canvas.FillAndStroke();
            canvas.SetFillColor(PdfColor.FromCmyk(0.1f, 0.2f, 0.3f, 0.4f));
            canvas.SetStrokeColor(PdfColor.FromGray(1));
            canvas.Rectangle(0, 0, 1, 1);
            canvas.FillAndStroke();
        }

        List<GraphicsState> states = ReplayStates(document, out _);

        AssertColor(states[0].FillColor, PdfColorSpaceFamily.DeviceGray, 0.25f);
        AssertColor(states[0].StrokeColor, PdfColorSpaceFamily.DeviceRgb, 1, 0.5f, 0);
        AssertColor(states[1].FillColor, PdfColorSpaceFamily.DeviceCmyk, 0.1f, 0.2f, 0.3f, 0.4f);
        AssertColor(states[1].StrokeColor, PdfColorSpaceFamily.DeviceGray, 1);
        Assert.False(document.Pages[0].Resources!.ContainsKey(ColorSpaceKey));
    }

    [Fact]
    public void An_icc_based_colour_replays_in_an_icc_based_space_holding_the_profile()
    {
        byte[] profile = RgbProfile();
        using PdfDocument document = PdfDocument.Create();
        PdfIccBasedColorSpace space = document.CreateIccBasedColorSpace(profile);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFillColor(new PdfColor(space, [0.2f, 0.4f, 0.6f]));
            canvas.SetStrokeColor(new PdfColor(space, [1, 0, 0]));
            canvas.Rectangle(0, 0, 1, 1);
            canvas.FillAndStroke();
        }

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        GraphicsState state = ReplayStates(reopened, out _)[0];

        AssertColor(state.FillColor, PdfColorSpaceFamily.IccBased, 0.2f, 0.4f, 0.6f);
        AssertColor(state.StrokeColor, PdfColorSpaceFamily.IccBased, 1, 0, 0);
        var read = (PdfIccBasedColorSpace)state.FillColor.ColorSpace;
        Assert.Equal(3, read.DeclaredComponentCount);
        Assert.Equal(profile, read.GetProfileData().ToArray());
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_separation_colour_replays_with_its_colourant_and_a_tint_transform_from_white_to_the_alternate()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfSeparationColorSpace orange = document.CreateSeparationColorSpace("Spot Orange", PdfColor.FromCmyk(0, 0.5f, 1, 0));
        PdfSeparationColorSpace red = document.CreateSeparationColorSpace("Spot Red", PdfColor.FromRgb(1, 0, 0));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFillColor(new PdfColor(orange, [0.5f]));
            canvas.SetStrokeColor(new PdfColor(red, [0.5f]));
            canvas.Rectangle(0, 0, 1, 1);
            canvas.FillAndStroke();
        }

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        GraphicsState state = ReplayStates(reopened, out _)[0];

        AssertColor(state.FillColor, PdfColorSpaceFamily.Separation, 0.5f);
        var fill = (PdfSeparationColorSpace)state.FillColor.ColorSpace;
        Assert.Equal("Spot Orange", fill.ColorantName.Value);
        Assert.Equal(PdfColorSpaceFamily.DeviceCmyk, fill.Alternate.Family);
        Assert.Equal([0f, 0.25f, 0.5f, 0f], Evaluate(fill.TintTransform!, 0.5f, 4));
        var stroke = (PdfSeparationColorSpace)state.StrokeColor.ColorSpace;
        Assert.Equal("Spot Red", stroke.ColorantName.Value);
        Assert.Equal([1f, 1f, 1f], Evaluate(stroke.TintTransform!, 0, 3));
        Assert.Equal([1f, 0.5f, 0.5f], Evaluate(stroke.TintTransform!, 0.5f, 3));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_devicen_colour_replays_with_its_colourants_and_a_tint_transform_that_mixes_the_alternates()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfDeviceNColorSpace inks = document.CreateDeviceNColorSpace(
            ["Orange", "Green"], [PdfColor.FromCmyk(0, 0.5f, 1, 0), PdfColor.FromCmyk(1, 0, 1, 0)]);
        PdfDeviceNColorSpace lights = document.CreateDeviceNColorSpace(
            ["Red", "Blue"], [PdfColor.FromRgb(1, 0, 0), PdfColor.FromRgb(0, 0, 1)]);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFillColor(new PdfColor(inks, [1, 0.5f]));
            canvas.SetStrokeColor(new PdfColor(lights, [0.5f, 0.5f]));
            canvas.Rectangle(0, 0, 1, 1);
            canvas.FillAndStroke();
        }

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        GraphicsState state = ReplayStates(reopened, out _)[0];

        AssertColor(state.FillColor, PdfColorSpaceFamily.DeviceN, 1, 0.5f);
        var fill = (PdfDeviceNColorSpace)state.FillColor.ColorSpace;
        Assert.Equal(["Orange", "Green"], fill.ColorantNames.Select(name => name.Value));
        Assert.Equal([0.5f, 0.5f, 1f, 0f], Evaluate(fill.TintTransform!, [1, 0.5f], 4));
        var stroke = (PdfDeviceNColorSpace)state.StrokeColor.ColorSpace;
        Assert.Equal([0.5f, 0f, 0.5f], Evaluate(stroke.TintTransform!, [0.5f, 0.5f], 3));
        Assert.Equal([1f, 1f, 1f], Evaluate(stroke.TintTransform!, [0, 0], 3));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Separation_and_devicen_spaces_take_a_tint_transform_of_the_callers()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfExponentialFunction toGray = document.CreateExponentialFunction([1], [0], 1);
        PdfPostScriptFunction twoToGray = document.CreatePostScriptFunction([0, 1, 0, 1], [0, 1], "{ add 2 div 1 exch sub }");
        PdfSeparationColorSpace separation = document.CreateSeparationColorSpace("Black", PdfDeviceGrayColorSpace.Instance, toGray);
        PdfDeviceNColorSpace deviceN = document.CreateDeviceNColorSpace(["A", "B"], PdfDeviceGrayColorSpace.Instance, twoToGray);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFillColor(new PdfColor(separation, [0.25f]));
            canvas.SetStrokeColor(new PdfColor(deviceN, [1, 0]));
            canvas.Rectangle(0, 0, 1, 1);
            canvas.FillAndStroke();
        }

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        GraphicsState state = ReplayStates(reopened, out _)[0];

        Assert.Equal([0.75f], Evaluate(((PdfSeparationColorSpace)state.FillColor.ColorSpace).TintTransform!, 0.25f, 1));
        Assert.Equal([0.5f], Evaluate(((PdfDeviceNColorSpace)state.StrokeColor.ColorSpace).TintTransform!, [1, 0], 1));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Alpha_and_blend_mode_replay_through_graphics_state_parameter_dictionaries()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFillAlpha(0.5);
            canvas.SetStrokeAlpha(0.25);
            canvas.SetBlendMode(BlendMode.Multiply);
            canvas.Rectangle(0, 0, 1, 1);
            canvas.Fill();
        }

        GraphicsState state = ReplayStates(document, out _)[0];

        Assert.Equal(0.5, state.FillAlpha);
        Assert.Equal(0.25, state.StrokeAlpha);
        Assert.Equal(BlendMode.Multiply, state.BlendMode);
    }

    [Fact]
    public void Drawing_the_same_colour_space_twice_adds_one_resource_and_one_object()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 200, 200));
        PdfSeparationColorSpace first = document.CreateSeparationColorSpace("Gold", PdfColor.FromCmyk(0, 0.2f, 0.8f, 0.1f));
        PdfSeparationColorSpace again = document.CreateSeparationColorSpace("Gold", PdfColor.FromCmyk(0, 0.2f, 0.8f, 0.1f));
        foreach (PdfPage page in document.Pages)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                using PdfCanvas canvas = page.CreateCanvas();
                canvas.SetFillColor(new PdfColor(first, [1]));
                canvas.SetStrokeColor(new PdfColor(again, [0.5f]));
                canvas.SetFillAlpha(0.5);
                canvas.Rectangle(0, 0, 1, 1);
                canvas.FillAndStroke();
                canvas.SetFillAlpha(0.5);
            }
        }

        Assert.Same(first, again);
        CosDictionary resources0 = document.Pages[0].Resources!;
        CosDictionary resources1 = document.Pages[1].Resources!;
        CosObject space = Assert.Single(Subdictionary(document, resources0, ColorSpaceKey)).Value;
        Assert.Equal(space, Assert.Single(Subdictionary(document, resources1, ColorSpaceKey)).Value);
        CosObject parameters = Assert.Single(Subdictionary(document, resources0, ExtGStateKey)).Value;
        Assert.Equal(parameters, Assert.Single(Subdictionary(document, resources1, ExtGStateKey)).Value);
        Assert.Equal(first.Reference, space);
    }

    [Fact]
    public void A_resource_name_already_in_the_page_resources_is_kept_and_a_new_one_is_chosen()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/ExtGState << /GS1 << /LW 3 >> >>", "/GS1 gs 0 0 1 1 re S"));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetStrokeAlpha(0.5);
            canvas.Rectangle(0, 0, 1, 1);
            canvas.Stroke();
        }

        List<GraphicsState> states = ReplayStates(document, out _);

        Assert.Equal([3.0, 1.0], states.Select(state => state.LineWidth));
        Assert.Equal([1.0, 0.5], states.Select(state => state.StrokeAlpha));
        Assert.Equal(["GS1", "GS2"], Subdictionary(document, document.Pages[0].Resources!, ExtGStateKey).Keys.Select(key => key.Value).Order());
    }

    [Fact]
    public void A_canvas_on_a_page_with_content_draws_after_it_unaffected_by_the_state_it_leaves()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("2 0 0 2 0 0 cm 5 w 0 0 1 1 re S"));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.Rectangle(0, 0, 1, 1);
            canvas.Stroke();
        }

        List<GraphicsState> states = ReplayStates(document, out _);

        Assert.Equal([new Matrix(2, 0, 0, 2, 0, 0), Matrix.Identity], states.Select(state => state.Ctm));
        Assert.Equal([5.0, 1.0], states.Select(state => state.LineWidth));
    }

    [Fact]
    public void Dispose_ends_an_open_path_and_restores_unbalanced_states()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SaveState();
            canvas.SaveState();
            canvas.Translate(1, 1);
            canvas.MoveTo(0, 0);
        }

        RecordingProcessor recorder = Replay(document);

        Assert.Equal(["q 1", "q 2", "Paint None NonZero M 0,0 ctm=[1 0 0 1 1 1]", "Q 1", "Q 0"], recorder.Semantic);
    }

    [Fact]
    public void A_canvas_that_draws_nothing_leaves_the_page_as_it_was()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages[0].CreateCanvas().Dispose();

        Assert.False(document.Pages[0].Dictionary.ContainsKey(new CosName("Contents")));
    }

    [Fact]
    public void Calls_out_of_the_order_figure_9_allows_throw_and_write_nothing()
    {
        using PdfDocument document = PdfDocument.Create();
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();

        Assert.Throws<InvalidOperationException>(() => canvas.LineTo(1, 1));
        Assert.Throws<InvalidOperationException>(() => canvas.ClosePath());
        Assert.Throws<InvalidOperationException>(() => canvas.Fill());
        Assert.Throws<InvalidOperationException>(() => canvas.Clip());
        Assert.Throws<InvalidOperationException>(() => canvas.RestoreState());
        canvas.MoveTo(0, 0);
        Assert.Throws<InvalidOperationException>(() => canvas.SetLineWidth(2));
        Assert.Throws<InvalidOperationException>(() => canvas.SetFillColor(PdfColor.FromGray(0)));
        Assert.Throws<InvalidOperationException>(() => canvas.SaveState());
        canvas.Clip();
        Assert.Throws<InvalidOperationException>(() => canvas.LineTo(1, 1));
        Assert.Throws<InvalidOperationException>(() => canvas.Clip());
        canvas.EndPath();
        canvas.Dispose();
        Assert.Throws<ObjectDisposedException>(() => canvas.MoveTo(0, 0));
    }

    [Fact]
    public void Invalid_values_are_refused()
    {
        using PdfDocument document = PdfDocument.Create();
        using PdfDocument other = PdfDocument.Create();
        PdfSeparationColorSpace foreign = other.CreateSeparationColorSpace("Spot", PdfColor.FromGray(0));
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();

        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.SetLineWidth(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.SetMiterLimit(0.5));
        Assert.Throws<ArgumentException>(() => canvas.SetDashPattern([0, 0], 0));
        Assert.Throws<ArgumentException>(() => canvas.SetDashPattern([-1], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.SetFillAlpha(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.SetFlatness(101));
        Assert.Throws<ArgumentException>(() => canvas.MoveTo(double.NaN, 0));
        Assert.Throws<ArgumentException>(() => canvas.SetFillColor(new PdfColor(foreign, [1])));
        Assert.Throws<ArgumentException>(() => canvas.SetFillColor(new PdfColor(PdfDeviceRgbColorSpace.Instance, [1])));
        Assert.Throws<ArgumentException>(() => document.CreateSeparationColorSpace(string.Empty, PdfColor.FromGray(0)));
        Assert.Throws<ArgumentException>(() => document.CreateDeviceNColorSpace(["A", "A"], [PdfColor.FromGray(0), PdfColor.FromGray(0)]));
        Assert.Throws<ArgumentException>(() => document.CreateDeviceNColorSpace(["A", "B"], [PdfColor.FromGray(0), PdfColor.FromRgb(0, 0, 0)]));
        Assert.Throws<ArgumentException>(() => document.CreateSeparationColorSpace("S", new PdfColor(foreign, [1])));
        Assert.Throws<ArgumentException>(() => document.CreateIccBasedColorSpace(new byte[200]));
        Assert.Throws<ArgumentException>(() => document.CreatePostScriptFunction([0, 1], [0, 1], "{ 1 frobnicate }"));
        Assert.Throws<ArgumentException>(() => document.CreateSeparationColorSpace(
            "S", PdfDeviceRgbColorSpace.Instance, document.CreateExponentialFunction([0], [1], 1)));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_form_drawn_by_a_canvas_and_painted_on_two_pages_is_one_object_whose_content_replays_inside_each()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 200, 200));
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 10, 10));
        using (PdfCanvas canvas = form.CreateCanvas())
        {
            canvas.SetFillColor(PdfColor.FromRgb(0, 0, 1));
            canvas.Rectangle(1, 1, 8, 8);
            canvas.Fill();
        }

        foreach (PdfPage page in document.Pages)
        {
            using PdfCanvas canvas = page.CreateCanvas();
            canvas.Translate(50, 50);
            canvas.DrawForm(form);
            canvas.DrawForm(form);
        }

        RecordingProcessor first = Replay(document, 0);
        RecordingProcessor second = Replay(document, 1);

        string[] expected =
        [
            "BeginForm", "q 1", "Clip Rectangle NonZero #1<#0 M 0,0 L 10,0 L 10,10 L 0,10 Z ctm=[1 0 0 1 50 50]",
            "Paint Fill NonZero M 1,1 L 9,1 L 9,9 L 1,9 Z ctm=[1 0 0 1 50 50]", "Q 0", "EndForm",
        ];
        Assert.Equal([.. expected, .. expected.Select(line => line.Replace("#1<#0", "#2<#0", StringComparison.Ordinal))], first.Semantic);
        Assert.Equal(first.Semantic, second.Semantic);
        Assert.Equal(form.Reference, Assert.Single(Subdictionary(document, document.Pages[0].Resources!, XObjectKey)).Value);
        Assert.Equal(form.Reference, Assert.Single(Subdictionary(document, document.Pages[1].Resources!, XObjectKey)).Value);
    }

    [Fact]
    public void A_canvas_on_an_existing_form_draws_after_its_content()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 10, 10));
        using (PdfCanvas canvas = form.CreateCanvas())
        {
            canvas.Scale(2, 2);
            canvas.Rectangle(0, 0, 1, 1);
            canvas.Fill();
        }

        using (PdfCanvas canvas = form.CreateCanvas())
        {
            canvas.Rectangle(0, 0, 1, 1);
            canvas.Stroke();
        }

        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawForm(form);
        }

        List<string> paints = [.. Replay(document).Semantic.Where(line => line.StartsWith("Paint", StringComparison.Ordinal))];

        Assert.Equal(
            [
                "Paint Fill NonZero M 0,0 L 1,0 L 1,1 L 0,1 Z ctm=[2 0 0 2 0 0]",
                "Paint Stroke NonZero M 0,0 L 1,0 L 1,1 L 0,1 Z ctm=[1 0 0 1 0 0]",
            ],
            paints);
    }

    [Fact]
    public void A_new_page_takes_the_boxes_and_rotation_given()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, 600, 800));
        page.CropBox = new PdfRectangle(10, 10, 590, 790);
        page.BleedBox = new PdfRectangle(5, 5, 595, 795);
        page.TrimBox = new PdfRectangle(20, 20, 580, 780);
        page.ArtBox = new PdfRectangle(30, 30, 570, 770);
        page.Rotation = 90;
        document.Pages[0].MediaBox = new PdfRectangle(0, 0, 300, 400);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));

        PdfPage read = reopened.Pages[1];
        Assert.Equal(new PdfRectangle(0, 0, 600, 800), read.MediaBox);
        Assert.Equal(new PdfRectangle(10, 10, 590, 790), read.CropBox);
        Assert.Equal(new PdfRectangle(5, 5, 595, 795), read.BleedBox);
        Assert.Equal(new PdfRectangle(20, 20, 580, 780), read.TrimBox);
        Assert.Equal(new PdfRectangle(30, 30, 570, 770), read.ArtBox);
        Assert.Equal(90, read.Rotation);
        Assert.Equal(new PdfRectangle(0, 0, 300, 400), reopened.Pages[0].MediaBox);
        Assert.Empty(reopened.Diagnostics);
        Assert.Throws<ArgumentException>(() => page.CropBox = new PdfRectangle(0, 0, 0, 10));
    }

    [Theory]
    [InlineData("blend mode", "1.3", "1.4", "transparency parameters")]
    [InlineData("fill alpha", "1.3", "1.4", "transparency parameters")]
    [InlineData("rendering intent", "1.0", "1.1", "rendering intent operator")]
    [InlineData("separation", "1.1", "1.3", "Separation colour space")]
    [InlineData("devicen", "1.2", "1.3", "DeviceN colour space")]
    [InlineData("icc", "1.2", "1.3", "ICCBased colour space")]
    [InlineData("trim box", "1.2", "1.3", "TrimBox")]
    public void A_drawn_feature_raises_an_older_document_to_its_version(string feature, string header, string raised, string named)
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-" + header));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            switch (feature)
            {
                case "blend mode":
                    canvas.SetBlendMode(BlendMode.Screen);
                    break;
                case "fill alpha":
                    canvas.SetFillAlpha(0.5);
                    break;
                case "rendering intent":
                    canvas.SetRenderingIntent(RenderingIntent.Perceptual);
                    break;
                case "separation":
                    canvas.SetFillColor(new PdfColor(document.CreateSeparationColorSpace("Spot", PdfColor.FromGray(0)), [1]));
                    break;
                case "devicen":
                    canvas.SetFillColor(new PdfColor(document.CreateDeviceNColorSpace(["A"], [PdfColor.FromGray(0)]), [1]));
                    break;
                case "icc":
                    canvas.SetFillColor(new PdfColor(document.CreateIccBasedColorSpace(RgbProfile()), [1, 1, 1]));
                    break;
                default:
                    document.Pages[0].TrimBox = new PdfRectangle(10, 10, 600, 780);
                    break;
            }

            canvas.Rectangle(0, 0, 1, 1);
            canvas.Fill();
        }

        byte[] saved = Save(document, new PdfSaveOptions().WithUpdateModificationDate(false).WithStreamCompression(PdfStreamCompression.None));

        Assert.Equal("%PDF-" + raised, System.Text.Encoding.ASCII.GetString(saved, 0, 8));
        Assert.Contains(named, Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_mode_refuses_to_save_a_blend_mode_in_a_pdf_1_3_document()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.3"), new PdfOptions().UseStrict());
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetBlendMode(BlendMode.Screen);
        }

        using var output = new MemoryStream();
        Broadside.Diagnostics.DiagnosticException exception = Assert.Throws<Broadside.Diagnostics.DiagnosticException>(() => document.Save(output));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_page_drawn_with_every_feature_passes_qpdf_check_and_renders_in_poppler_without_warnings()
    {
        byte[] saved;
        using (PdfDocument document = DrawEverything())
        {
            saved = Saved(document);
        }

        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        Assert.DoesNotContain("WARNING", output, StringComparison.OrdinalIgnoreCase);
        (int renderCode, byte[] image, string error) = ExternalTool.RunBinary("pdftoppm", saved, "-r", "36", "-f", "1", "-l", "1");
        Assert.True(renderCode == 0, error);
        Assert.Equal(string.Empty, error.Trim());
        Assert.NotEmpty(image);
        using PdfDocument reopened = PdfDocument.Open(saved, new PdfOptions().UseStrict());
        reopened.Pages[0].ProcessContent(new RecordingProcessor());
        Assert.Empty(reopened.Diagnostics);
    }

    /// <summary>A document whose page uses every canvas feature.</summary>
    internal static PdfDocument DrawEverything()
    {
        PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        page.CropBox = new PdfRectangle(10, 10, 600, 780);
        page.Rotation = 90;
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 50, 50));
        using (PdfCanvas canvas = form.CreateCanvas())
        {
            canvas.SetFillColor(PdfColor.FromCmyk(0, 1, 1, 0));
            canvas.Rectangle(5, 5, 40, 40);
            canvas.Fill();
        }

        PdfIccBasedColorSpace icc = document.CreateIccBasedColorSpace(RgbProfile());
        PdfSeparationColorSpace spot = document.CreateSeparationColorSpace("Spot Gold", PdfColor.FromCmyk(0, 0.2f, 0.8f, 0.1f));
        PdfDeviceNColorSpace inks = document.CreateDeviceNColorSpace(["Orange", "Green"], [PdfColor.FromCmyk(0, 0.5f, 1, 0), PdfColor.FromCmyk(1, 0, 1, 0)]);
        using (PdfCanvas canvas = page.CreateCanvas())
        {
            canvas.SaveState();
            canvas.Translate(100, 100);
            canvas.Rotate(15);
            canvas.SetLineWidth(3);
            canvas.SetLineCap(LineCap.Round);
            canvas.SetLineJoin(LineJoin.Round);
            canvas.SetMiterLimit(5);
            canvas.SetDashPattern([6, 3], 0);
            canvas.SetStrokeColor(new PdfColor(icc, [0.2f, 0.4f, 0.8f]));
            canvas.SetFillColor(new PdfColor(spot, [0.7f]));
            canvas.SetFillAlpha(0.6);
            canvas.SetStrokeAlpha(0.9);
            canvas.SetBlendMode(BlendMode.Multiply);
            canvas.SetRenderingIntent(RenderingIntent.Perceptual);
            canvas.SetFlatness(1);
            canvas.MoveTo(0, 0);
            canvas.LineTo(200, 0);
            canvas.CurveTo(250, 50, 250, 150, 200, 200);
            canvas.ClosePath();
            canvas.FillAndStroke();
            canvas.RestoreState();
            canvas.SaveState();
            canvas.Rectangle(300, 300, 200, 200);
            canvas.Clip();
            canvas.EndPath();
            canvas.SetFillColor(new PdfColor(inks, [1, 0.5f]));
            canvas.Rectangle(250, 250, 200, 200);
            canvas.Fill(FillRule.EvenOdd);
            canvas.RestoreState();
            canvas.Translate(400, 600);
            canvas.DrawForm(form);
        }

        return document;
    }

    /// <summary>Saves <paramref name="document"/>, reopens it and records the events of one page, without operator events.</summary>
    internal static RecordingProcessor Replay(PdfDocument document, int page = 0)
    {
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        var recorder = new RecordingProcessor(ContentEvents.All & ~ContentEvents.Operators);
        reopened.Pages[page].ProcessContent(recorder);
        Assert.Empty(reopened.Diagnostics);
        return recorder;
    }

    /// <summary>The document saved with default options.</summary>
    internal static byte[] Saved(PdfDocument document) => Save(document, new PdfSaveOptions());

    internal static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }

    /// <summary>The graphics state of each path painted on the first page of the saved and reopened document.</summary>
    private static List<GraphicsState> ReplayStates(PdfDocument document, out List<double[]> dashes)
    {
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        var recorder = new StateRecorder();
        reopened.Pages[0].ProcessContent(recorder);
        Assert.Empty(reopened.Diagnostics);
        dashes = recorder.DashArrays;
        return recorder.States;
    }

    private static Matrix Round(Matrix m) =>
        new(Math.Round(m.A, 9), Math.Round(m.B, 9), Math.Round(m.C, 9), Math.Round(m.D, 9), Math.Round(m.E, 9), Math.Round(m.F, 9));

    private static void AssertColor(in PdfColor color, PdfColorSpaceFamily family, params float[] components)
    {
        Assert.Equal(family, color.ColorSpace.Family);
        Assert.Equal(components, color.Components.ToArray());
    }

    private static float[] Evaluate(PdfFunction function, float input, int outputs) => Evaluate(function, [input], outputs);

    private static float[] Evaluate(PdfFunction function, float[] inputs, int outputs)
    {
        float[] result = new float[outputs];
        function.Evaluate(inputs, result);
        return [.. result.Select(value => MathF.Round(value, 4))];
    }

    private static Dictionary<CosName, CosObject> Subdictionary(PdfDocument document, CosDictionary resources, CosName key) =>
        new((CosDictionary)document.Resolve(resources[key]));

    /// <summary>The RGB matrix/TRC profile of the corpus file <c>icc-rgb-matrix.pdf</c>.</summary>
    internal static byte[] RgbProfile()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("icc-rgb-matrix.pdf"));
        var spaces = (CosDictionary)source.Resolve(source.Pages[0].Resources![ColorSpaceKey]);
        return spaces.Values
            .Select(value => source.GetColorSpace(value))
            .OfType<PdfIccBasedColorSpace>()
            .First(space => space.ComponentCount == 3)
            .GetProfileData()
            .ToArray();
    }
}
