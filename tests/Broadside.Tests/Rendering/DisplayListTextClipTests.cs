using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Text clips in the display list (ISO 32000-2 §9.3.6, §8.5.4; ADR 0006): the glyphs a text object shows in rendering modes 4 to 7
/// reach the backend at the end of the text object, after they are painted, as one <see cref="IRenderingBackend.IntersectClip"/> of
/// their outlines under the non-zero winding rule; Type 3 glyphs and glyphs without outlines add nothing.
/// </summary>
public sealed partial class DisplayListTextClipTests
{
    // TextSamples.FontProgram draws B (code 66) as the box x 50-280, y 0-460 in a 1000-unit em, 600 units wide.
    [Fact]
    public void Glyphs_shown_in_mode_7_become_one_nonzero_clip_of_their_outlines_in_user_space_at_the_end_of_the_text_object()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Ascii("BT /F1 100 Tf 7 Tr 100 200 Td (BB) Tj ET 0 0 612 792 re f")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(3, recorder.Operations.Count);
        Assert.StartsWith("glyphs Clip TrueType ", recorder.Operations[0], StringComparison.Ordinal);
        Assert.Equal(
            "clip nonzero M 105 200 L 105 246 L 128 246 L 128 200 Z M 165 200 L 165 246 L 188 246 L 188 200 Z",
            Rounded(recorder.Operations[1]));
        Assert.StartsWith("fill nonzero M 0 0 L 612 0", recorder.Operations[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Glyphs_painted_in_a_clipping_mode_are_painted_before_the_clip_which_applies_only_to_later_paints_until_Q()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Ascii("q BT /F1 100 Tf 4 Tr 100 200 Td (B) Tj ET 1 g 0 0 10 10 re f Q 0 0 20 20 re f")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            [
                "save",
                "glyphs FillClip TrueType 35 [100 0 0 100 100 200] | fill DeviceGray 0",
                "clip nonzero M 105 200 L 105 246 L 128 246 L 128 200 Z",
                "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 1",
                "restore",
                "fill nonzero M 0 0 L 20 0 L 20 20 L 0 20 Z | DeviceGray 0",
            ],
            recorder.Operations.Select(Rounded));
    }

    [Theory]
    [InlineData("BT /F1 100 Tf 7 Tr ET")]
    [InlineData("BT 7 Tr ET")]
    [InlineData("BT /F1 100 Tf 7 Tr 100 200 Td (   ) Tj ET")]
    [InlineData("BT /F1 0 Tf 7 Tr 100 200 Td (B) Tj ET")]
    public void A_text_object_in_a_clipping_mode_that_shows_no_glyph_outline_leaves_the_clip_unchanged(string text)
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Ascii(text + " 0 0 10 10 re f")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.DoesNotContain(recorder.Operations, static line => line.StartsWith("clip ", StringComparison.Ordinal));
        Assert.Equal("fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 0", recorder.Operations[^1]);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    public void Type_3_glyphs_add_nothing_to_the_clip_of_a_text_object_that_also_shows_clipping_glyphs_of_another_font(int mode)
    {
        // A Type 3 square in /T3 at code 65, then a B in /F1, both in the clipping mode: only the B's outline clips.
        const string type3 = "/T3 << /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] "
            + "/CharProcs << /sq 8 0 R >> /Encoding << /Type /Encoding /Differences [65 /sq] >> /FirstChar 65 /LastChar 65 /Widths [1000] >>";
        string content = $"BT {mode} Tr /T3 100 Tf 0 200 Td (A) Tj /F1 100 Tf (B) Tj ET";
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Ascii(content), fonts: type3, objects: ContentSamples.Stream("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // The B starts after the square's advance of 100.
        Assert.Equal(["clip nonzero M 105 200 L 105 246 L 128 246 L 128 200 Z"], recorder.Operations.Where(static line => line.StartsWith("clip ", StringComparison.Ordinal)).Select(Rounded));
    }

    [Fact]
    public void A_type_3_font_alone_in_a_clipping_mode_leaves_the_clip_unchanged()
    {
        const string type3 = "/T3 << /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] "
            + "/CharProcs << /sq 8 0 R >> /Encoding << /Type /Encoding /Differences [65 /sq] >> /FirstChar 65 /LastChar 65 /Widths [1000] >>";
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Ascii("BT 7 Tr /T3 100 Tf 0 200 Td (AA) Tj ET 0 0 10 10 re f"), fonts: type3, objects: ContentSamples.Stream("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f")));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.DoesNotContain(recorder.Operations, static line => line.StartsWith("clip ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_composite_font_clips_with_the_glyph_its_cmap_and_cidfont_select()
    {
        // Identity-H over a CIDFontType2 embedding the sample program with the identity CID-to-GID map: <0023> is glyph 35, the B.
        const string type0 = "/F0 << /Type /Font /Subtype /Type0 /BaseFont /BroadsideSample /Encoding /Identity-H /DescendantFonts [8 0 R] >>";
        const string cidFont = "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /BroadsideSample /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
            + "/FontDescriptor 6 0 R /DW 600 /CIDToGIDMap /Identity >>";
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Ascii("BT /F0 100 Tf 7 Tr 100 200 Td <00230023> Tj ET"), fonts: type0, objects: cidFont));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            "clip nonzero M 105 200 L 105 246 L 128 246 L 128 200 Z M 165 200 L 165 246 L 188 246 L 188 200 Z",
            Rounded(recorder.Operations[^1]));
    }

    [Fact]
    public void Text_clipping_modes_pdf_clips_each_page_to_its_glyphs_after_painting_them_and_before_the_image()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-render-modes.pdf"));
        Assert.Equal(5, document.Pages.Count);
        string[] modes = ["FillClip", "StrokeClip", "FillStrokeClip", "Clip"];
        for (int page = 1; page <= 4; page++)
        {
            var recorder = new RecordingBackend();

            document.Pages[page].CreateDisplayList().Replay(recorder);

            // HI at 120 pt from (30, 100): the H's left stem (glyph units 100-300 by 0-700) is x 42-66, y 100-184.
            List<string> operations = recorder.Operations;
            int glyphs = operations.FindIndex(static line => line.StartsWith("glyphs ", StringComparison.Ordinal));
            int clip = operations.FindIndex(static line => line.StartsWith("clip ", StringComparison.Ordinal));
            int image = operations.FindIndex(static line => line.StartsWith("image ", StringComparison.Ordinal));
            Assert.StartsWith($"glyphs {modes[page - 1]} TrueType 1 ", operations[glyphs], StringComparison.Ordinal);
            Assert.StartsWith("clip nonzero M 42 100 L 42 184 L 66 184", Rounded(operations[clip]), StringComparison.Ordinal);
            Assert.True(glyphs < clip && clip < image, string.Join('\n', operations));
        }
    }

    private static byte[] Ascii(string content) => Encoding.ASCII.GetBytes(content);

    /// <summary>Rounds every number in a recorded line to 9 decimals, so products of the font matrix compare as written.</summary>
    private static string Rounded(string line) => NumberPattern().Replace(
        line,
        static match => Math.Round(double.Parse(match.Value, CultureInfo.InvariantCulture), 9).ToString(CultureInfo.InvariantCulture));

    [GeneratedRegex(@"-?\d+(\.\d+)?(E[-+]?\d+)?")]
    private static partial Regex NumberPattern();
}
