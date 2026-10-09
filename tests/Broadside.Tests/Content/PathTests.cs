using Broadside.Content;

namespace Broadside.Tests.Content;

/// <summary>
/// Path construction and painting operators drive <see cref="ContentProcessor.PaintPath"/> with the path in user space, as
/// Tables 58 and 59 define them. ISO 32000-2 §8.5.2, §8.5.3.
/// </summary>
public class PathTests
{
    [Fact]
    public void A_stroked_polyline_is_reported_in_user_space_with_the_identity_ctm()
    {
        (RecordingProcessor events, _) = ContentPdf.Run("10 20 m 30 40 l 50 20 l S", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 10,20 L 30,40 L 50,20 ctm=[1 0 0 1 0 0]"], events.Body);
    }

    [Fact]
    public void A_rectangle_is_a_moveto_three_lines_and_a_close_keeping_the_signs_of_its_size()
    {
        (RecordingProcessor events, _) = ContentPdf.Run("100 200 -50 30 re f", ContentEvents.Paths);

        Assert.Equal(["Paint Fill NonZero M 100,200 L 50,200 L 50,230 L 100,230 Z ctm=[1 0 0 1 0 0]"], events.Body);
    }

    [Fact]
    public void The_v_and_y_curves_replicate_the_current_point_and_the_end_point()
    {
        (RecordingProcessor events, _) = ContentPdf.Run("0 0 m 1 2 3 4 v 5 6 7 8 y 1 1 2 2 3 3 c n", ContentEvents.Paths);

        Assert.Equal(["Paint None NonZero M 0,0 C 0,0 1,2 3,4 C 5,6 7,8 7,8 C 1,1 2,2 3,3 ctm=[1 0 0 1 0 0]"], events.Body);
    }

    [Fact]
    public void A_moveto_after_a_moveto_replaces_it()
    {
        (RecordingProcessor events, _) = ContentPdf.Run("1 1 m 2 2 m 3 3 l S", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 2,2 L 3,3 ctm=[1 0 0 1 0 0]"], events.Body);
    }

    [Fact]
    public void A_segment_after_a_close_starts_a_new_subpath_at_the_closed_subpaths_first_point()
    {
        (RecordingProcessor events, _) = ContentPdf.Run("0 0 m 10 0 l h h 10 10 l S", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 0,0 L 10,0 Z M 0,0 L 10,10 ctm=[1 0 0 1 0 0]"], events.Body);
    }

    [Fact]
    public void A_trailing_lone_moveto_is_kept_for_the_renderer_to_ignore()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("0 0 m 10 0 l 5 5 m f", ContentEvents.Paths);

        Assert.Equal(["Paint Fill NonZero M 0,0 L 10,0 M 5,5 ctm=[1 0 0 1 0 0]"], events.Body);
        Assert.Empty(diagnostics);
    }

    public static TheoryData<string, string> PaintingOperators => new()
    {
        { "S", "Paint Stroke NonZero M 0,0 L 1,0 L 1,1 ctm=[1 0 0 1 0 0]" },
        { "s", "Paint Stroke NonZero closed M 0,0 L 1,0 L 1,1 Z ctm=[1 0 0 1 0 0]" },
        { "f", "Paint Fill NonZero M 0,0 L 1,0 L 1,1 ctm=[1 0 0 1 0 0]" },
        { "F", "Paint Fill NonZero M 0,0 L 1,0 L 1,1 ctm=[1 0 0 1 0 0]" },
        { "f*", "Paint Fill EvenOdd M 0,0 L 1,0 L 1,1 ctm=[1 0 0 1 0 0]" },
        { "B", "Paint FillAndStroke NonZero M 0,0 L 1,0 L 1,1 ctm=[1 0 0 1 0 0]" },
        { "B*", "Paint FillAndStroke EvenOdd M 0,0 L 1,0 L 1,1 ctm=[1 0 0 1 0 0]" },
        { "b", "Paint FillAndStroke NonZero closed M 0,0 L 1,0 L 1,1 Z ctm=[1 0 0 1 0 0]" },
        { "b*", "Paint FillAndStroke EvenOdd closed M 0,0 L 1,0 L 1,1 Z ctm=[1 0 0 1 0 0]" },
        { "n", "Paint None NonZero M 0,0 L 1,0 L 1,1 ctm=[1 0 0 1 0 0]" },
    };

    [Theory]
    [MemberData(nameof(PaintingOperators))]
    public void Each_painting_operator_reports_how_it_paints(string op, string expected)
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run($"0 0 m 1 0 l 1 1 l {op}", ContentEvents.Paths);

        Assert.Equal([expected], events.Body);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_painting_operator_ends_the_path_so_a_second_paint_has_no_path()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("0 0 m 1 1 l S f", ContentEvents.Paths);

        Assert.Single(events.Body);
        Assert.Equal(["ContentNoCurrentPath"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Painting_without_a_path_paints_nothing_and_is_recorded()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("S", ContentEvents.Paths);

        Assert.Empty(events.Body);
        Assert.Equal(["ContentNoCurrentPath"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void A_lineto_without_a_current_point_begins_a_subpath_at_its_end_point()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("5 5 l 10 10 l S", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 5,5 L 10,10 ctm=[1 0 0 1 0 0]"], events.Body);
        Assert.Equal(["ContentNoCurrentPoint"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void The_current_path_survives_q_and_q_does_not_save_it()
    {
        (RecordingProcessor events, _) = ContentPdf.Run("0 0 m q 1 1 l Q 2 2 l S", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 0,0 L 1,1 L 2,2 ctm=[1 0 0 1 0 0]"], events.Body);
    }

    [Fact]
    public void Coordinates_keep_double_precision()
    {
        (RecordingProcessor events, _) = ContentPdf.Run("1000000.125 2000000.0625 m 1000000.25 2000000.125 l S", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 1000000.125,2000000.0625 L 1000000.25,2000000.125 ctm=[1 0 0 1 0 0]"], events.Body);
    }

    [Fact]
    public void A_path_left_unpainted_at_the_end_is_discarded_with_an_information_diagnostic()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("0 0 m 1 1 l", ContentEvents.Paths);

        Assert.Empty(events.Body);
        Assert.Equal(["ContentPathNotPainted"], ContentPdf.Codes(diagnostics));
        Assert.Equal(Broadside.Diagnostics.DiagnosticSeverity.Information, diagnostics[0].Severity);
    }

    [Fact]
    public void Without_path_or_clip_events_no_geometry_is_reported_but_the_same_diagnostics_are()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("5 5 l S", ContentEvents.Text);

        Assert.DoesNotContain(events.Semantic, line => line.StartsWith("Paint", StringComparison.Ordinal));
        Assert.Equal(["ContentNoCurrentPoint"], ContentPdf.Codes(diagnostics));
    }
}
