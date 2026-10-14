using System.Buffers;
using System.Text;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Type 3 glyphs in the display list (ISO 32000-2 §9.6.4, Tables 110 and 111; ADR 0006): each glyph's description paints in glyph
/// space under FontMatrix × text space × CTM, a <c>d1</c> glyph in the colours of the text it is shown with and a <c>d0</c> glyph in
/// its own, built once per font and code and replayed at every show.
/// </summary>
public sealed class DisplayListType3Tests
{
    private static readonly CosName Contents = new("Contents");

    [Fact]
    public void Each_glyph_paints_its_description_in_glyph_space_a_d1_glyph_in_the_text_colour_and_a_d0_glyph_in_its_own()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // 0 0 1 rg BT /T3a 24 Tf 72 700 Td (abc) Tj: glyph space maps to user space by FontMatrix 0.001 x 24 pt, from x = 72, 96, 120.
        // a (d1): its square and its font's form /Fm0 (clipped to the form's BBox) in the text's blue, their own colours ignored;
        // b (d0): a red triangle; c (d1): an 8x1 image mask over the 750-unit box in blue, its gray image ignored.
        const string blue = " | DeviceRgb 0 0 1";
        string[] expected =
        [
            $"transform {Glyph(72, 700)}",
            $"fill nonzero M 0 0 L 750 0 L 750 750 L 0 750 Z{blue}",
            "save",
            "clip nonzero M 0 0 L 750 0 L 750 750 L 0 750 Z",
            $"fill nonzero M 100 100 L 300 100 L 300 300 L 100 300 Z{blue}",
            "restore",
            $"transform {Glyph(96, 700)}",
            "fill nonzero M 0 0 L 375 750 L 750 0 | DeviceRgb 1 0 0",
            "save",
            $"transform {new Matrix(18, 0, 0, 18, 120, 700)}",
            $"imagemask 8x1{blue}",
            "restore",
        ];
        Assert.Equal(expected, recorder.Operations.Take(expected.Length));

        // The page's own /Fm0 (a black page-sized square) is never found from inside a glyph.
        Assert.DoesNotContain(recorder.Operations, line => line.Contains("612", StringComparison.Ordinal) || line.EndsWith("DeviceGray 0", StringComparison.Ordinal));

        static Matrix Glyph(double x, double y) => new(0.024, 0, 0, 0.024, x, y);
    }

    [Fact]
    public void A_d1_glyph_takes_the_colours_of_each_show_and_a_d0_glyph_keeps_its_own()
    {
        using PdfDocument document = OpenWithContent(
            "text-type3.pdf", "BT /T3a 24 Tf 72 700 Td 0 0 1 rg (ab) Tj 0 1 0 rg (ab) Tj ET");
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // a (d1) is blue, then green, both times with its form; b (d0) is red both times (Table 111).
        Assert.Equal(
            ["DeviceRgb 0 0 1", "DeviceRgb 0 0 1", "DeviceRgb 1 0 0", "DeviceRgb 0 1 0", "DeviceRgb 0 1 0", "DeviceRgb 1 0 0"],
            recorder.Fills.Select(static fill => $"{fill.ColorSpace.Family} {string.Join(' ', fill.Components)}"));
        Assert.Equal(
            [Glyph(72), Glyph(96), Glyph(120), Glyph(144)],
            recorder.Operations.Where(static line => line.StartsWith("transform", StringComparison.Ordinal)).Select(static line => line[10..]));

        static string Glyph(double x) => new Matrix(0.024, 0, 0, 0.024, x, 700).ToString();
    }

    [Fact]
    public void A_d1_glyph_shown_a_thousand_times_runs_its_description_once()
    {
        var hex = new CountingFilter(new AsciiHexDecodeFilter());
        string shows = string.Concat(Enumerable.Repeat("(c) Tj ", 1000));
        using PdfDocument document = OpenWithContent(
            "text-type3.pdf", "0 0 1 rg BT /T3a 24 Tf 72 700 Td " + shows + "ET", new PdfOptions().UseFilter(hex));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // c's inline image mask is decoded where its description runs: once; every show paints it, each 24 points further on.
        Assert.Equal(1, hex.Calls);
        Assert.Equal(1000, recorder.ImageMasks.Count);
        string[] transforms = [.. recorder.Operations.Where(static line => line.StartsWith("transform", StringComparison.Ordinal))];
        Assert.Equal(1000, transforms.Length);
        Assert.Equal($"transform {new Matrix(18, 0, 0, 18, 72 + (999 * 24), 700)}", transforms[^1]);
    }

    [Fact]
    public void Glyphs_in_rendering_modes_3_and_7_paint_nothing_but_advance()
    {
        using PdfDocument document = OpenWithContent(
            "text-type3.pdf", "0 0 1 rg BT /T3a 24 Tf 72 700 Td 3 Tr (a) Tj 7 Tr (a) Tj 0 Tr (b) Tj ET");
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            [$"transform {new Matrix(0.024, 0, 0, 0.024, 120, 700)}", "fill nonzero M 0 0 L 375 750 L 750 0 | DeviceRgb 1 0 0"],
            recorder.Operations);
    }

    [Fact]
    public void A_glyph_that_would_run_inside_itself_paints_the_rest_and_records_the_recursion()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3-recursive.pdf"));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // (arn) at 24 pt from 72: a's square; r (d0) shows (ar) in its own glyph space, so a again at 1/1000 x 24 x 0.024 scale and
        // no r inside r; n (no d0 or d1, run as d0) a 500-unit square. Every square is in the initial black.
        const string square = "fill nonzero M 0 0 L 1000 0 L 1000 1000 L 0 1000 Z | DeviceGray 0";
        Assert.Equal(
            [
                $"transform {new Matrix(0.024, 0, 0, 0.024, 72, 700)}",
                square,
                $"transform {new Matrix(0.024 * 0.024, 0, 0, 0.024 * 0.024, 96, 700)}",
                square,
                $"transform {new Matrix(0.024, 0, 0, 0.024, 120, 700)}",
                "fill nonzero M 0 0 L 500 0 L 500 500 L 0 500 Z | DeviceGray 0",
            ],
            recorder.Operations);
        Assert.Contains(document.Diagnostics, static diagnostic => diagnostic.Code == "ContentType3GlyphRecursion");
        Assert.Contains(document.Diagnostics, static diagnostic => diagnostic.Code == "ContentType3GlyphMetricsMissing");
    }

    [Fact]
    public void A_glyph_reused_under_another_inherited_state_is_built_again_for_it()
    {
        using PdfDocument document = OpenWithContent(
            "text-type3.pdf", "BT /T3a 24 Tf 72 700 Td 2 w (a) Tj 4 w (a) Tj 2 w (a) Tj ET");
        PdfType3Font font = Assert.IsType<PdfType3Font>(document.Pages[0].GetFont("T3a"));
        font.GetCharProc(97)!.EncodedData = Encoding.ASCII.GetBytes("1000 0 0 0 750 750 d1 0 0 750 750 re S");
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // §9.6.4: aside from the CTM the description inherits the state of the text, so the strokes take the line width of each show.
        Assert.Equal(
            ["2", "4", "2"],
            recorder.Operations.Where(static line => line.StartsWith("stroke", StringComparison.Ordinal)).Select(static line => line.Split(' ')[1]));
    }

    [Fact]
    public void A_list_built_after_a_glyph_description_is_edited_paints_the_edited_glyph()
    {
        using PdfDocument document = OpenWithContent("text-type3.pdf", "BT /T3a 24 Tf 72 700 Td (aa) Tj ET");
        document.Pages[0].CreateDisplayList().Replay(new RecordingBackend());
        PdfType3Font font = Assert.IsType<PdfType3Font>(document.Pages[0].GetFont("T3a"));
        font.GetCharProc(97)!.EncodedData = Encoding.ASCII.GetBytes("1000 0 d0 0 0 500 500 re f");
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            ["fill nonzero M 0 0 L 500 0 L 500 500 L 0 500 Z | DeviceGray 0", "fill nonzero M 0 0 L 500 0 L 500 500 L 0 500 Z | DeviceGray 0"],
            recorder.Operations.Where(static line => line.StartsWith("fill", StringComparison.Ordinal)));
    }

    private static PdfDocument OpenWithContent(string fileName, string content, PdfOptions? options = null)
    {
        PdfDocument document = new PdfEngine(options ?? new PdfOptions()).Open(Corpus.Bytes(fileName));
        document.Pages[0].Dictionary[Contents] = new CosStream(new CosDictionary(), Encoding.ASCII.GetBytes(content));
        return document;
    }

    private sealed class CountingFilter(IStreamFilter inner) : IStreamFilter
    {
        private int _calls;

        public int Calls => _calls;

        public CosName Name => inner.Name;

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            Interlocked.Increment(ref _calls);
            inner.Decode(encoded, output, context);
        }
    }
}
