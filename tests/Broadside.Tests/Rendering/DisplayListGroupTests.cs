using System.Text;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Transparency groups in the display list (ISO 32000-2 §11.4, §11.6.6, §11.7.4.4; ADR 0006): a transparency group XObject, a page
/// group and the implicit knockout group of a combined fill and stroke reach the backend through
/// <see cref="IRenderingBackend.DrawGroup"/>, carrying the group's attributes and the alpha and blend mode it composites with, and
/// its content replays inside it.
/// </summary>
public sealed class DisplayListGroupTests
{
    private const string States =
        "/ExtGState << /Half << /CA 0.5 /ca 0.25 /BM /Multiply >> /Stroke << /CA 0.5 >> /Screen << /BM /Screen >> >>";

    [Fact]
    public void A_cancellable_or_sliced_replay_hands_a_groups_content_over_once_through_the_group()
    {
        // #97's cancellable and progressive replays walk the list themselves: a DrawGroup's content must be skipped there too, or
        // the backend would get it twice (once from DisplayGroup.Replay, once inline).
        using PdfDocument document = PdfDocument.Open(Corpus.Path("transparency-groups.pdf"));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var plain = new RecordingBackend();
        list.Replay(plain);
        Assert.NotEmpty(plain.Groups);

        var cancellable = new RecordingBackend();
        using var source = new CancellationTokenSource();
        list.Replay(cancellable, source.Token, progress: null);
        var sliced = new RecordingBackend();
        DisplayListReplay replay = list.BeginReplay(sliced, source.Token, progress: null);
        while (!replay.Continue(TimeSpan.Zero))
        {
        }

        Assert.Equal(plain.Operations, cancellable.Operations);
        Assert.Equal(plain.Operations, sliced.Operations);
    }

    [Fact]
    public void A_transparency_group_form_paints_as_a_group_with_the_alpha_and_blend_mode_at_Do()
    {
        List<string> operations = Record(
            "q 1 0 0 1 100 100 cm /Half gs /Fm0 Do Q",
            "/XObject << /Fm0 5 0 R >>",
            Form("/Group << /S /Transparency /I true /K true /CS /DeviceCMYK >> /BBox [0 0 10 10]", "0 0 10 10 re f"));

        // Inside the group the alphas and blend mode start from their initial values (§11.6.6), so the fill needs no compositing
        // record: the backend's compositing is the one before the group's.
        Assert.Equal(
            [
                "save",
                "group Transparency isolated knockout DeviceCmyk opacity 0.25 Multiply bbox [0 0 10 10] [1 0 0 1 100 100]",
                "save",
                "transform [1 0 0 1 100 100]",
                "clip nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z",
                "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 0",
                "restore",
                "end group",
                "restore",
            ],
            operations);
    }

    [Fact]
    public void Paints_after_a_group_get_back_the_compositing_they_had_before_it()
    {
        List<string> operations = Record(
            "/Half gs 0 0 1 1 re f /Fm0 Do 0 0 2 2 re f",
            "/XObject << /Fm0 5 0 R >>",
            Form("/Group << /S /Transparency >> /BBox [0 0 10 10]", "0 0 10 10 re f"));

        Assert.Equal(
            [
                "compositing 0.5 0.25 Multiply",
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "group Transparency opacity 0.25 Multiply bbox [0 0 10 10] [1 0 0 1 0 0]",
                "save",
                "clip nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z",
                "compositing 1 1 Normal",
                "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 0",
                "restore",
                "end group",
                "fill nonzero M 0 0 L 2 0 L 2 2 L 0 2 Z | DeviceGray 0",
            ],
            operations);
    }

    [Fact]
    public void A_group_that_paints_nothing_is_not_in_the_list()
    {
        List<string> operations = Record(
            "/Fm0 Do /Fm1 Do",
            "/XObject << /Fm0 5 0 R /Fm1 6 0 R >>",
            Form("/Group << /S /Transparency >> /BBox [0 0 10 10]", "0 0 10 10 re n"),
            Form("/Group << /S /Transparency >> /BBox [0 0 10 10]", "/Fm0 Do", "/XObject << /Fm0 5 0 R >>"));

        Assert.Empty(operations);
    }

    [Fact]
    public void A_form_whose_group_is_not_a_transparency_group_paints_without_one()
    {
        List<string> operations = Record(
            "/Fm0 Do",
            "/XObject << /Fm0 5 0 R >>",
            Form("/Group << /S /Other >> /BBox [0 0 10 10]", "0 0 10 10 re f"));

        Assert.DoesNotContain(operations, line => line.StartsWith("group", StringComparison.Ordinal));
        Assert.Contains("fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 0", operations);
    }

    [Fact]
    public void A_group_colour_space_counts_only_for_an_isolated_group()
    {
        List<string> operations = Record(
            "/Fm0 Do",
            "/XObject << /Fm0 5 0 R >>",
            Form("/Group << /S /Transparency /CS /DeviceGray >> /BBox [0 0 10 10]", "0 0 10 10 re f"));

        // §11.6.6: a non-isolated group's colour space is its parent's.
        Assert.Equal("group Transparency bbox [0 0 10 10] [1 0 0 1 0 0]", operations[0]);
    }

    [Fact]
    public void Nested_groups_replay_inside_their_parent()
    {
        List<string> operations = Record(
            "/Fm1 Do",
            "/XObject << /Fm1 6 0 R >>",
            Form("/Group << /S /Transparency /I true >> /BBox [0 0 5 5]", "0 0 5 5 re f"),
            Form("/Group << /S /Transparency /K true >> /BBox [0 0 10 10]", "/Screen gs /Fm0 Do", "/XObject << /Fm0 5 0 R >> " + States));

        Assert.Equal(
            [
                "group Transparency knockout bbox [0 0 10 10] [1 0 0 1 0 0]",
                "save",
                "clip nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z",
                "group Transparency isolated Screen bbox [0 0 5 5] [1 0 0 1 0 0]",
                "save",
                "clip nonzero M 0 0 L 5 0 L 5 5 L 0 5 Z",
                "fill nonzero M 0 0 L 5 0 L 5 5 L 0 5 Z | DeviceGray 0",
                "restore",
                "end group",
                "restore",
                "end group",
            ],
            operations);
    }

    [Fact]
    public void A_page_with_a_transparency_group_paints_its_content_in_an_isolated_page_group()
    {
        byte[] file = ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Group << /S /Transparency /CS /DeviceCMYK /K true >> /Contents 4 0 R >>",
            ContentSamples.Stream("0 0 1 1 re f"),
        ]);
        using PdfDocument document = PdfDocument.Open(file);
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // §11.4.7 and Table 145: the page group is isolated whatever its I says, and its CS is honoured.
        Assert.Equal(
            [
                "group Page isolated knockout DeviceCmyk",
                "save",
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "restore",
                "end group",
            ],
            recorder.Operations);
    }

    [Fact]
    public void A_translucent_fill_and_stroke_of_one_path_paint_as_one_implicit_knockout_group()
    {
        List<string> operations = Record("/Stroke gs 4 w 10 10 m 20 10 l 20 20 l b");

        // §11.7.4.4: a non-isolated knockout group composited at alpha 1 with Normal, the fill and stroke inside it with their own
        // alphas. The bounds hold the path and the stroke's reach.
        Assert.Equal(
            [
                "group FillAndStroke knockout bbox [-10 -10 40 40] [1 0 0 1 0 0]",
                "save",
                "compositing 0.5 1 Normal",
                "fill nonzero M 10 10 L 20 10 L 20 20 Z | DeviceGray 0",
                "stroke 4 Butt Miter 10 [] 0 M 10 10 L 20 10 L 20 20 Z | DeviceGray 0",
                "restore",
                "end group",
            ],
            operations);
    }

    [Fact]
    public void An_opaque_fill_and_stroke_in_the_normal_blend_mode_needs_no_group()
    {
        List<string> operations = Record("4 w 10 10 m 20 10 l 20 20 l b");

        Assert.Equal(
            [
                "fill nonzero M 10 10 L 20 10 L 20 20 Z | DeviceGray 0",
                "stroke 4 Butt Miter 10 [] 0 M 10 10 L 20 10 L 20 20 Z | DeviceGray 0",
            ],
            operations);
    }

    [Fact]
    public void Each_translucent_glyph_filled_and_stroked_is_its_own_implicit_knockout_group()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Encoding.Latin1.GetBytes("/Stroke gs BT /F1 10 Tf 2 Tr 100 100 Td (BD) Tj ET"), States));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // Glyph B's box runs from (50, 0) to (280, 460) in glyph space: (100.5, 100) to (102.8, 104.6) on the page, widened by half
        // the line width times the miter limit.
        List<string> operations = recorder.Operations;
        Assert.Equal(2, operations.Count(line => line.StartsWith("group FillAndStroke knockout bbox [", StringComparison.Ordinal)));
        Assert.Equal(2, operations.Count(line => line.StartsWith("glyphs FillStroke", StringComparison.Ordinal)));
        string[] box = operations[0]["group FillAndStroke knockout bbox [".Length..].Split(']')[0].Split(' ');
        double[] bounds = [.. box.Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture))];
        Assert.Equal([95.5, 95, 107.8, 109.6], bounds, (expected, actual) => Math.Abs(expected - actual) < 1e-9);
    }

    [Fact]
    public void A_group_colour_space_that_cannot_blend_is_dropped_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(
            Encoding.Latin1.GetBytes("/Fm0 Do"),
            "/XObject << /Fm0 5 0 R >>",
            Form("/Group << /S /Transparency /I true /CS [/Indexed /DeviceRGB 0 <000000>] >> /BBox [0 0 10 10]", "0 0 10 10 re f")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal("group Transparency isolated bbox [0 0 10 10] [1 0 0 1 0 0]", recorder.Operations[0]);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "GroupColorSpaceInvalid");
    }

    [Fact]
    public void A_group_in_a_tiling_pattern_s_cell_is_recorded_in_pattern_space()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(
            Encoding.Latin1.GetBytes("/Pattern cs /P0 scn 0 0 100 100 re f"),
            "/Pattern << /P0 5 0 R >>",
            ContentSamples.Stream(
                "/Fm0 Do",
                "/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10 /Matrix [1 0 0 1 30 40] /Resources << /XObject << /Fm0 6 0 R >> >>"),
            Form("/Group << /S /Transparency /I true >> /BBox [0 0 5 5] /Matrix [1 0 0 1 2 3]", "0 0 5 5 re f")));
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        var cell = new RecordingBackend();

        Assert.Single(recorder.Tilings).Cell.Replay(cell);

        // The cell's list replays with pattern space as its default space (§8.7.3.1): the group's matrix is the form's alone.
        Assert.Contains("group Transparency isolated bbox [0 0 5 5] [1 0 0 1 2 3]", cell.Operations);
    }

    [Fact]
    public void A_region_replay_hands_a_group_over_once_or_skips_it_whole()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(
            Encoding.Latin1.GetBytes("/Half gs /Fm0 Do 500 500 10 10 re f"),
            States + " /XObject << /Fm0 5 0 R >>",
            Form("/Group << /S /Transparency >> /BBox [0 0 100 100]", "10 10 20 20 re f")));
        DisplayList list = document.Pages[0].CreateDisplayList();
        var inside = new RecordingBackend();
        var outside = new RecordingBackend();

        list.Replay(inside, new PdfRectangle(0, 0, 50, 50));
        list.Replay(outside, new PdfRectangle(400, 400, 600, 600));

        // Bounded by its content (10..30), the group reaches the backend once, its content through the group alone.
        Assert.Single(inside.Operations, line => line.StartsWith("group", StringComparison.Ordinal));
        Assert.Single(inside.Operations, line => line.StartsWith("fill nonzero M 10 10", StringComparison.Ordinal));
        Assert.DoesNotContain(outside.Operations, line => line.StartsWith("group", StringComparison.Ordinal) || line.StartsWith("fill nonzero M 10 10", StringComparison.Ordinal));
        Assert.Contains(outside.Operations, line => line.StartsWith("fill nonzero M 500 500", StringComparison.Ordinal));
    }

    [Fact]
    public void A_group_in_a_type3_glyph_is_a_group_at_every_show_of_the_glyph()
    {
        // The description of a runs once and is replayed at the second show (#108); its group XObject is a group at both shows,
        // under each glyph's transform.
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(
            Encoding.Latin1.GetBytes("BT /T3 10 Tf 100 100 Td (aa) Tj ET"),
            "/Font << /T3 5 0 R >>",
            "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 6 0 R >> "
            + "/Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [1000] /Resources << /XObject << /Fm0 7 0 R >> " + States + " >> >>",
            ContentSamples.Stream("1000 0 d0 /Half gs /Fm0 Do"),
            Form("/Group << /S /Transparency >> /BBox [0 0 1000 1000]", "0 0 1000 1000 re f")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        string[] groups = [.. recorder.Operations.Where(line => line.StartsWith("group", StringComparison.Ordinal))];
        Assert.Equal(2, groups.Length);
        Assert.All(groups, line => Assert.StartsWith("group Transparency opacity 0.25 Multiply bbox [0 0 1000 1000] [", line));
        Assert.EndsWith(" 100 100]", groups[0], StringComparison.Ordinal);
        Assert.EndsWith(" 110 100]", groups[1], StringComparison.Ordinal);
        Assert.Equal(2, recorder.Operations.Count(line => line == "end group"));
    }

    private static string Form(string entries, string content, string resources = "") =>
        ContentSamples.Stream(content, $"/Type /XObject /Subtype /Form {entries} /Resources << {resources} >>");

    private static List<string> Record(string content, string resources = "", params string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), States + " " + resources, objects));
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        return recorder.Operations;
    }
}
