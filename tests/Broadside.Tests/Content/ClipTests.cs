using Broadside.Content;

namespace Broadside.Tests.Content;

/// <summary>
/// <c>W</c> and <c>W*</c> narrow the clip after the painting operator of their path object, never before it, and <c>Q</c> brings
/// the earlier clip back. ISO 32000-2 §8.5.4, Table 60.
/// </summary>
public sealed class ClipTests
{
    private const ContentEvents PathsAndClips = ContentEvents.Paths | ContentEvents.Clips;

    [Fact]
    public void W_s_strokes_the_path_unclipped_by_itself_and_clips_after_the_stroke()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("0 0 m 10 0 l 10 10 l h W S", PathsAndClips);

        Assert.Equal(
            [
                "Paint Stroke NonZero clip=NonZero M 0,0 L 10,0 L 10,10 Z ctm=[1 0 0 1 0 0]",
                "Clip Path NonZero #1<#0 M 0,0 L 10,0 L 10,10 Z ctm=[1 0 0 1 0 0]",
            ],
            events.Body);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void The_state_at_the_paint_still_holds_the_old_clip_and_after_it_the_new_one()
    {
        var probe = new ClipProbe();
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("0 0 10 10 re W* f 0 0 m 5 5 l S"));

        document.Pages[0].ProcessContent(probe);

        Assert.Equal([0, 1], probe.ClipAtPaint);
        Assert.Equal([1], probe.ClipInEvent);
    }

    [Fact]
    public void W_n_only_intersects_the_clip()
    {
        (RecordingProcessor events, _) = ContentPdf.Run("2 0 0 2 0 0 cm 0 0 5 5 re W* n", PathsAndClips);

        Assert.Equal(
            [
                "Paint None NonZero clip=EvenOdd M 0,0 L 5,0 L 5,5 L 0,5 Z ctm=[2 0 0 2 0 0]",
                "Clip Path EvenOdd #1<#0 M 0,0 L 5,0 L 5,5 L 0,5 Z ctm=[2 0 0 2 0 0]",
            ],
            events.Body);
    }

    [Fact]
    public void Nested_clips_chain_to_their_parents_and_q_restores_the_earlier_clip()
    {
        var probe = new ClipProbe();
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(
            "0 0 100 100 re W n q 10 10 50 50 re W n 0 0 m 1 1 l S Q 0 0 m 1 1 l S"));

        document.Pages[0].ProcessContent(probe);

        Assert.Equal([1, 2, 1], probe.ClipAtPaint.Where((_, index) => index > 0));
        Assert.Equal([0, 1], probe.Parents);
        Assert.Equal([ClipKind.Path, ClipKind.Initial], probe.Chain);
    }

    [Fact]
    public void W_without_a_path_is_ignored()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("W n", PathsAndClips);

        Assert.Empty(events.Body);
        Assert.Equal(["ContentNoCurrentPath"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Without_clip_events_the_clip_handle_stays_at_the_initial_clip()
    {
        var probe = new ClipProbe(ContentEvents.Paths);
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("0 0 10 10 re W n 0 0 m 1 1 l S"));

        document.Pages[0].ProcessContent(probe);

        Assert.Equal([0, 0], probe.ClipAtPaint);
        Assert.Empty(probe.ClipInEvent);
    }
}

/// <summary>Records clip handles at each paint and clip event, and the chain of the last paint's clip.</summary>
internal sealed class ClipProbe(ContentEvents events = ContentEvents.Paths | ContentEvents.Clips) : ContentProcessor
{
    public List<int> ClipAtPaint { get; } = [];

    public List<int> ClipInEvent { get; } = [];

    public List<int> Parents { get; } = [];

    public List<ClipKind> Chain { get; } = [];

    public override ContentEvents Events => events;

    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        ClipAtPaint.Add(context.State.ClipHandle);
        Chain.Clear();
        for (int handle = context.State.ClipHandle; ; handle = context.GetClip(handle).ParentHandle)
        {
            ClipView clip = context.GetClip(handle);
            Chain.Add(clip.Kind);
            if (handle == 0)
            {
                break;
            }
        }
    }

    public override void IntersectClip(in ClipEvent clip, ContentContext context)
    {
        Assert.Equal(clip.Handle, context.State.ClipHandle);
        ClipInEvent.Add(clip.Handle);
        Parents.Add(clip.ParentHandle);
        Assert.Equal(clip.Rule, context.GetClip(clip.Handle).Rule);
    }
}
