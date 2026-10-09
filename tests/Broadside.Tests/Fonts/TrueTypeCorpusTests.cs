using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Glyph outlines and glyph selection of the embedded TrueType corpus files, through the public document API. Expected values
/// are the coordinates and tables <c>tests/Corpus/generate.py</c> writes. ISO 32000-2 §9.6.3, §9.6.5.4, §9.9.
/// </summary>
public class TrueTypeCorpusTests
{
    [Fact]
    public void Every_code_of_the_embedded_TrueType_page_has_its_quadratic_outline()
    {
        using PdfDocument document = Open("text-truetype-embedded.pdf");
        PdfTrueTypeFont font = Font(document, "F1");
        FontProgram program = Assert.IsAssignableFrom<FontProgram>(font.Program);

        Assert.Equal(FontProgramFormat.TrueType, program.Format);
        Assert.Equal(3, program.GlyphCount);
        Assert.Equal(1.0 / 1000, program.FontMatrix.A);
        Assert.Equal(1, font.GetGlyphId(0x48));
        Assert.Equal(2, font.GetGlyphId(0x49));
        Assert.Equal(
            "M 100,0 L 100,700 L 300,700 L 300,400 L 500,400 L 500,700 L 700,700 L 700,0 L 500,0 L 500,300 L 300,300 L 300,0 Z",
            OutlineText.Of(program, font.GetGlyphId(0x48)));
        Assert.Equal("M 100,0 L 100,700 L 300,700 L 300,0 Z", OutlineText.Of(program, font.GetGlyphId(0x49)));
        Assert.Equal("Empty", OutlineText.Of(program, 0));
        Assert.Equal(new GlyphMetrics(800, 100), program.GetMetrics(1));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Simple_glyphs_start_contours_on_curve_and_imply_midpoints_between_off_curve_points()
    {
        using PdfDocument document = Open("text-truetype-composite.pdf");
        PdfTrueTypeFont font = Font(document, "F1");
        FontProgram program = font.Program!;

        Assert.Equal([1, 2, 3], Glyphs(font, "Ioc"));
        Assert.Equal("M 100,0 L 100,700 L 300,700 L 300,0 Z", OutlineText.Of(program, 1));
        Assert.Equal("M 125,125 Q 250,0 375,125 Q 500,250 375,375 Q 250,500 125,375 Q 0,250 125,125 Z", OutlineText.Of(program, 2));
        Assert.Equal("M 0,200 Q 0,0 200,0 L 200,200 Z", OutlineText.Of(program, 3));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Composite_glyphs_apply_offsets_transforms_point_matching_and_nesting()
    {
        using PdfDocument document = Open("text-truetype-composite.pdf");
        PdfTrueTypeFont font = Font(document, "F1");
        FontProgram program = font.Program!;

        Assert.Equal([4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14], Glyphs(font, "0123456789:"));
        string[] expected =
        [
            "M 500,300 L 500,1000 L 700,1000 L 700,300 Z",              // word offset (400, 300)
            "M 110,20 L 110,720 L 310,720 L 310,20 Z",                  // byte offset (10, 20)
            "M 100,20 L 100,370 L 200,370 L 200,20 Z",                  // scale 0.5, SCALED_COMPONENT_OFFSET: offset (50, 20)
            "M 150,40 L 150,390 L 250,390 L 250,40 Z",                  // scale 0.5, UNSCALED_COMPONENT_OFFSET: offset (100, 40)
            "M 150,40 L 150,390 L 250,390 L 250,40 Z",                  // neither flag: unscaled, as on Apple and Microsoft platforms
            "M 300,0 L 300,700 L 100,700 L 100,0 Z",                    // x scale -1, offset (400, 0)
            "M 700,100 L 0,100 L 0,300 L 700,300 Z",                    // 2x2 (0 1 -1 0): x' = -y, y' = x, offset (700, 0)
            "M 100,0 L 100,700 L 300,700 L 300,0 Z M 300,700 L 300,1400 L 500,1400 L 500,700 Z", // point 0 onto point 2
            "M 110,120 L 110,820 L 310,820 L 310,120 Z",                // glyph 5 moved by (0, 100)
            "M 50,200 Q 50,0 250,0 L 250,200 Z",                        // glyph 3 moved by (50, 0)
            "M 125,125 Q 250,0 375,125 Q 500,250 375,375 Q 250,500 125,375 Q 0,250 125,125 Z", // with instructions
        ];
        Assert.Equal(expected, Enumerable.Range(4, 11).Select(glyph => OutlineText.Of(program, glyph)).ToArray());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Advances_past_the_last_hmtx_advance_repeat_it_and_USE_MY_METRICS_takes_the_component_metrics()
    {
        using PdfDocument document = Open("text-truetype-composite.pdf");
        FontProgram program = Font(document, "F1").Program!;

        Assert.Equal(new GlyphMetrics(600, 100), program.GetMetrics(11));
        Assert.Equal(new GlyphMetrics(600, 110), program.GetMetrics(12));
        Assert.Equal(new GlyphMetrics(300, 0), program.GetMetrics(13));
        Assert.Equal(new GlyphMetrics(600, 150), program.GetMetrics(15));
        Assert.Equal(new GlyphMetrics(0, 0), program.GetMetrics(16));
    }

    [Fact]
    public void An_outline_is_placed_at_its_left_side_bearing()
    {
        using PdfDocument document = Open("text-truetype-composite.pdf");
        PdfTrueTypeFont font = Font(document, "F1");

        Assert.Equal(15, font.GetGlyphId((byte)'<'));
        Assert.Equal("M 150,0 L 150,700 L 350,700 L 350,0 Z", OutlineText.Of(font.Program!, 15));
    }

    [Fact]
    public void A_format_4_glyph_id_array_entry_of_0_stays_the_missing_glyph()
    {
        using PdfDocument document = Open("text-truetype-composite.pdf");
        PdfTrueTypeFont font = Font(document, "F1");
        FontCharacterMap map = Assert.Single(font.Program!.CharacterMaps);

        Assert.Equal((3, 1, 4), (map.PlatformId, map.EncodingId, map.Format));
        Assert.Equal(0, map.GetGlyphId(';'));
        Assert.Equal(0, font.GetGlyphId((byte)';'));
        Assert.Equal(1, map.GetGlyphId('I'));
        Assert.Equal(0, map.GetGlyphId('J'));
        Assert.Equal(0, map.GetGlyphId(0x10000));
    }

    [Fact]
    public void A_symbolic_font_selects_glyphs_by_code_through_3_0_with_the_F0_high_byte_before_1_0()
    {
        using PdfDocument document = Open("text-truetype-symbolic.pdf");
        PdfTrueTypeFont font = Font(document, "F1");

        Assert.Equal([1, 2, 3, 2], Glyphs(font, "ABCD"));
        Assert.Equal(0, font.GetGlyphId((byte)'E'));
        Assert.All(document.Diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity));
        Assert.Equal(["FontBuiltInEncodingUnavailable", "FontGlyphMappingFallback"], document.Diagnostics.Select(diagnostic => diagnostic.Code).Order().ToArray());
    }

    [Fact]
    public void A_nonsymbolic_font_with_only_a_1_0_cmap_maps_names_through_Mac_OS_Roman_and_then_post()
    {
        using PdfDocument document = Open("text-truetype-macroman.pdf");
        PdfTrueTypeFont font = Font(document, "F1");
        FontProgram program = font.Program!;

        Assert.Equal(["Euro", "eacute", "brds.alt"], [font.GetGlyphName(0x80), font.GetGlyphName(0xE9), font.GetGlyphName(0x81)]);
        Assert.Equal([1, 2, 3], [font.GetGlyphId(0x80), font.GetGlyphId(0xE9), font.GetGlyphId(0x81)]);
        Assert.Equal(6, Assert.Single(program.CharacterMaps).Format);
        Assert.Equal([".notdef", "Euro", "eacute", "brds.alt"], [.. Enumerable.Range(0, 4).Select(program.GetGlyphName)]);
        Assert.True(program.TryGetGlyphId("brds.alt", out int glyph));
        Assert.Equal(3, glyph);
        Assert.False(program.TryGetGlyphId("Aring", out _));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Long_loca_offsets_and_a_3_10_format_12_cmap_select_the_glyphs()
    {
        using PdfDocument document = Open("text-truetype-loca-long.pdf");
        PdfTrueTypeFont font = Font(document, "F1");
        FontProgram program = font.Program!;
        FontCharacterMap map = Assert.Single(program.CharacterMaps);

        Assert.Equal((3, 10, 12), (map.PlatformId, map.EncodingId, map.Format));
        Assert.Equal(2, map.GetGlyphId(0x1F600));
        Assert.Equal([1, 2], Glyphs(font, "HI"));
        Assert.Equal(
            "M 100,0 L 100,700 L 300,700 L 300,400 L 500,400 L 500,700 L 700,700 L 700,0 L 500,0 L 500,300 L 300,300 L 300,0 Z",
            OutlineText.Of(program, 1));
        Assert.Equal("M 100,0 L 100,700 L 300,700 L 300,0 Z", OutlineText.Of(program, 2));
        Assert.Equal(".null", program.GetGlyphName(1));
        Assert.Equal("BroadsideLongLoca", program.PostScriptName);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("text-truetype-embedded.pdf")]
    [InlineData("text-truetype-composite.pdf")]
    [InlineData("text-truetype-macroman.pdf")]
    [InlineData("text-truetype-loca-long.pdf")]
    public void Strict_mode_reads_every_glyph_of_the_well_formed_TrueType_files(string name)
    {
        using PdfDocument document = Open(name, new PdfOptions().UseStrict());
        PdfTrueTypeFont font = Font(document, "F1");
        var outline = new GlyphOutline();
        for (int code = 0; code < 256; code++)
        {
            _ = font.GetGlyphId((byte)code);
        }

        for (int glyph = 0; glyph < font.Program!.GlyphCount; glyph++)
        {
            Assert.NotEqual(GlyphOutlineStatus.Invalid, font.Program.GetOutline(glyph, outline));
        }

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity > DiagnosticSeverity.Information);
    }

    internal static PdfDocument Open(string name, PdfOptions? options = null) =>
        PdfDocument.Open(Path.Combine(Corpus.Directory, name), options ?? new PdfOptions());

    internal static PdfTrueTypeFont Font(PdfDocument document, string resource) =>
        Assert.IsType<PdfTrueTypeFont>(Assert.Single(document.Pages).GetFont(resource));

    private static int[] Glyphs(PdfTrueTypeFont font, string codes) => [.. codes.Select(code => font.GetGlyphId((byte)code))];
}
