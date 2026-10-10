using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.Cff;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Type 2 charstrings through the font program contract: one charstring per case wrapped in a synthesized CFF program, its outline
/// compared with the absolute points the operator definitions of Adobe Technical Note #5177 give (worked by hand in each case).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class Type2CharStringTests
{
    public static TheoryData<string, object[], string> PathOperators => new()
    {
        { "rmoveto rlineto", [10, 20, "rmoveto", 100, 0, 0, 50, "rlineto"], "M 10,20 L 110,20 L 110,70 Z" },
        { "hmoveto hlineto", [10, "hmoveto", 100, 50, -100, "hlineto"], "M 10,0 L 110,0 L 110,50 L 10,50 Z" },
        { "vmoveto vlineto", [10, "vmoveto", 50, 100, -50, "vlineto"], "M 0,10 L 0,60 L 100,60 L 100,10 Z" },
        { "rrcurveto", [0, 0, "rmoveto", 10, 20, 30, 40, 50, 60, 1, 2, 3, 4, 5, 6, "rrcurveto"], "M 0,0 C 10,20 40,60 90,120 C 91,122 94,126 99,132 Z" },
        { "hhcurveto dy1", [0, 0, "rmoveto", 5, 10, 20, 30, 40, "hhcurveto"], "M 0,0 C 10,5 30,35 70,35 Z" },
        { "hhcurveto twice", [0, 0, "rmoveto", 10, 20, 30, 40, 1, 2, 3, 4, "hhcurveto"], "M 0,0 C 10,0 30,30 70,30 C 71,30 73,33 77,33 Z" },
        { "vvcurveto dx1", [0, 0, "rmoveto", 5, 10, 20, 30, 40, "vvcurveto"], "M 0,0 C 5,10 25,40 25,80 Z" },
        { "vvcurveto twice", [0, 0, "rmoveto", 10, 20, 30, 40, 1, 2, 3, 4, "vvcurveto"], "M 0,0 C 0,10 20,40 20,80 C 20,81 22,84 22,88 Z" },
        { "hvcurveto", [0, 0, "rmoveto", 10, 20, 30, 40, "hvcurveto"], "M 0,0 C 10,0 30,30 30,70 Z" },
        { "hvcurveto last dx", [0, 0, "rmoveto", 10, 20, 30, 40, 5, "hvcurveto"], "M 0,0 C 10,0 30,30 35,70 Z" },
        { "hvcurveto alternates", [0, 0, "rmoveto", 10, 20, 30, 40, 1, 2, 3, 4, 5, "hvcurveto"], "M 0,0 C 10,0 30,30 30,70 C 30,71 32,74 36,79 Z" },
        { "vhcurveto", [0, 0, "rmoveto", 10, 20, 30, 40, "vhcurveto"], "M 0,0 C 0,10 20,40 60,40 Z" },
        { "vhcurveto last dy", [0, 0, "rmoveto", 10, 20, 30, 40, 5, "vhcurveto"], "M 0,0 C 0,10 20,40 60,45 Z" },
        { "vhcurveto alternates", [0, 0, "rmoveto", 10, 20, 30, 40, 1, 2, 3, 4, 5, "vhcurveto"], "M 0,0 C 0,10 20,40 60,40 C 61,40 63,43 68,47 Z" },
        { "rcurveline", [0, 0, "rmoveto", 10, 20, 30, 40, 50, 60, 5, 6, "rcurveline"], "M 0,0 C 10,20 40,60 90,120 L 95,126 Z" },
        { "rlinecurve", [0, 0, "rmoveto", 5, 6, 10, 20, 30, 40, 50, 60, "rlinecurve"], "M 0,0 L 5,6 C 15,26 45,66 95,126 Z" },
        { "flex", [0, 0, "rmoveto", 10, 0, 10, 10, 10, 10, 10, -10, 10, -10, 10, 0, 50, "flex"], "M 0,0 C 10,0 20,10 30,20 C 40,10 50,0 60,0 Z" },
        { "hflex", [0, 0, "rmoveto", 10, 10, 20, 10, 10, 10, 10, "hflex"], "M 0,0 C 10,0 20,20 30,20 C 40,20 50,0 60,0 Z" },
        { "hflex1", [0, 0, "rmoveto", 10, 5, 10, 10, 10, 10, 10, -10, 10, "hflex1"], "M 0,0 C 10,5 20,15 30,15 C 40,15 50,5 60,0 Z" },
        { "flex1 horizontal", [0, 0, "rmoveto", 10, 5, 10, 10, 10, 5, 10, -5, 10, -10, 10, "flex1"], "M 0,0 C 10,5 20,15 30,20 C 40,15 50,5 60,0 Z" },
        { "flex1 vertical", [0, 0, "rmoveto", 5, 10, 10, 10, 5, 10, -5, 10, -10, 10, 10, "flex1"], "M 0,0 C 5,10 15,20 20,30 C 15,40 5,50 0,60 Z" },
        { "moveto closes", [0, 0, "rmoveto", 10, 0, "rlineto", 50, 50, "rmoveto", 0, 10, "rlineto"], "M 0,0 L 10,0 Z M 60,50 L 60,60 Z" },
        { "consecutive movetos", [5, 5, "rmoveto", 10, 10, "rmoveto", 1, 0, "rlineto"], "M 15,15 L 16,15 Z" },
        { "16.16 fixed operands", [1.5, 2.25, "rmoveto", -0.5, 0, "rlineto"], "M 1.5,2.25 L 1,2.25 Z" },
        { "short integer operand", [2000, -3000, "rmoveto", 1, 0, "rlineto"], "M 2000,-3000 L 2001,-3000 Z" },
        { "dotsection", ["dotsection", 0, 0, "rmoveto", 1, 0, "rlineto", "dotsection"], "M 0,0 L 1,0 Z" },
    };

    public static TheoryData<string, object[], double> ArithmeticOperators => new()
    {
        { "abs", [-5, "abs"], 5 },
        { "add", [3, 4, "add"], 7 },
        { "sub", [10, 4, "sub"], 6 },
        { "div", [600, 2, "div"], 300 },
        { "div fraction", [1, 4, "div"], 0.25 },
        { "neg", [5, "neg"], -5 },
        { "mul", [6, 7, "mul"], 42 },
        { "sqrt", [16, "sqrt"], 4 },
        { "and", [1, 0, "and"], 0 },
        { "and true", [2, 3, "and"], 1 },
        { "or", [1, 0, "or"], 1 },
        { "not", [0, "not"], 1 },
        { "eq", [3, 3, "eq"], 1 },
        { "eq false", [3, 4, "eq"], 0 },
        { "ifelse v1 <= v2", [10, 20, 1, 2, "ifelse"], 10 },
        { "ifelse v1 > v2", [10, 20, 3, 2, "ifelse"], 20 },
        { "drop", [5, 9, "drop"], 5 },
        { "put get", [42, 0, "put", 0, "get"], 42 },
        { "put get last element", [17, 31, "put", 31, "get"], 17 },
        { "index 0", [5, 0, "index", "add"], 10 },
        { "index negative copies the top", [6, -1, "index", "add"], 12 },
        { "index 1", [5, 6, 1, "index", "add", "add"], 16 },
    };

    public static TheoryData<object[], double> WidthCases => new()
    {
        { [50, 0, 0, "rmoveto"], 250 },
        { [0, 0, "rmoveto"], 300 },
        { [50, 10, "hmoveto"], 250 },
        { [10, "hmoveto"], 300 },
        { [50, 10, "vmoveto"], 250 },
        { [10, "vmoveto"], 300 },
        { [50, 10, 20, "hstem", 0, 0, "rmoveto"], 250 },
        { [10, 20, "hstem", 0, 0, "rmoveto"], 300 },
        { [50, 10, 20, "vstem", 0, 0, "rmoveto"], 250 },
        { [50, 10, 20, "hstemhm", 0, 0, "rmoveto"], 250 },
        { [50, 10, 20, "vstemhm", 0, 0, "rmoveto"], 250 },
        { [-50, 10, 20, "hintmask", new byte[] { 0x80 }, 0, 0, "rmoveto"], 150 },
        { [10, 20, "cntrmask", new byte[] { 0x80 }, 0, 0, "rmoveto"], 300 },
        { [50], 250 },
        { [], 300 },
    };

    public static TheoryData<int, byte[]> HintMasks => new()
    {
        { 3, new byte[] { 0xE0 } },
        { 9, new byte[] { 0xFF, 0x80 } },
    };

    public static TheoryData<object[], string, string> DamagedCharStrings => new()
    {
        { [0, 0, "rmoveto", 5, "callsubr", 1, 0, "rlineto", "endchar"], "M 0,0 L 1,0 Z", "FontCharstringSubrOutOfRange" },
        { [0, 0, "rmoveto", 9, new byte[] { 2 }, 1, 0, "rlineto", "endchar"], "M 0,0 L 1,0 Z", "FontCharstringUnknownOperator" },
        { [0, 0, "rmoveto", 1, 0, "rlineto"], "M 0,0 L 1,0 Z", "FontCharstringNoEndchar" },
        { [1, 0, "rlineto", "endchar"], "M 0,0 L 1,0 Z", "FontCharstringMovetoMissing" },
        { [0, 0, "rmoveto", 1, 0, 2, "rlineto", "endchar"], "M 0,0 L 1,0 Z", "FontCharstringArgumentCount" },
        { [0, 0, "rmoveto", new byte[] { 28, 1 }], "Invalid", "FontCharstringTruncated" },
        { [0, 10, "hstem", 0, 0, "rmoveto", "hintmask"], "Invalid", "FontCharstringTruncated" },
    };

    [Theory]
    [MemberData(nameof(PathOperators))]
    public void Path_operators_draw_the_points_their_definition_gives(string operators, object[] charString, string expected)
    {
        var context = new FontProgramContext();
        FontProgram program = Program(context, ("A", CffBuilder.T2([.. charString, "endchar"])));

        Assert.Equal(expected, OutlineText.Of(program, 1));
        Assert.True(context.Diagnostics.Count == 0, operators + ": " + string.Join("; ", context.Diagnostics.Select(d => d.Code)));
    }

    [Theory]
    [MemberData(nameof(ArithmeticOperators))]
    public void Arithmetic_and_storage_operators_compute_a_coordinate(string operators, object[] expression, double expected)
    {
        var context = new FontProgramContext();
        FontProgram program = Program(context, ("A", CffBuilder.T2([.. expression, 0, "rmoveto", 10, 0, "rlineto", "endchar"])));

        string x = expected.ToString(CultureInfo.InvariantCulture);
        string end = (expected + 10).ToString(CultureInfo.InvariantCulture);
        Assert.Equal($"M {x},0 L {end},0 Z", OutlineText.Of(program, 1));
        Assert.True(context.Diagnostics.Count == 0, operators);
    }

    [Theory]
    [InlineData(1, "M 3,1 L 4,1 Z")]
    [InlineData(-1, "M 2,3 L 3,3 Z")]
    [InlineData(4, "M 3,1 L 4,1 Z")]
    public void Roll_shifts_the_top_elements_toward_the_top_for_a_positive_amount(int amount, string expected)
    {
        // 1 2 3 3 J roll: J = 1 gives 3 1 2, J = -1 gives 2 3 1 (PostScript roll); drop leaves the first two for rmoveto.
        FontProgram program = Program(null, ("A", CffBuilder.T2(1, 2, 3, 3, amount, "roll", "drop", "rmoveto", 1, 0, "rlineto", "endchar")));

        Assert.Equal(expected, OutlineText.Of(program, 1));
    }

    [Fact]
    public void Dup_and_exch_rearrange_the_stack()
    {
        FontProgram program = Program(null, ("A", CffBuilder.T2(7, "dup", "rmoveto", 1, 2, "exch", "rlineto", "endchar")));

        Assert.Equal("M 7,7 L 9,8 Z", OutlineText.Of(program, 1));
    }

    [Fact]
    public void Random_gives_a_number_above_0_and_at_most_1_reproducibly()
    {
        FontProgram program = Program(null, ("A", CffBuilder.T2("random", 1000, "mul", 0, "rmoveto", 1, 0, "rlineto", "endchar")));
        var outline = new GlyphOutline();

        program.GetOutline(1, outline);
        double x = outline.Path.Points[0].X;
        program.GetOutline(1, outline);

        Assert.InRange(x, double.Epsilon, 1000);
        Assert.Equal(x, outline.Path.Points[0].X);
    }

    [Fact]
    public void Division_by_zero_gives_0_with_a_diagnostic()
    {
        var context = new FontProgramContext();
        FontProgram program = Program(context, ("A", CffBuilder.T2(5, 0, "div", 0, "rmoveto", 1, 0, "rlineto", "endchar")));

        Assert.Equal("M 0,0 L 1,0 Z", OutlineText.Of(program, 1));
        Assert.Equal("FontCharstringOperandInvalid", Assert.Single(context.Diagnostics).Code);
    }

    [Theory]
    [MemberData(nameof(WidthCases))]
    public void The_first_stack_clearing_operator_carries_an_optional_width_over_nominalWidthX(object[] charString, double advance)
    {
        var context = new FontProgramContext();
        byte[] glyph = CffBuilder.T2([.. charString, "endchar"]);
        FontProgram program = Parse(new CffBuilder { DefaultWidthX = 300, NominalWidthX = 200, Glyphs = { (".notdef", CffBuilder.T2("endchar")), ("A", glyph) } }.Build(), context);

        Assert.Equal(new GlyphMetrics(advance, 0), program.GetMetrics(1));
        Assert.NotEqual(GlyphOutlineStatus.Invalid, program.GetOutline(1, new GlyphOutline()));
        Assert.Empty(context.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(HintMasks))]
    public void A_hint_mask_has_one_bit_per_stem_including_an_implicit_vstem_before_it(int stems, byte[] mask)
    {
        // Two hstem pairs declared, the rest given as pending arguments of the mask (an implicit vstemhm, 5177 §4.3).
        var arguments = new List<object> { 0, 10, 20, 10, "hstemhm" };
        for (int pair = 2; pair < stems; pair++)
        {
            arguments.AddRange([pair * 30, 10]);
        }

        var context = new FontProgramContext();
        FontProgram program = Program(context, ("A", CffBuilder.T2([.. arguments, "hintmask", mask, 0, 0, "rmoveto", 10, 0, "rlineto", "cntrmask", mask, 0, 10, "rlineto", "endchar"])));

        Assert.Equal("M 0,0 L 10,0 L 10,10 Z", OutlineText.Of(program, 1));
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void Local_and_global_subroutines_are_numbered_with_their_bias()
    {
        var context = new FontProgramContext();
        var builder = new CffBuilder
        {
            Glyphs = { (".notdef", CffBuilder.T2("endchar")), ("I", CffBuilder.T2(-107, "callsubr", -106, "callgsubr", "endchar")) },
            LocalSubrs = { CffBuilder.T2(100, 0, "rmoveto", 200, "hlineto", "return") },
            GlobalSubrs =
            {
                CffBuilder.T2("return"),
                CffBuilder.T2(0, 700, "rlineto", -105, "callgsubr", "return"),
                CffBuilder.T2(-200, "hlineto", "return"),
            },
        };

        FontProgram program = Parse(builder.Build(), context);

        Assert.Equal("M 100,0 L 300,0 L 300,700 L 100,700 Z", OutlineText.Of(program, 1));
        Assert.Empty(context.Diagnostics);
    }

    [Theory]
    [InlineData(1239, 107)]
    [InlineData(1240, 1131)]
    [InlineData(33899, 1131)]
    [InlineData(33900, 32768)]
    public void The_subroutine_bias_depends_on_the_number_of_subroutines(int count, int bias)
    {
        var builder = new CffBuilder { Glyphs = { (".notdef", CffBuilder.T2("endchar")), ("A", CffBuilder.T2(-bias, "callsubr", "endchar")) } };
        byte[] empty = CffBuilder.T2("return");
        builder.LocalSubrs.Add(CffBuilder.T2(0, 0, "rmoveto", 5, 0, "rlineto", "return"));
        for (int index = 1; index < count; index++)
        {
            builder.LocalSubrs.Add(empty);
        }

        var context = new FontProgramContext();
        FontProgram program = Parse(builder.Build(), context);

        Assert.Equal("M 0,0 L 5,0 Z", OutlineText.Of(program, 1));
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void Endchar_with_four_arguments_composes_an_accented_character_from_StandardEncoding_components()
    {
        // 5177 Appendix C: adx ady bchar achar endchar; 65 is A and 194 acute in StandardEncoding.
        var context = new FontProgramContext();
        FontProgram program = Parse(
            new CffBuilder
            {
                NominalWidthX = 500,
                Glyphs =
                {
                    (".notdef", CffBuilder.T2("endchar")),
                    ("A", CffBuilder.T2(100, 0, 0, "rmoveto", 300, 700, 300, -700, "rlineto", "endchar")),
                    ("acute", CffBuilder.T2(0, 0, "rmoveto", 100, 100, "rlineto", "endchar")),
                    ("Aacute", CffBuilder.T2(120, 250, 750, 65, 194, "endchar")),
                },
            }.Build(),
            context);

        Assert.Equal("M 0,0 L 300,700 L 600,0 Z M 250,750 L 350,850 Z", OutlineText.Of(program, 3));
        Assert.Equal(620, program.GetMetrics(3).AdvanceWidth);
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void An_accent_component_that_is_itself_accented_or_missing_is_skipped_with_a_diagnostic()
    {
        var context = new FontProgramContext();
        FontProgram program = Parse(
            new CffBuilder
            {
                Glyphs =
                {
                    (".notdef", CffBuilder.T2("endchar")),
                    ("A", CffBuilder.T2(0, 0, 66, 194, "endchar")),
                    ("B", CffBuilder.T2(0, 0, "rmoveto", 10, 10, "rlineto", "endchar")),
                    ("C", CffBuilder.T2(0, 0, 65, 66, "endchar")),
                },
            }.Build(),
            context);

        Assert.Equal("M 0,0 L 10,10 Z", OutlineText.Of(program, 1));
        Assert.Equal("M 0,0 L 10,10 Z", OutlineText.Of(program, 3));
        Assert.Equal("FontCharstringSeacComponentMissing", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void Too_many_operands_drop_the_glyph_with_a_diagnostic()
    {
        var context = new FontProgramContext();
        FontProgram program = Program(context, ("A", CffBuilder.T2([.. Enumerable.Repeat<object>(1, 49), "endchar"])));

        Assert.Equal("Invalid", OutlineText.Of(program, 1));
        Assert.Equal("FontCharstringStackOverflow", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void A_subroutine_that_calls_itself_stops_at_the_nesting_limit()
    {
        var context = new FontProgramContext();
        var builder = new CffBuilder { Glyphs = { (".notdef", CffBuilder.T2("endchar")), ("A", CffBuilder.T2(0, 0, "rmoveto", -107, "callsubr", "endchar")) } };
        builder.LocalSubrs.Add(CffBuilder.T2(1, 0, "rlineto", -107, "callsubr", "return"));
        FontProgram program = Parse(builder.Build(), context);

        Assert.Equal("Invalid", OutlineText.Of(program, 1));
        Assert.Equal("FontCharstringSubrDepth", Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void Subroutine_fan_out_stops_at_the_operator_budget()
    {
        // Each level calls the next twice: 2^10 calls of the leaf, far more than a budget of 1000 operators.
        var context = new FontProgramContext { MaxCharStringOperators = 1000 };
        var builder = new CffBuilder { Glyphs = { (".notdef", CffBuilder.T2("endchar")), ("A", CffBuilder.T2(0, 0, "rmoveto", -107, "callsubr", "endchar")) } };
        for (int level = 0; level < 9; level++)
        {
            builder.LocalSubrs.Add(CffBuilder.T2(level - 106, "callsubr", level - 106, "callsubr", "return"));
        }

        builder.LocalSubrs.Add(CffBuilder.T2(1, 0, "rlineto", "return"));
        FontProgram program = Parse(builder.Build(), context);

        Assert.Equal("Invalid", OutlineText.Of(program, 1));
        Assert.Equal("FontCharstringBudgetExceeded", Assert.Single(context.Diagnostics).Code);
    }

    [Theory]
    [MemberData(nameof(DamagedCharStrings))]
    public void A_damaged_charstring_is_repaired_or_dropped_with_a_diagnostic(object[] charString, string expected, string code)
    {
        var context = new FontProgramContext();
        FontProgram program = Program(context, ("A", CffBuilder.T2(charString)));

        Assert.Equal(expected, OutlineText.Of(program, 1));
        Assert.Equal(code, Assert.Single(context.Diagnostics).Code);
    }

    [Fact]
    public void Strict_mode_throws_for_a_damaged_charstring_every_time_its_outline_is_asked_for()
    {
        FontProgram program = Program(new FontProgramContext { ReadingMode = PdfReadingMode.Strict }, ("A", CffBuilder.T2(0, 0, "rmoveto", 1, 0, "rlineto")));

        Assert.Equal("FontCharstringNoEndchar", Assert.Throws<DiagnosticException>(() => program.GetOutline(1, new GlyphOutline())).Diagnostic.Code);
        Assert.Throws<DiagnosticException>(() => program.GetOutline(1, new GlyphOutline()));
    }

    [Fact]
    public void Diagnostics_of_a_charstring_are_reported_once_per_program_in_lenient_mode()
    {
        var context = new FontProgramContext();
        FontProgram program = Program(context, ("A", CffBuilder.T2(0, 0, "rmoveto", 1, 0, "rlineto")), ("B", CffBuilder.T2(0, 0, "rmoveto", 2, 0, "rlineto")));

        for (int pass = 0; pass < 3; pass++)
        {
            OutlineText.Of(program, 1);
            OutlineText.Of(program, 2);
        }

        Assert.Single(context.Diagnostics);
    }

    [Fact]
    public void Extracting_outlines_allocates_nothing_once_warm()
    {
        var builder = new CffBuilder
        {
            Glyphs =
            {
                (".notdef", CffBuilder.T2("endchar")),
                ("A", CffBuilder.T2(100, 0, 0, "rmoveto", 300, 700, 300, -700, "rlineto", "endchar")),
                ("acute", CffBuilder.T2(0, 10, "hstem", 0, 0, "rmoveto", 10, 20, 30, 40, 1, 2, 3, 4, 5, "hvcurveto", 600, 2, "div", 0, "rlineto", "endchar")),
                ("Aacute", CffBuilder.T2(120, 250, 750, 65, 194, "endchar")),
                ("B", CffBuilder.T2(-107, "callsubr", -107, "callgsubr", 0, 0, "rmoveto", 10, 0, 10, 10, 10, 10, 10, -10, 10, -10, 10, 0, 50, "flex", "endchar")),
            },
            LocalSubrs = { CffBuilder.T2(5, 5, "rmoveto", 1, 1, "rlineto", "return") },
            GlobalSubrs = { CffBuilder.T2(1, 0, 0, 1, 2, 3, "random", "mul", "rrcurveto", "return") },
        };
        FontProgram program = Parse(builder.Build());
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
                    segments += outline.Path.Verbs.Length + (int)program.GetMetrics(glyph).AdvanceWidth;
                }
            }

            return segments;
        }
    }

    private static FontProgram Program(FontProgramContext? context, params (string Name, byte[] CharString)[] glyphs)
    {
        var builder = new CffBuilder();
        builder.Glyphs.Add((".notdef", CffBuilder.T2("endchar")));
        builder.Glyphs.AddRange(glyphs);
        return Parse(builder.Build(), context);
    }

    private static FontProgram Parse(byte[] font, FontProgramContext? context = null) =>
        Assert.IsAssignableFrom<FontProgram>(new CffFontProgramParser().Parse(font, context ?? new FontProgramContext()));
}
