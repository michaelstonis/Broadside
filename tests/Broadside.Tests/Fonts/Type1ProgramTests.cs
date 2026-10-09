using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.Type1;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The Type 1 parser through the font program contract: program bytes in, outlines, metrics and lookups out, with programs built
/// in memory (<see cref="Type1Builder"/>) so each part can be made malformed. Vectors come from the Adobe Type 1 Font Format: the
/// letter C of §6.6 and its encryption in §7.3, the flex example of §8.3. ISO 32000-2 §9.9.
/// </summary>
public class Type1ProgramTests
{
    private const string Box = "0 500 hsbw 0 0 rmoveto 100 hlineto 100 vlineto -100 hlineto closepath endchar";
    private const string C = "50 800 hsbw 0 100 vstem 0 100 hstem 600 100 hstem 0 hmoveto 700 hlineto 100 vlineto -600 hlineto 500 vlineto 600 hlineto 100 vlineto -700 hlineto closepath endchar";
    private const string COutline = "M 50,0 L 750,0 L 750,100 L 150,100 L 150,600 L 750,600 L 750,700 L 50,700 Z";

    [Fact]
    public void The_charstring_of_the_letter_C_decrypts_and_draws_as_the_specification_shows()
    {
        // Type 1 Font Format §6.6 (37 plaintext bytes) and §7.3 (41 cipher bytes with four zero random bytes, R = 4330).
        byte[] plain = Convert.FromHexString("BDF9B40D8BEF038BEF01F8ECEF018B16F95006EF07FCEC06F88807F8EC06EF07FD5006090E");
        Assert.Equal(plain, Type1Builder.Charstring(C));
        Assert.Equal(
            Convert.FromHexString("10BF31704FAB5B1F03F9B68B1F39A66521B1841F1481697F8E12B7F7DDD6E3D7248D965B1CD45E2114"),
            Type1Builder.Encrypt([0, 0, 0, 0, .. plain], 4330));

        FontProgram program = Parse(Builder().Glyph("C", C));

        Assert.Equal(COutline, OutlineText.Of(program, 1));
        Assert.Equal(new GlyphMetrics(800, 50), program.GetMetrics(1));
    }

    [Fact]
    public void The_flex_example_draws_its_two_curves()
    {
        // Type 1 Font Format §8.3, Figure 8e: start (100, −10), reference point (150, −10), end (200, −10).
        FontProgram program = Parse(Builder().Glyph(
            "flex",
            "0 300 hsbw 100 -10 rmoveto 1 callsubr 50 0 rmoveto 2 callsubr -35 0 rmoveto 2 callsubr 10 10 rmoveto 2 callsubr " +
            "25 0 rmoveto 2 callsubr 25 0 rmoveto 2 callsubr 10 -10 rmoveto 2 callsubr 15 0 rmoveto 2 callsubr 50 200 -10 0 callsubr " +
            "-100 hlineto closepath endchar")
            .Subr("3 0 callothersubr pop pop setcurrentpoint return") // Subrs 0-3 as §8.4 requires
            .Subr("0 1 callothersubr return")
            .Subr("0 2 callothersubr return")
            .Subr("return"));

        Assert.Equal("M 100,-10 C 115,-10 125,0 150,0 C 175,0 185,-10 200,-10 L 100,-10 Z", OutlineText.Of(program, 1));
    }

    public static TheoryData<string, string> Layouts => new() { { "pdf", "PDF layout" }, { "pfb", "PFB" }, { "pfa", "PFA" } };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void PDF_PFB_and_PFA_layouts_read_alike_and_stand_alone_without_diagnostics(string layout, string description)
    {
        Type1Builder builder = Builder().Glyph("C", C);
        byte[] bytes = layout switch { "pfb" => builder.BuildPfb(), "pfa" => builder.BuildPfa(), _ => builder.Build().Program };
        var context = new FontProgramContext();

        Assert.True(new Type1FontProgramParser().CanParse(bytes), description);
        FontProgram program = Parse(bytes, context);

        Assert.Equal(COutline, OutlineText.Of(program, 1));
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void A_PFB_inside_a_document_and_hexadecimal_eexec_are_reported()
    {
        Type1Builder builder = Builder().Glyph("C", C);
        var pfb = new FontProgramContext { Source = FontProgramSource.FontFile };
        var pfa = new FontProgramContext { Source = FontProgramSource.FontFile };

        Assert.Equal(COutline, OutlineText.Of(Parse(builder.BuildPfb(), pfb), 1));
        Assert.Equal(COutline, OutlineText.Of(Parse(builder.BuildPfa(), pfa), 1));

        Assert.Equal(["FontType1PfbWrapped"], Codes(pfb));
        Assert.Equal(["FontType1Length1Repaired", "FontType1Length2Repaired", "FontType1HexEexec"], Codes(pfa));
    }

    [Fact]
    public void Wrong_lengths_are_repaired_from_the_eexec_token()
    {
        (byte[] program, int length1, _, _) = Builder().Glyph("C", C).Build();
        var context = new FontProgramContext { Source = FontProgramSource.FontFile, Length1 = length1 - 3, Length2 = -1, Length3 = 0 };

        Assert.Equal(COutline, OutlineText.Of(Parse(program, context), 1));
        Assert.Equal(["FontType1Length1Repaired", "FontType1Length2Repaired"], Codes(context));
    }

    [Fact]
    public void Exact_lengths_are_used_as_given()
    {
        (byte[] program, int length1, int length2, int length3) = Builder().Glyph("C", C).Build();
        var context = new FontProgramContext { Source = FontProgramSource.FontFile, Length1 = length1, Length2 = length2, Length3 = length3 };

        Assert.Equal(COutline, OutlineText.Of(Parse(program, context), 1));
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void A_program_without_eexec_is_read_as_clear_text()
    {
        // The private dictionary in clear text; its charstrings are still encrypted with lenIV bytes.
        Type1Builder builder = Builder().Glyph("C", C);
        byte[] clear = builder.ClearText();
        byte[] program = [.. clear[..^"currentfile eexec\n".Length], .. builder.PrivateText()];
        var context = new FontProgramContext();

        Assert.Equal(COutline, OutlineText.Of(Parse(program, context), 1));
        Assert.Equal(["FontType1NoEexec"], Codes(context));
    }

    [Fact]
    public void Plaintext_charstrings_with_lenIV_minus_1_and_other_lenIV_values_are_read()
    {
        Assert.Equal(COutline, OutlineText.Of(Parse(Builder(lenIV: -1).Glyph("C", C)), 1));
        Assert.Equal(COutline, OutlineText.Of(Parse(Builder(lenIV: 0).Glyph("C", C)), 1));
        Assert.Equal(COutline, OutlineText.Of(Parse(Builder(lenIV: null).Glyph("C", C)), 1));
    }

    [Fact]
    public void The_alternate_procedure_names_and_missing_terminators_are_accepted()
    {
        Type1Builder alternate = Builder().Glyph("C", C);
        alternate.Procedures = ("-|", "|-", "|");
        Assert.Equal(COutline, OutlineText.Of(Parse(alternate), 1));

        // Any executable name after the length is taken as RD (pdf.js does the same); a missing ND is tolerated.
        Type1Builder odd = Builder().Glyph("C", C);
        odd.Procedures = ("XX", "", "");
        Assert.Equal(COutline, OutlineText.Of(Parse(odd), 1));
    }

    [Fact]
    public void A_custom_encoding_glued_tokens_comments_and_radix_numbers_are_tokenized()
    {
        Type1Builder builder = Builder().Glyph("C", C).Glyph("D", Box);
        builder.FontMatrix = "{1e-3 0 0 .001 0 0}readonly def % a procedure, reals with an exponent\n";
        builder.Encoding = "256 array 0 1 255{1 index exch/.notdef put}for\ndup 8#101/C put dup 66 /D put dup 67 / put\n% comment\ndup 300 /D put readonly def\n";
        var context = new FontProgramContext();
        FontProgram program = Parse(builder, context);

        Assert.Equal("[0.001 0 0 0.001 0 0]", program.FontMatrix.ToString());
        Assert.Equal(["C", "D", ".notdef"], [program.BuiltInEncoding![65], program.BuiltInEncoding[66], program.BuiltInEncoding[67]]);
        Assert.Equal(["FontType1EncodingInvalid"], Codes(context));
    }

    [Fact]
    public void StandardEncoding_by_name_and_an_unusable_FontMatrix_fall_back_to_the_defaults()
    {
        Type1Builder builder = Builder().Glyph("C", C);
        builder.FontMatrix = "[0.001 0 0] def\n";
        var context = new FontProgramContext();
        FontProgram program = Parse(builder, context);

        Assert.Equal("C", program.BuiltInEncoding![67]);
        Assert.Equal("quoteright", program.BuiltInEncoding[39]);
        Assert.Equal("[0.001 0 0 0.001 0 0]", program.FontMatrix.ToString());
        Assert.Equal(["FontType1FontMatrixInvalid"], Codes(context));
    }

    [Fact]
    public void Glyph_ids_put_notdef_first_and_a_missing_notdef_is_synthesized()
    {
        var builder = new Type1Builder();
        builder.Glyph("A", Box).Glyph("B", Box);
        var context = new FontProgramContext();
        FontProgram program = Parse(builder, context);

        Assert.Equal([".notdef", "A", "B"], Enumerable.Range(0, program.GlyphCount).Select(program.GetGlyphName).ToArray());
        Assert.Equal("Empty", OutlineText.Of(program, 0));
        Assert.Equal(["FontType1NotdefMissing"], Codes(context));

        var late = new Type1Builder();
        late.Glyph("A", Box).Glyph(".notdef", "0 250 hsbw endchar");
        FontProgram reordered = Parse(late);
        Assert.Equal([".notdef", "A"], Enumerable.Range(0, reordered.GlyphCount).Select(reordered.GetGlyphName).ToArray());
        Assert.Equal(new GlyphMetrics(250, 0), reordered.GetMetrics(0));
    }

    [Fact]
    public void Hybrid_fonts_use_the_first_Subrs_and_CharStrings()
    {
        Type1Builder builder = Builder().Glyph("C", "0 800 hsbw 0 callsubr endchar").Subr("0 0 rmoveto 100 hlineto 100 vlineto closepath return");
        builder.Trailer = "/Subrs 1 array\ndup 0 3 RD xxx NP\n2 index /CharStrings 1 dict dup begin\n/C 3 RD xxx ND\nend\n";

        Assert.Equal("M 0,0 L 100,0 L 100,100 Z", OutlineText.Of(Parse(builder), 1));
    }

    [Fact]
    public void A_program_without_CharStrings_is_unusable()
    {
        var context = new FontProgramContext();
        byte[] program = [.. new Type1Builder().ClearText(), .. Type1Builder.Encrypt([1, 2, 3, 4, .. "dup /Private 1 dict dup begin end"u8], 55665)];

        Assert.Null(new Type1FontProgramParser().Parse(program, context));
        Assert.Equal(["FontType1CharStringsMissing"], Codes(context));
        Assert.Equal(DiagnosticSeverity.Error, context.Diagnostics[0].Severity);
    }

    [Fact]
    public void An_OtherSubr_the_reader_does_not_implement_returns_its_arguments_in_order()
    {
        // Type 1 Font Format §8.2: pop gets arg1 first; here the two pops feed rmoveto.
        FontProgram program = Parse(Builder().Glyph("x", "0 500 hsbw 30 40 2 99 callothersubr pop pop rmoveto 100 hlineto closepath endchar"));

        Assert.Equal("M 30,40 L 130,40 Z", OutlineText.Of(program, 1));
    }

    [Fact]
    public void Multiple_master_blends_weigh_the_deltas_with_the_WeightVector()
    {
        // TN 5015 §3.13: OtherSubr 14 blends one value of two masters: 100 + 200 × 0.75; OtherSubr 15 two values.
        Type1Builder builder = Builder().Glyph("x", "0 500 hsbw 0 0 rmoveto 100 200 2 14 callothersubr pop hlineto 10 20 40 80 4 15 callothersubr pop pop rlineto closepath endchar");
        builder.ClearTextExtra = "/WeightVector [0.25 0.75] def\n";
        FontProgram program = Parse(builder);

        Assert.Equal("M 0,0 L 250,0 L 290,80 Z", OutlineText.Of(program, 1));

        var context = new FontProgramContext();
        Assert.Equal("M 0,0 L 100,0 L 110,20 Z", OutlineText.Of(Parse(Builder().Glyph("x", "0 500 hsbw 0 0 rmoveto 100 200 2 14 callothersubr pop hlineto 10 20 40 80 4 15 callothersubr pop pop rlineto closepath endchar"), context), 1));
        Assert.Equal(["FontType1BlendUnavailable"], Codes(context));
    }

    [Fact]
    public void Counter_control_OtherSubrs_and_hint_commands_leave_the_outline_alone()
    {
        FontProgram program = Parse(Builder().Glyph(
            "x",
            "0 500 hsbw 1 2 3 3 12 callothersubr 0 13 callothersubr 0 10 20 10 40 10 vstem3 0 10 20 10 40 10 hstem3 dotsection 0 0 rmoveto 100 hlineto closepath endchar"));

        Assert.Equal("M 0,0 L 100,0 Z", OutlineText.Of(program, 1));
    }

    [Fact]
    public void A_command_with_extra_operands_takes_the_last_ones_and_consecutive_moves_make_no_empty_contour()
    {
        FontProgram program = Parse(Builder().Glyph("x", "0 500 hsbw 7 8 10 20 rmoveto 5 5 rmoveto 0 hmoveto 100 hlineto 100 vlineto closepath 50 vmoveto endchar"));

        Assert.Equal("M 15,25 L 115,25 L 115,125 Z", OutlineText.Of(program, 1));
    }

    [Fact]
    public void A_contour_left_open_is_closed_by_the_next_move_and_by_endchar()
    {
        FontProgram program = Parse(Builder().Glyph("x", "0 500 hsbw 0 0 rmoveto 100 hlineto 100 vlineto 200 0 rmoveto 50 hlineto endchar"));

        Assert.Equal("M 0,0 L 100,0 L 100,100 Z M 300,100 L 350,100 Z", OutlineText.Of(program, 1));
    }

    [Fact]
    public void Seac_places_the_accent_by_its_left_side_bearing_and_names_components_by_StandardEncoding()
    {
        // TN 5015 §6 errata: the accent (sbx 30) lands with its side bearing point at the composite's (10) + (adx, ady) = (110, 200).
        Type1Builder builder = Builder()
            .Glyph("A", "10 600 hsbw 0 0 rmoveto 100 hlineto closepath endchar")
            .Glyph("grave", "30 300 hsbw 0 0 rmoveto 50 hlineto closepath endchar")
            .Glyph("Agrave", "10 600 hsbw 30 100 200 65 193 seac");

        FontProgram program = Parse(builder);

        Assert.Equal("M 10,0 L 110,0 Z M 110,200 L 160,200 Z", OutlineText.Of(program, 3));
        Assert.Equal(new GlyphMetrics(600, 10), program.GetMetrics(3));
    }

    public static TheoryData<string, string, string> Repairs => new()
    {
        // charstring, outline, diagnostic code
        { "0 500 hsbw 0 0 rmoveto 100 hlineto raw:00 100 vlineto closepath endchar", "M 0,0 L 100,0 L 100,100 Z", "FontType1UnknownOperator" },
        { "0 500 hsbw 0 0 rmoveto 100 0 div hlineto 100 vlineto closepath endchar", "M 0,0 L 0,0 L 0,100 Z", "FontType1DivideByZero" },
        { "0 500 hsbw 0 0 rmoveto hlineto 100 vlineto closepath endchar", "M 0,0 L 0,100 Z", "FontType1StackUnderflow" },
        { "0 500 hsbw 0 0 rmoveto pop hlineto closepath endchar", "M 0,0 L 0,0 Z", "FontType1StackUnderflow" },
        { "0 500 hsbw 0 0 rmoveto 100 hlineto closepath", "M 0,0 L 100,0 Z", "FontType1EndcharMissing" },
        { "0 0 rmoveto 100 hlineto closepath endchar", "M 0,0 L 100,0 Z", "FontType1NoWidth" },
        { "0 500 hsbw 0 0 rmoveto 0 2 callothersubr 100 hlineto closepath endchar", "M 0,0 L 100,0 Z", "FontType1FlexMalformed" },
        { "0 500 hsbw 0 0 rmoveto 0 1 callothersubr 50 0 0 3 0 callothersubr pop pop setcurrentpoint 100 hlineto closepath endchar", "M 0,0 L 100,0 Z", "FontType1FlexMalformed" },
        { "0 500 hsbw 0 0 0 65 66 seac", "M 0,0 L 100,0 L 100,100 L 0,100 Z", "FontType1SeacMissingComponent" },
        { "0 500 hsbw 0 0 0 65 67 seac", "M 0,0 L 100,0 L 100,100 L 0,100 Z", "FontType1SeacNested" },
        { "0 500 hsbw 0 0 rmoveto 100 hlineto 0 return closepath endchar", "M 0,0 L 100,0 Z", "FontType1UnknownOperator" },
    };

    [Theory]
    [MemberData(nameof(Repairs))]
    public void Small_charstring_deviations_are_repaired_and_reported(string charstring, string outline, string code)
    {
        var context = new FontProgramContext();
        FontProgram program = Parse(
            Builder().Glyph("A", Box).Glyph("C", "0 500 hsbw 0 0 0 65 65 seac").Glyph("x", charstring),
            context);

        Assert.Equal(outline, OutlineText.Of(program, 3));
        Assert.Equal([code], Codes(context));
        Assert.All(context.Diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));
    }

    public static TheoryData<string, string> Failures => new()
    {
        { "0 500 hsbw 0 0 rmoveto 100 hlineto 5 callsubr endchar", "FontType1SubrMissing" },
        { "0 500 hsbw 0 0 rmoveto 100 hlineto 0 callsubr endchar", "FontType1SubrDepthExceeded" },
        { "0 500 hsbw 0 0 rmoveto 100 hlineto raw:FF raw:00", "FontType1CharstringTruncated" },
        { "0 500 hsbw 0 0 rmoveto 100 raw:F7", "FontType1CharstringTruncated" },
        { "0 500 hsbw 0 0 rmoveto 100 hlineto raw:0C", "FontType1CharstringTruncated" },
        { "0 500 hsbw " + string.Join(' ', Enumerable.Repeat("1", 49)) + " endchar", "FontType1StackOverflow" },
        { "0 500 hsbw callsubr endchar", "FontType1StackUnderflow" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void A_charstring_that_cannot_be_finished_drops_the_glyph(string charstring, string code)
    {
        var context = new FontProgramContext();
        FontProgram program = Parse(Builder().Glyph("x", charstring).Subr("0 0 rmoveto 0 callsubr return"), context);
        var outline = new GlyphOutline();

        Assert.Equal(GlyphOutlineStatus.Invalid, program.GetOutline(1, outline));
        Assert.True(outline.IsEmpty);
        Assert.Equal([code], Codes(context));
        Assert.Equal(DiagnosticSeverity.Error, context.Diagnostics[0].Severity);
    }

    [Fact]
    public void Fan_out_through_subroutines_is_bounded()
    {
        // Each subroutine calls the next one ten times: 10^12 calls unbounded.
        Type1Builder builder = Builder().Glyph("x", "0 500 hsbw 0 callsubr endchar");
        for (int subr = 0; subr < 12; subr++)
        {
            builder.Subr(string.Concat(Enumerable.Repeat($"{subr + 1} callsubr ", 10)) + "return");
        }

        builder.Subr("1 0 rlineto return");
        var context = new FontProgramContext();

        Assert.Equal("Invalid", OutlineText.Of(Parse(builder, context), 1));
        Assert.Contains(Codes(context), code => code is "FontType1GlyphTooComplex");
    }

    [Fact]
    public void Strict_mode_throws_from_GetOutline_for_a_charstring_deviation()
    {
        FontProgram program = Parse(Builder().Glyph("x", "0 500 hsbw 0 0 rmoveto 100 0 div hlineto endchar"), new FontProgramContext { ReadingMode = PdfReadingMode.Strict });

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => program.GetOutline(1, new GlyphOutline()));
        Assert.Equal("FontType1DivideByZero", exception.Diagnostic.Code);
    }

    [Fact]
    public void CanParse_accepts_a_Type_1_signature_or_a_PFB_header_only()
    {
        var parser = new Type1FontProgramParser();

        Assert.True(parser.CanParse("%!FontType1-1.0: X"u8));
        Assert.True(parser.CanParse([0x80, 0x01, 0x10, 0, 0, 0]));
        Assert.False(parser.CanParse([0x00, 0x01, 0x00, 0x00]));
        Assert.False(parser.CanParse([0x01, 0x00, 0x04, 0x02]));
        Assert.False(parser.CanParse("OTTO"u8));
        Assert.Equal([FontProgramFormat.Type1], parser.Formats);
    }

    [Fact]
    public void Truncated_and_malformed_PFB_segments_keep_what_they_hold()
    {
        byte[] pfb = Builder().Glyph("C", C).BuildPfb();
        var context = new FontProgramContext();
        FontProgram program = Parse(pfb[..^30], context);

        Assert.Equal(COutline, OutlineText.Of(program, 1));
        Assert.Equal(["FontType1PfbWrapped"], Codes(context));
    }

    [Fact]
    public void Extracting_outlines_and_metrics_allocates_nothing_once_warm()
    {
        (byte[] bytes, _, _, _) = Builder()
            .Glyph("C", C)
            .Glyph("A", "10 600 hsbw 0 0 rmoveto 100 hlineto 1 callsubr closepath endchar")
            .Glyph("grave", "30 300 hsbw 0 0 rmoveto 50 hlineto closepath endchar")
            .Glyph("Agrave", "10 600 hsbw 30 100 200 65 193 seac")
            .Glyph("flex", "0 300 hsbw 100 -10 rmoveto 1 0 1 callothersubr 50 0 rmoveto 0 2 callothersubr -35 0 rmoveto 0 2 callothersubr 10 10 rmoveto 0 2 callothersubr 25 0 rmoveto 0 2 callothersubr 25 0 rmoveto 0 2 callothersubr 10 -10 rmoveto 0 2 callothersubr 15 0 rmoveto 0 2 callothersubr 50 200 -10 3 0 callothersubr pop pop setcurrentpoint closepath endchar")
            .Subr("return")
            .Subr("100 vlineto -100 hlineto return")
            .Build();
        FontProgram program = Parse(bytes);
        var outline = new GlyphOutline();
        for (int warm = 0; warm < 50; warm++)
        {
            Outline(program, outline);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int segments = Outline(program, outline);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

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
                    segments += outline.Path.Verbs.Length + (int)program.GetMetrics(glyph).AdvanceWidth % 2;
                }
            }

            return segments;
        }
    }

    private static Type1Builder Builder(int? lenIV = 4)
    {
        var builder = new Type1Builder { LenIV = lenIV };
        builder.Glyph(".notdef", "0 250 hsbw endchar");
        return builder;
    }

    private static string[] Codes(FontProgramContext context) => [.. context.Diagnostics.Select(diagnostic => diagnostic.Code)];

    private static FontProgram Parse(Type1Builder builder, FontProgramContext? context = null) => Parse(builder.Build().Program, context);

    private static FontProgram Parse(byte[] program, FontProgramContext? context = null) =>
        Assert.IsAssignableFrom<FontProgram>(new Type1FontProgramParser().Parse(program, context ?? new FontProgramContext()));
}
