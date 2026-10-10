using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.Cff;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The CFF parser through the font program contract: bytes in, names, encodings, metrics and outlines out. Vectors are the example
/// font of Adobe Technical Note #5176 Appendix D and programs built in memory with <see cref="CffBuilder"/>. ISO 32000-2 §9.9.
/// </summary>
public sealed class CffProgramTests
{
    /// <summary>5176 Appendix D (p.51): the 147-byte example font, a renamed Times subset with .notdef and space.</summary>
    private static readonly byte[] AppendixD = Convert.FromHexString(
        "0100040100010101134142434445462B"
        + "54696D65732D526F6D616E000101011F"
        + "F81B00F81C02F81D03F819041C6F000D"
        + "FB3CFB6EFA7CFA1605E911B8F1120003"
        + "01010813183030312E30303754696D65"
        + "7320526F6D616E54696D657300000002"
        + "010102030E0E7D99F92A99FB7695F773"
        + "8B06F79A93FC7C8C077D99F85695F75E"
        + "9908FB6E8CF87393F7108B09A70ADF0B"
        + "F78E14");

    public static TheoryData<byte[], string, string, string> RangeCharsets => new()
    {
        { [1, 0, 34, 2], "A", "B", "C" },
        { [2, 0, 66, 0, 1, 0, 35, 0, 0], "a", "b", "B" },
    };

    public static TheoryData<byte[], string> UnusablePrograms => new()
    {
        { [1, 0, 4, 1, 0, 0], "FontProgramInvalid" },
        { [0x25, 0x21, 0x50, 0x53], "FontCffHeaderInvalid" },
        { [1, 0, 4, 1, 0, 1, 1, 1, 9, 0, 1], "FontCffIndexInvalid" },
    };

    [Fact]
    public void The_example_font_of_the_CFF_specification_reads_with_its_names_box_and_default_width()
    {
        var context = new FontProgramContext();
        FontProgram program = Parse(AppendixD, context);

        Assert.Equal(147, AppendixD.Length);
        Assert.Equal(FontProgramFormat.Cff, program.Format);
        Assert.Equal(2, program.GlyphCount);
        Assert.Equal("ABCDEF+Times-Roman", program.PostScriptName);
        Assert.Equal(new PdfRectangle(-168, -218, 1000, 898), program.FontBBox);
        Assert.Equal(0.001, program.FontMatrix.A, 9);
        Assert.Equal(0.001, program.FontMatrix.D, 9);
        Assert.Equal(".notdef", program.GetGlyphName(0));
        Assert.Equal("space", program.GetGlyphName(1));
        Assert.True(program.TryGetGlyphId("space", out int space));
        Assert.Equal(1, space);
        Assert.Equal(new GlyphMetrics(250, 0), program.GetMetrics(1));
        Assert.Equal("Empty", OutlineText.Of(program, 1));
        IReadOnlyList<string> encoding = Assert.IsAssignableFrom<IReadOnlyList<string>>(program.BuiltInEncoding);
        Assert.Equal(256, encoding.Count);
        Assert.Equal("space", encoding[32]);
        Assert.Equal("A", encoding[65]);
        Assert.Equal(".notdef", encoding[0]);
        Assert.Equal([1, 0], new[] { 32, 65 }.Select(code => Encoded(program, code)));
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void A_custom_format_0_encoding_maps_codes_to_glyphs_and_a_supplement_adds_a_second_code()
    {
        // Format 0x80 | 0: two codes for glyphs 1 and 2, then one supplement giving code 0x61 to SID 34 (A, glyph 1).
        FontProgram program = Parse(Glyphs(new CffBuilder { PredefinedEncoding = null, Encoding = [0x80, 2, 0x41, 0x42, 1, 0x61, 0, 34] }, "A", "B").Build());

        Assert.Equal("A", program.BuiltInEncoding![0x41]);
        Assert.Equal("B", program.BuiltInEncoding[0x42]);
        Assert.Equal("A", program.BuiltInEncoding[0x61]);
        Assert.Equal(".notdef", program.BuiltInEncoding[0x43]);
        Assert.Equal([1, 2, 1, 0], new[] { 0x41, 0x42, 0x61, 0x43 }.Select(code => Encoded(program, code)));
    }

    [Fact]
    public void A_custom_format_1_encoding_maps_code_ranges_to_consecutive_glyphs()
    {
        // Ranges (0x30, 1 more) and (0x41, 0 more): codes 0x30, 0x31, 0x41 for glyphs 1, 2, 3.
        FontProgram program = Parse(Glyphs(new CffBuilder { PredefinedEncoding = null, Encoding = [1, 2, 0x30, 1, 0x41, 0] }, "zero", "one", "A").Build());

        Assert.Equal([1, 2, 3, 0], new[] { 0x30, 0x31, 0x41, 0x32 }.Select(code => Encoded(program, code)));
        Assert.Equal(["zero", "one", "A"], new[] { 0x30, 0x31, 0x41 }.Select(code => program.BuiltInEncoding![code]));
    }

    [Fact]
    public void The_Expert_encoding_names_codes_by_the_CFF_specification_table()
    {
        FontProgram program = Parse(Glyphs(new CffBuilder { PredefinedEncoding = 1 }, "space").Build());

        Assert.Equal("exclamsmall", program.BuiltInEncoding![0x21]);
        Assert.Equal("Asmall", program.BuiltInEncoding[0x61]);
        Assert.Equal("space", program.BuiltInEncoding[0x20]);
        Assert.Equal([1, 0], new[] { 0x20, 0x21 }.Select(code => Encoded(program, code)));
    }

    [Theory]
    [InlineData(1, "space", "exclamsmall", "Hungarumlautsmall")]
    [InlineData(2, "space", "dollaroldstyle", "dollarsuperior")]
    [InlineData(0, "space", "exclam", "quotedbl")]
    public void Predefined_charsets_name_glyphs_by_the_CFF_specification_tables(int charset, string first, string second, string third)
    {
        FontProgram program = Parse(Glyphs(new CffBuilder { PredefinedCharset = charset }, "x", "x", "x").Build());

        Assert.Equal([".notdef", first, second, third], Enumerable.Range(0, 4).Select(program.GetGlyphName));
    }

    [Theory]
    [MemberData(nameof(RangeCharsets))]
    public void Range_charsets_name_consecutive_SIDs(byte[] charset, string first, string second, string third)
    {
        FontProgram program = Parse(Glyphs(new CffBuilder { Charset = charset }, "x", "x", "x").Build());

        Assert.Equal([".notdef", first, second, third], Enumerable.Range(0, 4).Select(program.GetGlyphName));
        Assert.True(program.TryGetGlyphId(third, out int glyph));
        Assert.Equal(3, glyph);
    }

    [Fact]
    public void Glyph_names_beyond_the_standard_strings_come_from_the_String_INDEX()
    {
        FontProgram program = Parse(Glyphs(new CffBuilder(), "brds.alt", "uni2603").Build());

        Assert.Equal("brds.alt", program.GetGlyphName(1));
        Assert.Equal("uni2603", program.GetGlyphName(2));
        Assert.True(program.TryGetGlyphId("uni2603", out int glyph));
        Assert.Equal(2, glyph);
    }

    [Fact]
    public void Real_numbers_in_a_DICT_are_packed_decimal_nibbles()
    {
        // 5176 Table 5 examples: 1e e2 a2 5f is -2.25, 1e 0a 14 05 41 c3 ff is 0.140541E-3.
        byte[] minus225 = [0x1E, 0xE2, 0xA2, 0x5F];
        byte[] small = [0x1E, 0x0A, 0x14, 0x05, 0x41, 0xC3, 0xFF];
        byte[] zero = [0x8B];
        var builder = new CffBuilder
        {
            TopDictPrefix = [.. minus225, .. minus225, .. zero, .. zero, 5, .. small, .. zero, .. zero, .. small, .. zero, .. zero, 12, 7],
        };

        var context = new FontProgramContext();
        FontProgram program = Parse(builder.Build(), context);

        Assert.Equal(new PdfRectangle(-2.25, -2.25, 0, 0), program.FontBBox);
        Assert.Equal(0.140541e-3, program.FontMatrix.A, 12);
        Assert.Equal(0.140541e-3, program.FontMatrix.D, 12);
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void A_non_standard_FontMatrix_is_carried_with_the_outlines()
    {
        FontProgram program = Parse(new CffBuilder { FontMatrix = [0.0005, 0, 0, 0.0005, 0, 0] }.Build());

        Assert.Equal(0.0005, program.FontMatrix.A, 12);
        Assert.Equal(0.0005, program.FontMatrix.D, 12);
    }

    [Fact]
    public void An_OpenType_program_reads_its_CFF_table_and_keeps_its_cmap_subtables()
    {
        byte[] cff = Glyphs(new CffBuilder(), "A").Build();
        byte[] cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((0x41, 0x41, 1 - 0x41))));
        byte[] font = CffBuilder.OpenType(cff, new Dictionary<string, byte[]> { ["cmap"] = cmap });

        var context = new FontProgramContext();
        FontProgram program = Parse(font, context);

        Assert.Equal(FontProgramFormat.OpenType, program.Format);
        Assert.Equal("BroadsideTest", program.PostScriptName);
        Assert.Equal(2, program.GlyphCount);
        Assert.Equal(1, Assert.Single(program.CharacterMaps).GetGlyphId(0x41));
        Assert.Equal("A", program.GetGlyphName(1));
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void An_OpenType_program_with_CFF2_outlines_is_not_read()
    {
        byte[] font = CffBuilder.OpenType([2, 0, 5, 0, 0], new Dictionary<string, byte[]>());
        System.Text.Encoding.ASCII.GetBytes("CFF2").CopyTo(font, 12);
        var parser = new CffFontProgramParser();
        var context = new FontProgramContext();

        Assert.True(parser.CanParse(font));
        Assert.Null(parser.Parse(font, context));
        Assert.Equal("FontCff2Unsupported", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void A_CID_keyed_program_has_no_glyph_names_or_built_in_encoding()
    {
        // ROS (12 30) with SIDs 391 and 392 and supplement 0: the font is CID-keyed (5176 §18).
        var builder = Glyphs(new CffBuilder { PredefinedEncoding = 0, TopDictPrefix = [.. CffBuilder.DictInteger(391), .. CffBuilder.DictInteger(392), 139, 12, 30] }, "x");
        builder.Strings.AddRange(["Adobe", "Identity"]);
        var context = new FontProgramContext();
        FontProgram program = Parse(builder.Build(), context);

        Assert.Equal(2, program.GlyphCount);
        Assert.Null(program.BuiltInEncoding);
        Assert.Null(program.GetGlyphName(1));
        Assert.Equal("Empty", OutlineText.Of(program, 1));
        Assert.Equal(["FontCffFdArrayMissing"], context.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void A_Private_DICT_past_the_end_of_the_program_falls_back_to_its_defaults()
    {
        byte[] font = Glyphs(new CffBuilder { DefaultWidthX = 300 }, "A").Build();
        byte[] truncated = font[..^1];

        var context = new FontProgramContext();
        FontProgram program = Parse(truncated, context);

        Assert.Equal("Empty", OutlineText.Of(program, 1));
        Assert.Equal("FontCffPrivateMissing", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void A_program_without_a_Private_DICT_uses_its_defaults_with_a_diagnostic()
    {
        var context = new FontProgramContext();
        FontProgram program = Parse(Glyphs(new CffBuilder { WritePrivate = false }, "A").Build(), context);

        Assert.Equal(new GlyphMetrics(0, 0), program.GetMetrics(1));
        Assert.Equal("FontCffPrivateMissing", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void Junk_before_the_header_is_skipped_with_a_diagnostic()
    {
        byte[] font = [0, 0, 0, .. AppendixD];
        var context = new FontProgramContext();

        FontProgram? program = new CffFontProgramParser().Parse(font, context);

        Assert.Equal(2, program?.GlyphCount);
        Assert.Equal("FontCffHeaderInvalid", Assert.Single(context.Diagnostics).Code);
    }

    [Theory]
    [MemberData(nameof(UnusablePrograms))]
    public void An_unusable_program_gives_no_program_and_a_diagnostic(byte[] font, string code)
    {
        var context = new FontProgramContext();

        Assert.Null(new CffFontProgramParser().Parse(font, context));
        Assert.Contains(context.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void A_damaged_INDEX_is_clamped_with_a_diagnostic()
    {
        // The CharStrings INDEX's second offset points far past the data: glyph 0 is cut at the end of the program.
        byte[] font = Glyphs(new CffBuilder(), "A").Build();
        int index = font.AsSpan().LastIndexOf(new byte[] { 0, 2, 1, 1, 2, 3 });
        Assert.True(index > 0);
        font[index + 4] = 0xF0;

        var context = new FontProgramContext();
        FontProgram program = Parse(font, context);

        Assert.Equal(2, program.GlyphCount);
        Assert.Contains(context.Diagnostics, diagnostic => diagnostic.Code == "FontCffIndexInvalid");
    }

    [Fact]
    public void Strict_mode_throws_for_a_damaged_program()
    {
        byte[] font = Glyphs(new CffBuilder { WritePrivate = false }, "A").Build();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => new CffFontProgramParser().Parse(font, new FontProgramContext { ReadingMode = PdfReadingMode.Strict }));

        Assert.Equal("FontCffPrivateMissing", exception.Diagnostic.Code);
    }

    /// <summary>The glyph the program's built-in encoding names for a code (§9.6.5.2: by name); 0 when none.</summary>
    private static int Encoded(FontProgram program, int code) =>
        program.BuiltInEncoding is { } encoding && program.TryGetGlyphId(encoding[code], out int glyph) ? glyph : 0;

    private static CffBuilder Glyphs(CffBuilder builder, params string[] names)
    {
        builder.Glyphs.Add((".notdef", CffBuilder.T2("endchar")));
        foreach (string name in names)
        {
            builder.Glyphs.Add((name, CffBuilder.T2("endchar")));
        }

        return builder;
    }

    private static FontProgram Parse(byte[] font, FontProgramContext? context = null)
    {
        var parser = new CffFontProgramParser();
        Assert.True(parser.CanParse(font));
        return Assert.IsAssignableFrom<FontProgram>(parser.Parse(font, context ?? new FontProgramContext()));
    }
}
