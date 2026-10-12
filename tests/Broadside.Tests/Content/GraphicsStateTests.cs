using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Tests.Content;

/// <summary>
/// The graphics state operators of Table 56 set the state processors read at each paint; <c>q</c> and <c>Q</c> save and restore
/// it. ISO 32000-2 §8.4.1 to §8.4.4.
/// </summary>
public sealed class GraphicsStateTests
{
    [Fact]
    public void A_path_is_painted_with_the_initial_state_of_tables_51_and_52()
    {
        (StateRecorder recorder, _) = StateRecorder.Run("0 0 m 1 1 l S");

        GraphicsState state = Assert.Single(recorder.States);
        Assert.Equal(Matrix.Identity, state.Ctm);
        Assert.Equal(1.0, state.LineWidth);
        Assert.Equal(LineCap.Butt, state.LineCap);
        Assert.Equal(LineJoin.Miter, state.LineJoin);
        Assert.Equal(10.0, state.MiterLimit);
        Assert.Empty(recorder.DashArrays[0]);
        Assert.Equal(0, state.DashPhase);
        Assert.Equal(RenderingIntent.RelativeColorimetric, state.RenderingIntent);
        Assert.Equal(1.0, state.Flatness);
        Assert.Equal(0, state.ClipHandle);
        Assert.Equal(BlendMode.Normal, state.BlendMode);
        Assert.Equal(1.0, state.StrokeAlpha);
        Assert.Equal(1.0, state.FillAlpha);
        Assert.Equal(1.0, state.HorizontalScaling);
        Assert.True(state.TextKnockout);
    }

    [Fact]
    public void Each_operator_of_table_56_sets_its_parameter()
    {
        (StateRecorder recorder, var diagnostics) = StateRecorder.Run("2.5 w 1 J 2 j 4 M [3 1] 2 d /Perceptual ri 50 i 0 0 m 1 1 l S");

        GraphicsState state = Assert.Single(recorder.States);
        Assert.Equal(2.5, state.LineWidth);
        Assert.Equal(LineCap.Round, state.LineCap);
        Assert.Equal(LineJoin.Bevel, state.LineJoin);
        Assert.Equal(4, state.MiterLimit);
        Assert.Equal([3.0, 1.0], recorder.DashArrays[0]);
        Assert.Equal(2, state.DashPhase);
        Assert.Equal(RenderingIntent.Perceptual, state.RenderingIntent);
        Assert.Equal(50, state.Flatness);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Cm_premultiplies_the_ctm()
    {
        // §8.3.4: M′ = Mcm × CTM. A translation then a scale: the scale applies first to the coordinates.
        (StateRecorder recorder, _) = StateRecorder.Run("1 0 0 1 100 200 cm 2 0 0 3 0 0 cm 0 0 m 1 1 l S");

        Assert.Equal(new Matrix(2, 0, 0, 3, 100, 200), Assert.Single(recorder.States).Ctm);
    }

    [Fact]
    public void A_singular_matrix_is_concatenated_as_given()
    {
        (StateRecorder recorder, var diagnostics) = StateRecorder.Run("0 0 0 0 5 5 cm 0 0 m 1 1 l S");

        Assert.Equal(new Matrix(0, 0, 0, 0, 5, 5), Assert.Single(recorder.States).Ctm);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Q_restores_the_whole_state_saved_by_q()
    {
        (StateRecorder recorder, _) = StateRecorder.Run("q 2 0 0 2 0 0 cm 5 w [2] 0 d 0 0 m 1 1 l S Q 0 0 m 1 1 l S");

        Assert.Equal(2, recorder.States.Count);
        Assert.Equal(new Matrix(2, 0, 0, 2, 0, 0), recorder.States[0].Ctm);
        Assert.Equal(5, recorder.States[0].LineWidth);
        Assert.Equal([2.0], recorder.DashArrays[0]);
        Assert.Equal(Matrix.Identity, recorder.States[1].Ctm);
        Assert.Equal(1, recorder.States[1].LineWidth);
        Assert.Empty(recorder.DashArrays[1]);
    }

    [Fact]
    public void State_stack_events_follow_q_and_q_with_the_depth_after_each()
    {
        (RecordingProcessor events, _) = ContentPdf.Run("q q Q Q", ContentEvents.StateStack);

        Assert.Equal(["q 1", "q 2", "Q 1", "Q 0"], events.Body);
    }

    [Fact]
    public void Q_without_q_is_ignored_and_recorded()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("Q q Q", ContentEvents.StateStack);

        Assert.Equal(["q 1", "Q 0"], events.Body);
        Assert.Equal(["ContentStackUnderflow"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Unbalanced_q_at_the_end_is_restored_with_implicit_restore_events()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("q q 0 0 m", ContentEvents.StateStack);

        Assert.Equal(["q 1", "q 2", "Q 1", "Q 0"], events.Body);
        Assert.Equal(["ContentPathNotPainted", "ContentUnbalancedSave"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void A_contents_array_is_one_stream_for_q_and_q()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run(["q 2 0 0 2 0 0 cm", "0 0 m 1 1 l S Q"], ContentEvents.StateStack | ContentEvents.Paths);

        Assert.Equal(["q 1", "Paint Stroke NonZero M 0,0 L 1,1 ctm=[2 0 0 2 0 0]", "Q 0"], events.Body);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_q_beyond_the_depth_limit_is_ignored_with_its_q()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q q q 2 w Q Q Q 0 0 m 1 1 l S"));
        var recorder = new StateRecorder();

        document.Pages[0].ProcessContent(recorder, new ContentOptions().WithMaxSaveDepth(2));

        Assert.Equal(1, Assert.Single(recorder.States).LineWidth);
        Assert.Equal(["ContentStackOverflow"], ContentPdf.Codes(document.Diagnostics));
    }

    public static TheoryData<string, double> NegativeLineWidths => new() { { "-3 w", 3 }, { "0 w", 0 } };

    [Theory]
    [MemberData(nameof(NegativeLineWidths))]
    public void A_negative_line_width_uses_its_absolute_value(string op, double expected)
    {
        (StateRecorder recorder, var diagnostics) = StateRecorder.Run($"{op} 0 0 m 1 1 l S");

        Assert.Equal(expected, Assert.Single(recorder.States).LineWidth);
        Assert.Equal(expected > 0 ? ["ContentGraphicsStateRange"] : [], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Out_of_range_caps_joins_miter_limits_and_flatness_are_clipped_into_range()
    {
        (StateRecorder recorder, var diagnostics) = StateRecorder.Run("3 J 1.5 j 0.5 M 150 i 0 0 m 1 1 l S");

        GraphicsState state = Assert.Single(recorder.States);
        Assert.Equal(LineCap.Butt, state.LineCap);
        Assert.Equal(LineJoin.Miter, state.LineJoin);
        Assert.Equal(1, state.MiterLimit);
        Assert.Equal(100, state.Flatness);
        Assert.Equal(["ContentGraphicsStateRange"], ContentPdf.Codes(diagnostics));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostics[0].Severity);
    }

    public static TheoryData<string, double[], double, bool> DashPatterns => new()
    {
        // Table 55 examples, then §8.4.3.6: a negative phase is incremented by twice the sum until it is not negative.
        { "[] 0", [], 0, false },
        { "[3] 0", [3], 0, false },
        { "[2 1 3] -2", [2, 1, 3], 10, false },
        { "[2 1] -13", [2, 1], 5, false },
        { "[] 4", [], 0, false },
        { "[1 2 3 4 5 6 7 8 9 10] 1", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 1, false },
        // Elements shall be non-negative and not all zero: otherwise a solid line, recorded.
        { "[0 0] 0", [], 0, true },
        { "[2 -1] 0", [], 0, true },
    };

    [Theory]
    [MemberData(nameof(DashPatterns))]
    public void Dash_patterns_are_normalized_as_8_4_3_6_says(string operands, double[] array, double phase, bool repaired)
    {
        (StateRecorder recorder, var diagnostics) = StateRecorder.Run($"{operands} d 0 0 m 1 1 l S");

        Assert.Equal(array, recorder.DashArrays[0]);
        Assert.Equal(phase, recorder.States[0].DashPhase);
        Assert.Equal(repaired ? ["ContentGraphicsStateRange"] : [], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void An_unrecognized_rendering_intent_selects_relative_colorimetric()
    {
        (StateRecorder recorder, var diagnostics) = StateRecorder.Run("/Saturation ri /Vivid ri 0 0 m 1 1 l S");

        Assert.Equal(RenderingIntent.RelativeColorimetric, Assert.Single(recorder.States).RenderingIntent);
        Assert.Empty(diagnostics);
    }
}
