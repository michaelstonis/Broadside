using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The TrueType parser through the font program contract: bytes in, glyph outlines, metrics and lookups out, with programs built
/// in memory so each table can be made malformed. Vectors follow the OpenType "glyf", "cmap" and "post" table specifications and
/// fontTools' composite tests (offset, SCALED/UNSCALED offsets, point matching with a rotation). ISO 32000-2 §9.9.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class TrueTypeProgramTests
{
    private static readonly byte[] None = [];
    private static readonly byte[] Square = TrueTypeBuilder.Rectangle(0, 0, 100, 100);

    [Theory]
    [InlineData(0x1000, "M 10,20 L 10,70 L 60,70 L 60,20 Z")]
    [InlineData(0x0800, "M 5,10 L 5,60 L 55,60 L 55,10 Z")]
    [InlineData(0x0000, "M 10,20 L 10,70 L 60,70 L 60,20 Z")]
    public void A_scaled_component_offset_is_scaled_only_with_SCALED_COMPONENT_OFFSET(int offsetFlag, string expected)
    {
        FontProgram program = Parse(new TrueTypeBuilder
        {
            Glyphs = { None, Square, TrueTypeBuilder.Composite((0, 0, 60, 70), (0x0002 | 0x0008 | offsetFlag, 1, 10, 20, TrueTypeBuilder.F2Dot14(0.5))) },
            Metrics = [(500, 0), (500, 0), (500, 0)],
        });

        Assert.Equal(expected, OutlineText.Of(program, 2));
    }

    [Fact]
    public void Point_matching_moves_a_rotated_component_onto_a_point_of_the_glyph_so_far()
    {
        // Component 2 is the square rotated by 45 degrees (cos = sin = 11585/16384) with its point 0 matched onto point 2.
        FontProgram program = Parse(new TrueTypeBuilder
        {
            Glyphs =
            {
                None,
                Square,
                TrueTypeBuilder.Composite(
                    (0, 0, 171, 242),
                    (0x0002, 1, 0, 0, []),
                    (0x0080, 1, 2, 0, TrueTypeBuilder.F2Dot14(Math.Sqrt(0.5), Math.Sqrt(0.5), -Math.Sqrt(0.5), Math.Sqrt(0.5)))),
            },
            Metrics = [(500, 0), (500, 0), (500, 0)],
        });

        Assert.Equal(
            "M 0,0 L 0,100 L 100,100 L 100,0 Z M 100,100 L 29.2908,170.7092 L 100,241.4185 L 170.7092,170.7092 Z",
            OutlineText.Of(program, 2));
    }

    [Fact]
    public void A_composite_that_contains_itself_skips_the_component_with_a_diagnostic()
    {
        var context = new FontProgramContext();
        FontProgram program = Parse(
            new TrueTypeBuilder
            {
                Glyphs =
                {
                    None,
                    Square,
                    TrueTypeBuilder.Composite((0, 0, 100, 100), (0x0002, 1, 0, 0, []), (0x0002, 3, 0, 0, [])),
                    TrueTypeBuilder.Composite((0, 0, 100, 100), (0x0002, 2, 0, 0, [])),
                },
                Metrics = [(500, 0), (500, 0), (500, 0), (500, 0)],
            },
            context);

        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(program, 2));
        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(program, 3));
        Assert.Equal("FontGlyphInvalid", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void Components_nested_deeper_than_the_limit_are_skipped()
    {
        var context = new FontProgramContext { MaxCompositeDepth = 2 };
        FontProgram program = Parse(
            new TrueTypeBuilder
            {
                Glyphs =
                {
                    None,
                    Square,
                    TrueTypeBuilder.Composite((0, 0, 100, 100), (0x0002, 1, 0, 0, [])),
                    TrueTypeBuilder.Composite((0, 0, 100, 100), (0x0002, 2, 0, 0, [])),
                    TrueTypeBuilder.Composite((0, 0, 100, 100), (0x0002, 3, 0, 0, [])),
                },
                Metrics = [(500, 0), (500, 0), (500, 0), (500, 0), (500, 0)],
            },
            context);

        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(program, 3));
        Assert.Empty(context.Diagnostics);
        Assert.Equal("Empty", OutlineText.Of(program, 4));
        Assert.Equal("FontGlyphInvalid", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void A_glyph_with_more_points_than_the_limit_is_dropped()
    {
        var context = new FontProgramContext { MaxGlyphPoints = 6 };
        FontProgram program = Parse(
            new TrueTypeBuilder
            {
                Glyphs = { None, Square, TrueTypeBuilder.Composite((0, 0, 100, 100), (0x0002, 1, 0, 0, []), (0x0002, 1, 0, 0, [])) },
            },
            context);

        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(program, 1));
        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(program, 2));
        Assert.Equal("FontGlyphInvalid", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void Decreasing_contour_end_points_drop_the_glyph()
    {
        byte[] glyph = TrueTypeBuilder.Simple([(0, 0, true), (0, 10, true)], [(5, 5, true), (5, 6, true)]);
        glyph[10] = 0;
        glyph[11] = 3;
        var context = new FontProgramContext();
        FontProgram program = Parse(new TrueTypeBuilder { Glyphs = { None, glyph } }, context);

        Assert.Equal("Invalid", OutlineText.Of(program, 1));
        Assert.Equal("FontGlyphInvalid", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void A_glyph_whose_coordinates_are_cut_short_is_dropped_and_the_others_still_draw()
    {
        byte[] glyph = Square[..^3];
        var context = new FontProgramContext();
        FontProgram program = Parse(new TrueTypeBuilder { Glyphs = { None, glyph, Square } }, context);

        Assert.Equal("Invalid", OutlineText.Of(program, 1));
        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(program, 2));
        Assert.Equal("FontGlyphInvalid", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void A_flag_repeated_past_the_last_point_is_cut_short()
    {
        // Two on-curve points with x and y as positive bytes: one flag 0x37 repeated 5 times instead of 1.
        byte[] glyph = [0, 1, 0, 0, 0, 0, 0, 10, 0, 10, 0, 1, 0, 0, 0x3F, 5, 0, 10, 10, 0];
        var context = new FontProgramContext();
        FontProgram program = Parse(new TrueTypeBuilder { Glyphs = { None, glyph } }, context);

        Assert.Equal("M 0,10 L 10,10 Z", OutlineText.Of(program, 1));
        Assert.Equal("FontGlyphInvalid", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void Short_coordinates_take_their_sign_from_the_same_flag_and_same_without_short_repeats()
    {
        // Flags: point 0 x short positive (0x12), y short negative (0x04); point 1 x same (0x10), y int16; all on-curve.
        byte[] glyph = [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0x17, 0x11, 50, 20, 0x01, 0x2C];
        FontProgram program = Parse(new TrueTypeBuilder { Glyphs = { None, glyph }, Metrics = [(500, 0), (500, 0)] });

        Assert.Equal("M 50,-20 L 50,280 Z", OutlineText.Of(program, 1));
    }

    [Fact]
    public void Bad_loca_offsets_are_bounded_by_the_glyph_table()
    {
        byte[] font = new TrueTypeBuilder { Glyphs = { None, Square, Square }, LongOffsets = true }.Build();
        int loca = TableOffset(font, "loca");
        WriteUInt32(font, loca + 4, 0x7FFF0000);
        WriteUInt32(font, loca + 8, 0);
        var context = new FontProgramContext();
        FontProgram program = Parse(font, context);

        Assert.Equal("Invalid", OutlineText.Of(program, 1));
        Assert.Equal("FontGlyphInvalid", Assert.Single(context.Diagnostics).Code);
        Assert.Equal(GlyphOutlineStatus.Invalid, program.GetOutline(99, new GlyphOutline()));
        Assert.Equal(GlyphOutlineStatus.Invalid, program.GetOutline(-1, new GlyphOutline()));
    }

    [Fact]
    public void Missing_head_hhea_and_maxp_are_replaced_by_defaults_with_diagnostics()
    {
        var builder = new TrueTypeBuilder { Glyphs = { None, Square } };
        builder.Overrides["head"] = null;
        builder.Overrides["hhea"] = null;
        builder.Overrides["maxp"] = null;
        var context = new FontProgramContext();
        FontProgram program = Parse(builder, context);

        Assert.Equal(2, program.GlyphCount);
        Assert.Equal(1.0 / 1000, program.FontMatrix.A);
        Assert.Equal(default, program.GetMetrics(1));
        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(program, 1));
        Assert.Equal(["FontTableInvalid"], context.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Table_offsets_all_off_by_one_byte_are_recovered_from_where_each_table_starts(int shift)
    {
        // Every offset of the table directory one byte past (or before) its table: each table with a recognizable start (the "head"
        // magic number 0x5F0F3CF5, versions, ascending "loca" offsets, a plausible first glyph) is read where it starts.
        byte[] good = new TrueTypeBuilder { Glyphs = { None, Square, TrueTypeBuilder.Rectangle(10, 20, 30, 40) } }.Build();
        byte[] font = [.. good, 0];
        int count = (font[4] << 8) | font[5];
        if (shift > 0)
        {
            // Each stated offset one byte past its table.
            for (int index = 0; index < count; index++)
            {
                int record = 12 + (16 * index) + 8;
                WriteUInt32(font, record, (uint)(((font[record] << 24) | (font[record + 1] << 16) | (font[record + 2] << 8) | font[record + 3]) + 1));
            }
        }
        else
        {
            // Each table one byte past its stated offset: every byte after the directory moves one place on.
            int directoryEnd = 12 + (16 * count);
            Array.Copy(good, directoryEnd, font, directoryEnd + 1, good.Length - directoryEnd);
        }

        var context = new FontProgramContext();
        FontProgram program = Parse(font, context);

        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(program, 1));
        Assert.Equal("M 10,20 L 10,40 L 30,40 L 30,20 Z", OutlineText.Of(program, 2));
        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(("FontTableInvalid", DiagnosticSeverity.Warning), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void A_byte_lost_after_glyf_moves_only_the_tables_after_it()
    {
        // As in pdfjs/bug1050040.pdf: the tables before the lost byte ("cmap", "glyf") are where the directory says, those after it
        // ("head", "hhea", "hmtx", "loca", "maxp", "name", "post") one byte earlier.
        // The last glyph carries one unused trailing byte, which is the byte lost.
        byte[] last = [.. TrueTypeBuilder.Rectangle(10, 20, 30, 40), 0xAA];
        var builder = new TrueTypeBuilder { Glyphs = { None, Square, last } };
        byte[] good = builder.Build();
        int lost = TableOffset(good, "glyf") + TableLength(good, "glyf") - 1;
        byte[] font = [.. good[..lost], .. good[(lost + 1)..]];
        var context = new FontProgramContext();
        FontProgram program = Parse(font, context);

        Assert.Equal(3, program.GlyphCount);
        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(program, 1));
        Assert.Equal("M 10,20 L 10,40 L 30,40 L 30,20 Z", OutlineText.Of(program, 2));
        Assert.Equal(new GlyphMetrics(500, 10), program.GetMetrics(2));
        Assert.Equal("BroadsideTest", program.PostScriptName);
        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal("FontTableInvalid", diagnostic.Code);
        Assert.DoesNotContain("glyf", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_program_without_glyf_is_not_usable()
    {
        var builder = new TrueTypeBuilder { Glyphs = { None, Square } };
        builder.Overrides["glyf"] = null;
        var context = new FontProgramContext();

        Assert.Null(new TrueTypeFontProgramParser().Parse(builder.Build(), context));
        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(("FontProgramInvalid", DiagnosticSeverity.Error), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void A_Length1_longer_than_the_data_is_reported_and_the_program_read()
    {
        byte[] font = new TrueTypeBuilder { Glyphs = { None, Square } }.Build();
        var context = new FontProgramContext { Length1 = font.Length + 100 };
        FontProgram program = Parse(font, context);

        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(program, 1));
        Assert.Equal("FontProgramTruncated", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void Strict_mode_throws_for_a_damaged_glyph_when_its_outline_is_asked_for()
    {
        FontProgram program = Parse(new TrueTypeBuilder { Glyphs = { None, Square[..^3] } }, new FontProgramContext { ReadingMode = PdfReadingMode.Strict });

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => program.GetOutline(1, new GlyphOutline()));
        Assert.Equal("FontGlyphInvalid", exception.Diagnostic.Code);
    }

    [Fact]
    public void Signatures_select_the_parser()
    {
        var parser = new TrueTypeFontProgramParser();
        byte[] font = new TrueTypeBuilder { Glyphs = { None, Square } }.Build();
        byte[] openTypeWithGlyf = new TrueTypeBuilder { Glyphs = { None, Square }, Version = 0x4F54544F }.Build();
        byte[] openTypeCff = TrueTypeBuilder.Sfnt(new Dictionary<string, byte[]> { ["CFF "] = [1, 0, 4, 1], ["head"] = new byte[54] }, 0x4F54544F);

        Assert.True(parser.CanParse(font));
        Assert.True(parser.CanParse(TrueTypeBuilder.Collection(font)));
        Assert.True(parser.CanParse(openTypeWithGlyf));
        Assert.False(parser.CanParse(openTypeCff));
        Assert.False(parser.CanParse("%!PS-AdobeFont-1.0"u8));
        Assert.False(parser.CanParse([0, 1, 0]));
        Assert.Equal(FontProgramFormat.OpenType, Parse(openTypeWithGlyf).Format);
    }

    [Fact]
    public void A_collection_font_is_chosen_by_PostScript_name_then_by_index()
    {
        byte[] first = new TrueTypeBuilder { Glyphs = { None, Square }, Name = "First" }.Build();
        byte[] second = new TrueTypeBuilder { Glyphs = { None, Square, Square }, Name = "Second" }.Build();
        byte[] collection = TrueTypeBuilder.Collection(first, second);

        Assert.Equal(("Second", 3), Describe(Parse(collection, new FontProgramContext { FaceName = "Second" })));
        Assert.Equal(("Second", 3), Describe(Parse(collection, new FontProgramContext { FaceIndex = 1 })));
        Assert.Equal(("First", 2), Describe(Parse(collection, new FontProgramContext { FaceName = "Other" })));
        var context = new FontProgramContext { FaceIndex = 5 };
        Assert.Equal(("First", 2), Describe(Parse(collection, context)));
        Assert.Equal("FontTableInvalid", Assert.Single(context.Diagnostics).Code);
        Assert.Equal("M 0,0 L 0,100 L 100,100 L 100,0 Z", OutlineText.Of(Parse(collection, new FontProgramContext { FaceIndex = 1 }), 2));

        static (string?, int) Describe(FontProgram program) => (program.PostScriptName, program.GlyphCount);
    }

    [Fact]
    public void Cmap_formats_0_2_4_6_12_and_13_map_codes_to_glyphs()
    {
        byte[] cmap = TrueTypeBuilder.CmapTable(
            (1, 0, TrueTypeBuilder.Format0(new Dictionary<int, int> { [0x41] = 2 })),
            (3, 1, TrueTypeBuilder.Format4((0x20, 0x22, 1 - 0x20))),
            (3, 3, TrueTypeBuilder.Format2([0, 1, 2], (0x81, 0x40, [3, 0, 1]))),
            (3, 10, TrueTypeBuilder.Format12(12, (0x10000, 0x10002, 1))),
            (0, 6, TrueTypeBuilder.Format12(13, (0x4E00, 0x4EFF, 3))));
        FontProgram program = Parse(new TrueTypeBuilder { Glyphs = { None, Square, Square, Square }, Cmap = cmap });
        IReadOnlyList<FontCharacterMap> maps = program.CharacterMaps;

        Assert.Equal([(1, 0, 0), (3, 1, 4), (3, 3, 2), (3, 10, 12), (0, 6, 13)], maps.Select(map => (map.PlatformId, map.EncodingId, map.Format)));
        Assert.Equal([2, 0, 0], [maps[0].GetGlyphId(0x41), maps[0].GetGlyphId(0x42), maps[0].GetGlyphId(0x141)]);
        Assert.Equal([1, 3, 0], [maps[1].GetGlyphId(0x20), maps[1].GetGlyphId(0x22), maps[1].GetGlyphId(0x23)]);
        Assert.Equal([1, 2, 3, 0, 1, 0, 0], [maps[2].GetGlyphId(1), maps[2].GetGlyphId(2), maps[2].GetGlyphId(0x8140), maps[2].GetGlyphId(0x8141), maps[2].GetGlyphId(0x8142), maps[2].GetGlyphId(0x8143), maps[2].GetGlyphId(0x81)]);
        Assert.Equal([1, 3, 0], [maps[3].GetGlyphId(0x10000), maps[3].GetGlyphId(0x10002), maps[3].GetGlyphId(0x10003)]);
        Assert.Equal([3, 3, 0], [maps[4].GetGlyphId(0x4E00), maps[4].GetGlyphId(0x4E80), maps[4].GetGlyphId(0x4F00)]);
    }

    [Fact]
    public void Format_4_segments_out_of_order_are_searched_in_order_and_glyphs_past_the_program_read_as_0()
    {
        byte[] cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((0x60, 0x61, 1 - 0x60), (0x41, 0x42, 50 - 0x41))));
        var context = new FontProgramContext();
        FontProgram program = Parse(new TrueTypeBuilder { Glyphs = { None, Square, Square }, Cmap = cmap }, context);
        FontCharacterMap map = Assert.Single(program.CharacterMaps);

        Assert.Equal([1, 2, 0], [map.GetGlyphId(0x60), map.GetGlyphId(0x61), map.GetGlyphId(0x41)]);
        Assert.Equal(["FontCmapInvalid"], context.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Unknown_and_unsupported_cmap_formats_are_left_out_with_a_diagnostic()
    {
        byte[] format8 = [0, 8, 0, 0, 0, 0, 0, 16, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] format14 = [0, 14, 0, 0, 0, 10, 0, 0, 0, 0];
        byte[] cmap = TrueTypeBuilder.CmapTable((3, 1, format8), (0, 5, format14), (3, 0, [0, 99, 0, 6, 0, 0]));
        var context = new FontProgramContext();
        FontProgram program = Parse(new TrueTypeBuilder { Glyphs = { None, Square }, Cmap = cmap }, context);

        Assert.Empty(program.CharacterMaps);
        Assert.Equal(["FontCmapInvalid"], context.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Post_names_come_from_versions_1_2_and_2_5_and_the_first_glyph_of_a_name_wins()
    {
        FontProgram version2 = Parse(new TrueTypeBuilder { Glyphs = { None, Square, Square, Square }, Post = TrueTypeBuilder.Post2(4, ".notdef", "A", "custom", "A") });
        FontProgram version25 = Parse(new TrueTypeBuilder { Glyphs = { None, Square, Square }, Post = TrueTypeBuilder.Post25(0, 35, 35) });
        FontProgram version1 = Parse(new TrueTypeBuilder { Glyphs = { None, Square, Square }, Post = TrueTypeBuilder.PostHeader(0x00010000) });

        Assert.Equal([".notdef", "A", "custom", "A"], Enumerable.Range(0, 4).Select(version2.GetGlyphName));
        Assert.True(version2.TryGetGlyphId("A", out int glyph));
        Assert.Equal(1, glyph);
        Assert.Equal([".notdef", "A", "B"], Enumerable.Range(0, 3).Select(version25.GetGlyphName));
        Assert.Equal([".notdef", ".null", "nonmarkingreturn"], Enumerable.Range(0, 3).Select(version1.GetGlyphName));
        Assert.Null(version1.GetGlyphName(3));
    }

    [Fact]
    public void A_post_table_naming_more_glyphs_than_the_program_has_is_cut_to_the_program()
    {
        var context = new FontProgramContext();
        FontProgram program = Parse(new TrueTypeBuilder { Glyphs = { None, Square }, Post = TrueTypeBuilder.Post2(3, ".notdef", "A", "B") }, context);

        Assert.Equal([".notdef", "A", null], Enumerable.Range(0, 3).Select(program.GetGlyphName));
        Assert.Equal("FontTableInvalid", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void Extracting_outlines_allocates_nothing_once_warm()
    {
        byte[] composite = TrueTypeBuilder.Composite((0, 0, 200, 200), (0x0002 | 0x0008, 1, 10, 20, TrueTypeBuilder.F2Dot14(0.5)), (0x0002, 2, 0, 0, []));
        byte[] curve = TrueTypeBuilder.Simple([(0, 0, false), (100, 0, true), (100, 100, false), (0, 100, false)]);
        FontProgram program = Parse(new TrueTypeBuilder { Glyphs = { None, Square, curve, composite } });
        var outline = new GlyphOutline();
        int segments = default;
        long allocated = Allocations.Measure(() => segments = Outline(program, outline), 50);

        Assert.True(segments > 1_000);
        Assert.Equal(0, allocated);

        static int Outline(FontProgram program, GlyphOutline outline)
        {
            int segments = 0;
            for (int pass = 0; pass < 100; pass++)
            {
                for (int glyph = 0; glyph < program.GlyphCount; glyph++)
                {
                    program.GetOutline(glyph, outline);
                    segments += outline.Path.Verbs.Length;
                }
            }

            return segments;
        }
    }

    private static FontProgram Parse(TrueTypeBuilder builder, FontProgramContext? context = null) => Parse(builder.Build(), context);

    private static FontProgram Parse(byte[] font, FontProgramContext? context = null) =>
        Assert.IsAssignableFrom<FontProgram>(new TrueTypeFontProgramParser().Parse(font, context ?? new FontProgramContext()));

    private static int TableOffset(byte[] font, string tag)
    {
        int count = (font[4] << 8) | font[5];
        for (int index = 0; index < count; index++)
        {
            int record = 12 + (16 * index);
            if (System.Text.Encoding.ASCII.GetString(font, record, 4) == tag)
            {
                return (font[record + 8] << 24) | (font[record + 9] << 16) | (font[record + 10] << 8) | font[record + 11];
            }
        }

        throw new InvalidOperationException(tag);
    }

    private static int TableLength(byte[] font, string tag)
    {
        int count = (font[4] << 8) | font[5];
        for (int index = 0; index < count; index++)
        {
            int record = 12 + (16 * index);
            if (System.Text.Encoding.ASCII.GetString(font, record, 4) == tag)
            {
                return (font[record + 12] << 24) | (font[record + 13] << 16) | (font[record + 14] << 8) | font[record + 15];
            }
        }

        throw new InvalidOperationException(tag);
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
