using System.Text;
using Broadside.Graphics;
using Broadside.Parsing;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Tiling patterns in the display list (ISO 32000-2 §8.7.3; ADR 0006): a fill or stroke in a tiling pattern reaches the backend as
/// <see cref="IRenderingBackend.FillPathWithTilingPattern"/> or <see cref="IRenderingBackend.StrokePathWithTilingPattern"/>, with
/// the pattern, its pattern space and its cell built into a display list of its own, relative to pattern space and built once
/// per pattern per page.
/// </summary>
public sealed class DisplayListTilingTests
{
    [Fact]
    public void A_fill_in_a_coloured_tiling_pattern_hands_over_the_cell_as_a_list_in_pattern_space()
    {
        // pattern-tiling-colored.pdf: 2 0 0 2 0 0 cm, then 0 0 200 200 re f with P1 (Matrix [1 0 0 1 10 10], BBox [0 0 20 20],
        // steps 25); the cell paints a red and a blue square.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-tiling-colored.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            [
                "transform [2 0 0 2 0 0]",
                "fill nonzero M 0 0 L 200 0 L 200 200 L 0 200 Z | tiling Colored ConstantSpacing [1 0 0 1 10 10] bbox [0 0 20 20] step 25 25",
            ],
            recorder.Operations);
        Assert.Equal(
            [
                "save",
                "clip nonzero M 0 0 L 20 0 L 20 20 L 0 20 Z",
                "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceRgb 1 0 0",
                "fill nonzero M 10 10 L 20 10 L 20 20 L 10 20 Z | DeviceRgb 0 0 1",
                "restore",
            ],
            Replay(Assert.Single(recorder.Tilings).Cell));
    }

    [Fact]
    public void A_cell_is_built_once_per_pattern_per_page_and_shared_by_every_paint_with_it()
    {
        // Two fills and a stroke with P0, one fill with P1: two cells. A second list of the same page builds its own.
        using PdfDocument document = PdfDocument.Open(Page(
            "/Pattern << /P0 5 0 R /P1 6 0 R >>",
            "/Pattern cs /P0 scn /Pattern CS /P0 SCN 0 0 10 10 re f 20 0 10 10 re f 40 0 m 50 0 l S /P1 scn 60 0 10 10 re f",
            Cell("0 0 5 5 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 5 5] /XStep 5 /YStep 5"),
            Cell("0 0 2 2 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 5 5] /XStep 5 /YStep 5")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(4, recorder.Tilings.Count);
        DisplayList p0 = recorder.Tilings[0].Cell;
        Assert.Same(p0, recorder.Tilings[1].Cell);
        Assert.Same(p0, recorder.Tilings[2].Cell);
        Assert.NotSame(p0, recorder.Tilings[3].Cell);
        Assert.StartsWith("stroke 1 Butt Miter 10 [] 0 M 40 0 L 50 0 | tiling Colored", recorder.Operations[2], StringComparison.Ordinal);

        var again = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(again);
        Assert.NotSame(p0, again.Tilings[0].Cell);
        Assert.Equal(Replay(p0), Replay(again.Tilings[0].Cell));
    }

    [Fact]
    public void An_uncoloured_cell_is_painted_in_the_colour_given_with_the_pattern_and_built_once_per_colour()
    {
        // §8.7.3.3: the cell of pattern-tiling-uncolored.pdf has no colour operator; 0.8 0.2 0 /P1 scn colours it.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-tiling-uncolored.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(["fill nonzero M 50 50 L 350 50 L 350 350 L 50 350 Z | tiling Uncolored NoDistortion [1 0 0 1 0 0] bbox [0 0 12 12] step 15 15"], recorder.Operations);
        Assert.Equal(
            ["save", "clip nonzero M 0 0 L 12 0 L 12 12 L 0 12 Z", "fill nonzero M 0 0 L 12 0 L 6 12 | DeviceRgb 0.8 0.2 0", "restore"],
            Replay(Assert.Single(recorder.Tilings).Cell));

        using PdfDocument twoColours = PdfDocument.Open(Page(
            "/ColorSpace << /Cs1 [/Pattern /DeviceRGB] >> /Pattern << /P1 5 0 R >>",
            "/Cs1 cs 1 0 0 /P1 scn 0 0 10 10 re f 0 0 1 /P1 scn 20 0 10 10 re f 1 0 0 /P1 scn 40 0 10 10 re f",
            Cell("0 0 3 3 re f", "/PaintType 2 /TilingType 1 /BBox [0 0 5 5] /XStep 5 /YStep 5")));
        var colours = new RecordingBackend();
        twoColours.Pages[0].CreateDisplayList().Replay(colours);
        Assert.NotSame(colours.Tilings[0].Cell, colours.Tilings[1].Cell);
        Assert.Same(colours.Tilings[0].Cell, colours.Tilings[2].Cell);
        Assert.Contains("fill nonzero M 0 0 L 3 0 L 3 3 L 0 3 Z | DeviceRgb 0 0 1", Replay(colours.Tilings[1].Cell));
    }

    [Fact]
    public void A_pattern_selected_in_a_form_is_laid_out_in_the_form_s_space_at_do()
    {
        // pattern-in-form.pdf: 1 0 0 1 50 50 cm, then the form (Matrix [0.5 0 0 0.5 100 100]) fills with P1 (identity Matrix).
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-in-form.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Contains(
            "fill nonzero M 0 0 L 400 0 L 400 400 L 0 400 Z | tiling Colored ConstantSpacing [0.5 0 0 0.5 150 150] bbox [0 0 20 20] step 20 20",
            recorder.Operations);
        Assert.Equal(
            ["save", "clip nonzero M 0 0 L 20 0 L 20 20 L 0 20 Z", "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceRgb 0 0.5 0", "restore"],
            Replay(Assert.Single(recorder.Tilings).Cell));
    }

    [Fact]
    public void A_cell_that_paints_with_its_own_pattern_is_built_empty_and_records_the_recursion()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pattern-recursive.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Empty(Replay(Assert.Single(recorder.Tilings).Cell));
        Assert.Contains(document.Diagnostics, static d => d.Code == DiagnosticCodes.ContentPatternRecursion);
    }

    [Fact]
    public void A_cell_records_its_transforms_and_nested_patterns_relative_to_pattern_space()
    {
        // P0 (Matrix [2 0 0 2 100 100]) paints with cm and with P1 (Matrix [1 0 0 1 3 0]) selected inside its cell: the inner
        // pattern space is P1's matrix in P0's space, whatever P0's own matrix is.
        using PdfDocument document = PdfDocument.Open(Page(
            "/Pattern << /P0 5 0 R >>",
            "/Pattern cs /P0 scn 0 0 100 100 re f",
            Cell(
                "1 0 0 1 4 4 cm /Pattern cs /P1 scn 0 0 4 4 re f",
                "/PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10 /Matrix [2 0 0 2 100 100]",
                "/Pattern << /P1 6 0 R >>"),
            Cell("0 0 1 1 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 2 2] /XStep 2 /YStep 2 /Matrix [1 0 0 1 3 0]")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        var cell = new RecordingBackend();
        Assert.Single(recorder.Tilings).Cell.Replay(cell);
        Assert.Equal(
            [
                "save",
                "clip nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z",
                "transform [1 0 0 1 4 4]",
                "fill nonzero M 0 0 L 4 0 L 4 4 L 0 4 Z | tiling Colored ConstantSpacing [1 0 0 1 3 0] bbox [0 0 2 2] step 2 2",
                "restore",
            ],
            cell.Operations);
        Assert.Equal(
            ["save", "clip nonzero M 0 0 L 2 0 L 2 2 L 0 2 Z", "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0", "restore"],
            Replay(Assert.Single(cell.Tilings).Cell));
    }

    [Fact]
    public void A_cell_starts_with_the_default_alpha_and_blend_mode_and_the_paint_carries_the_state_s()
    {
        // §11.6.7: the form starts with ca 0.5 and Multiply (set before Do); its pattern's cell starts at 1 and Normal, and the
        // fill with the pattern composites with 0.5 and Multiply.
        using PdfDocument document = PdfDocument.Open(Page(
            "/ExtGState << /G0 << /ca 0.5 /BM /Multiply >> >> /XObject << /Fm0 5 0 R >>",
            "/G0 gs /Fm0 Do",
            ContentSamples.Stream("/Pattern cs /P1 scn 0 0 10 10 re f", "/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << /Pattern << /P1 6 0 R >> >>"),
            Cell("0 0 1 1 re f", "/PaintType 1 /TilingType 1 /BBox [0 0 2 2] /XStep 2 /YStep 2")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Contains("compositing 1 0.5 Multiply", recorder.Operations);
        Assert.DoesNotContain(Replay(Assert.Single(recorder.Tilings).Cell), static line => line.StartsWith("compositing", StringComparison.Ordinal));
    }

    private static string Cell(string content, string entries, string resources = "") =>
        ContentSamples.Stream(content, $"/Type /Pattern /PatternType 1 /Resources << {resources} >> {entries}");

    private static List<string> Replay(DisplayList list)
    {
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        return recorder.Operations;
    }

    private static byte[] Page(string resources, string content, params string[] objects) =>
        ContentSamples.OnePage(Encoding.ASCII.GetBytes(content), resources, objects);
}
