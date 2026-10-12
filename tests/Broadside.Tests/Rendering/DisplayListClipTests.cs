using Broadside.Rendering;
using Broadside.TestSupport;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Clips in the display list (ISO 32000-2 §8.5.4, §8.10.1; ADR 0006): <c>W</c> and <c>W*</c> reach the backend as
/// <see cref="IRenderingBackend.IntersectClip"/> after the paint of their path object, under the transform of the path, inside the
/// saved state that <c>Q</c> restores; a form's bounding box clips its content.
/// </summary>
public sealed class DisplayListClipTests
{
    [Fact]
    public void A_clip_reaches_the_backend_after_the_paint_of_its_path_object_so_it_applies_to_later_paints_only()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", "0 0 10 10 re W f 2 0 0 2 0 0 cm 0 0 5 5 re W* n 1 g 0 0 20 20 re f"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            [
                "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 0",
                "clip nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z",
                "transform [2 0 0 2 0 0]",
                "clip evenodd M 0 0 L 5 0 L 5 5 L 0 5 Z",
                "fill nonzero M 0 0 L 20 0 L 20 20 L 0 20 Z | DeviceGray 1",
            ],
            recorder.Operations);
    }

    [Fact]
    public void A_clip_with_nothing_painted_before_its_state_is_restored_is_dropped_with_the_save()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", "q 2 0 0 2 0 0 cm 0 0 5 5 re W n q 1 1 1 1 re W* n Q Q 0 0 1 1 re f"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(["fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0"], recorder.Operations);
    }

    [Fact]
    public void Each_form_clips_its_content_to_its_bounding_box_in_form_space_within_the_clip_of_the_form_that_paints_it()
    {
        using PdfDocument document = PdfDocument.Open(Page(
            "<< /XObject << /FA 5 0 R >> >>",
            "/FA Do",
            ContentSamples.Stream("0 0 100 100 re f /FB Do", "/Type /XObject /Subtype /Form /BBox [0 0 50 50] /Matrix [2 0 0 2 100 100] /Resources << /XObject << /FB 6 0 R >> >>"),
            ContentSamples.Stream("1 g 0 0 100 100 re f", "/Type /XObject /Subtype /Form /BBox [10 10 20 20]")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            [
                "save",
                "transform [2 0 0 2 100 100]",
                "clip nonzero M 0 0 L 50 0 L 50 50 L 0 50 Z",
                "fill nonzero M 0 0 L 100 0 L 100 100 L 0 100 Z | DeviceGray 0",
                "save",
                "clip nonzero M 10 10 L 20 10 L 20 20 L 10 20 Z",
                "fill nonzero M 0 0 L 100 0 L 100 100 L 0 100 Z | DeviceGray 1",
                "restore",
                "restore",
            ],
            recorder.Operations);
    }

    [Fact]
    public void Form_xobject_nested_pdf_paints_form_a_inside_the_bounding_box_of_form_a()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("form-xobject-nested.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // The clips in force at each paint, following saves and restores. Form A (BBox [0 0 50 50] under [2 0 0 2 100 100]) fills
        // a 10 x 10 rectangle; forms B and C paint only text and strokes, which later tickets bring to the list.
        List<(string Paint, string[] Clips)> paints = ClipsAtEachPaint(recorder.Operations);
        (string paint, string[] clips) = Assert.Single(paints, static entry => entry.Paint.StartsWith("fill", StringComparison.Ordinal));
        Assert.Equal("fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 0", paint);
        Assert.Equal(["transform [2 0 0 2 100 100] clip nonzero M 0 0 L 50 0 L 50 50 L 0 50 Z"], clips);
    }

    /// <summary>Replays recorded lines with a clip stack: for every paint, the clips in force with the transform each was set under.</summary>
    private static List<(string Paint, string[] Clips)> ClipsAtEachPaint(List<string> operations)
    {
        var paints = new List<(string, string[])>();
        var saved = new Stack<(List<string> Clips, string Transform)>();
        var clips = new List<string>();
        string transform = "transform [1 0 0 1 0 0]";
        foreach (string line in operations)
        {
            if (line == "save")
            {
                saved.Push(([.. clips], transform));
            }
            else if (line == "restore")
            {
                (clips, transform) = saved.Pop();
            }
            else if (line.StartsWith("transform ", StringComparison.Ordinal))
            {
                transform = line;
            }
            else if (line.StartsWith("clip ", StringComparison.Ordinal))
            {
                clips.Add(transform + " " + line);
            }
            else
            {
                paints.Add((line, [.. clips]));
            }
        }

        return paints;
    }
}
