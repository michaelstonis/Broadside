using System.Globalization;
using System.Text;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Replaying a region of a display list (viewer capability 1, spec #33 story 14; ADR 0006): every paint carries its bounds in
/// default user space, and <see cref="DisplayList.Replay(IRenderingBackend, PdfRectangle)"/> hands the backend only the paints
/// whose bounds meet the region, with the state changes they depend on. Observed through the recording backend.
/// </summary>
public sealed class DisplayListRegionTests
{
    [Fact]
    public void A_region_meeting_one_percent_of_the_paints_visits_only_those_paints()
    {
        // 10,000 squares 4 units wide on a 6 x 7 grid; the region holds columns 10-19 and rows 30-39 and touches no other square.
        var content = new StringBuilder("0 0 1 rg\n");
        for (int row = 0; row < 100; row++)
        {
            for (int column = 0; column < 100; column++)
            {
                content.Append(CultureInfo.InvariantCulture, $"{(6 * column) + 1} {(7 * row) + 1} 4 4 re f\n");
            }
        }

        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.ASCII.GetBytes(content.ToString())));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var recorder = new RecordingBackend();

        list.Replay(recorder, new PdfRectangle(60, 210, 120, 280));

        var expected = new List<string>();
        for (int row = 30; row < 40; row++)
        {
            for (int column = 10; column < 20; column++)
            {
                int x = (6 * column) + 1;
                int y = (7 * row) + 1;
                expected.Add($"fill nonzero M {x} {y} L {x + 4} {y} L {x + 4} {y + 4} L {x} {y + 4} Z | DeviceRgb 0 0 1");
            }
        }

        Assert.Equal(expected, recorder.Operations);
    }

    [Fact]
    public void A_stroke_counts_as_wide_as_its_line_width_reaches()
    {
        // The region is 8 to 12 units above both lines: the 20-unit line reaches 10 units up into it, the 1-unit line half a unit.
        using PdfDocument document = PdfDocument.Open(Page("<< >>", "20 w 0 0 m 50 0 l S 1 w 100 0 m 150 0 l S"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder, new PdfRectangle(0, 8, 200, 12));

        Assert.Equal(["stroke 20 Butt Miter 10 [] 0 M 0 0 L 50 0 | DeviceGray 0"], recorder.Operations);
    }

    [Fact]
    public void A_miter_join_or_a_square_cap_widens_the_bounds_of_a_stroke()
    {
        // A 10-unit line ends at x = 50: a round join or cap reaches 5 units round it, a square cap √2 × 5 = 7.07 at its corners.
        using PdfDocument document = PdfDocument.Open(Page("<< >>", "10 w 0 J 0 0 m 50 0 l S 2 J 0 100 m 50 100 l S"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder, new PdfRectangle(56, -200, 60, 200));

        // A miter limit of 10 lets a join reach 50 units out, so with the default miter join both strokes are visited...
        Assert.Equal(2, recorder.Operations.Count);

        using PdfDocument rounded = PdfDocument.Open(Page("<< >>", "10 w 1 j 0 J 0 0 m 50 0 l S 2 J 0 100 m 50 100 l S"));
        var roundRecorder = new RecordingBackend();

        rounded.Pages[0].CreateDisplayList().Replay(roundRecorder, new PdfRectangle(56, -200, 60, 200));

        // ...and with round joins only the square cap reaches the region.
        Assert.Equal(["stroke 10 ProjectingSquare Round 10 [] 0 M 0 100 L 50 100 | DeviceGray 0"], roundRecorder.Operations);
    }

    [Fact]
    public void Content_clipped_away_from_the_region_is_skipped_with_its_saved_state()
    {
        // The first fill covers the region but its clip keeps it in the 10-unit square at the origin.
        using PdfDocument document = PdfDocument.Open(Page("<< >>", "q 0 0 10 10 re W n 0 g 0 0 500 500 re f Q 1 g 0 0 500 500 re f"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder, new PdfRectangle(200, 200, 210, 210));

        Assert.Equal(["fill nonzero M 0 0 L 500 0 L 500 500 L 0 500 Z | DeviceGray 1"], recorder.Operations);
    }

    [Fact]
    public void State_changes_reach_the_backend_even_when_the_paints_after_them_are_skipped()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", "2 0 0 2 0 0 cm 0 g 0 0 10 10 re f 100 100 10 10 re f q 0.5 g 0 0 5 5 re W n 100 100 10 10 re f Q"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder, new PdfRectangle(200, 200, 210, 210));

        // The square at the origin (0..20 on the page) is skipped, the one at 200..220 is not; the saved state clipped to 0..10 is.
        Assert.Equal(["transform [2 0 0 2 0 0]", "fill nonzero M 100 100 L 110 100 L 110 110 L 100 110 Z | DeviceGray 0"], recorder.Operations);
    }

    [Fact]
    public void A_shading_without_a_bounding_box_is_bounded_by_its_clip_only()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("shading-type2-axial.pdf"));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var everything = new RecordingBackend();
        list.Replay(everything);
        Assert.Contains(everything.Operations, line => line.StartsWith("shading", StringComparison.Ordinal));
        var far = new RecordingBackend();

        list.Replay(far, new PdfRectangle(-1e6, -1e6, -1e6 + 1, -1e6 + 1));

        Assert.DoesNotContain(far.Operations, line => line.StartsWith("shading", StringComparison.Ordinal) || line.StartsWith("fill", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void A_region_that_holds_everything_replays_every_operation(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        foreach (PdfPage page in document.Pages)
        {
            DisplayList list = page.CreateDisplayList();
            var all = new RecordingBackend();
            var region = new RecordingBackend();
            list.Replay(all);

            list.Replay(region, new PdfRectangle(double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity));

            Assert.Equal(all.Operations, region.Operations);
        }
    }

    private static byte[] Page(string resources, string content) => Colors.ColorTesting.Page(resources, content);
}
