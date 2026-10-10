using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.Cff;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// CID-keyed CFF programs through the font program contract: each glyph's charstring runs with the Private DICT (local subrs, bias,
/// widths) of the Font DICT that FDSelect gives it, and outlines are in the space of the program's FontMatrix. Programs are built in
/// memory with <see cref="CffBuilder"/>. Adobe Technical Note #5176 §18-19; ISO 32000-2 §9.7.4.2 and §9.9.
/// </summary>
public class CidCffProgramTests
{
    private const string FirstBox = "M 10,20 L 110,20 L 110,120 Z";
    private const string SecondBox = "M 10,20 L 210,20 L 210,220 Z";

    public static TheoryData<string, byte[]> FdSelects => new()
    {
        { "format 0", [0, 0, 0, 1] },
        { "format 3", CffBuilder.FdSelectFormat3(3, (0, 0), (2, 1)) },
    };

    /// <summary>Top DICT and Font DICT FontMatrix (null = absent) and the scale of the program matrix they give.</summary>
    public static TheoryData<double[]?, double[]?, double> Matrices => new()
    {
        { [1, 0, 0, 1, 0, 0], [0.001, 0, 0, 0.001, 0, 0], 0.001 },
        { null, [0.002, 0, 0, 0.002, 0, 0], 0.002 },
        { [0.0005, 0, 0, 0.0005, 0, 0], null, 0.0005 },
        { null, null, 0.001 },
        { [2, 0, 0, 2, 0, 0], [0.001, 0, 0, 0.001, 0, 0], 0.002 },
    };

    /// <summary>Format 4 (CFF2 only); a format 3 first range not at glyph 0; a format 3 sentinel that is not the glyph count.</summary>
    public static TheoryData<byte[]> MalformedFdSelects => new()
    {
        { [4, 0, 0] },
        { [3, 0, 1, 0, 1, 0, 0, 3] },
        { [3, 0, 2, 0, 0, 0, 0, 2, 1, 0, 9] },
    };

    [Theory]
    [MemberData(nameof(FdSelects))]
    public void Each_glyph_runs_with_the_local_subrs_bias_and_widths_of_the_font_dictionary_FDSelect_gives_it(string format, byte[] fdSelect)
    {
        var context = new FontProgramContext();
        FontProgram program = Parse(TwoFontDicts(fdSelect), context);

        Assert.Equal(3, program.GlyphCount);
        Assert.Equal(FirstBox, OutlineText.Of(program, 1));
        Assert.Equal(SecondBox, OutlineText.Of(program, 2));
        Assert.Equal(new GlyphMetrics(550, 0), program.GetMetrics(1));
        Assert.Equal(new GlyphMetrics(730, 0), program.GetMetrics(2));
        Assert.True(context.Diagnostics.Count == 0, format);
    }

    [Theory]
    [MemberData(nameof(Matrices))]
    public void The_font_matrix_is_the_font_dictionary_matrix_concatenated_with_the_top_matrix(double[]? top, double[]? fd, double scale)
    {
        CffBuilder builder = TwoFontDicts([0, 0, 0, 0]);
        builder.FontMatrix = top;
        builder.FontDicts[0].FontMatrix = fd;
        builder.FontDicts[1].FontMatrix = fd;

        FontProgram program = Parse(builder.Build());

        Assert.Equal(Matrix.CreateScale(scale, scale), Round(program.FontMatrix));
        Assert.Equal(FirstBox, OutlineText.Of(program, 1));
    }

    [Fact]
    public void A_glyph_whose_font_dictionary_has_another_matrix_is_scaled_into_the_space_of_the_program_matrix()
    {
        // FD 0 is 1/1000 em per unit, FD 1 is 1/2000: the second glyph's outline is halved so one FontMatrix fits every glyph.
        CffBuilder builder = TwoFontDicts([0, 0, 0, 1]);
        builder.FontMatrix = [1, 0, 0, 1, 0, 0];
        builder.FontDicts[0].FontMatrix = [0.001, 0, 0, 0.001, 0, 0];
        builder.FontDicts[1].FontMatrix = [0.0005, 0, 0, 0.0005, 0, 0];

        FontProgram program = Parse(builder.Build());

        Assert.Equal(Matrix.CreateScale(0.001, 0.001), Round(program.FontMatrix));
        Assert.Equal(FirstBox, OutlineText.Of(program, 1));
        Assert.Equal("M 5,10 L 105,10 L 105,110 Z", OutlineText.Of(program, 2));
        Assert.Equal(365, program.GetMetrics(2).AdvanceWidth, 9);
    }

    [Fact]
    public void An_FDSelect_entry_past_the_FDArray_selects_the_first_font_dictionary_with_a_diagnostic()
    {
        var context = new FontProgramContext();
        FontProgram program = Parse(TwoFontDicts([0, 0, 0, 7]), context);

        Assert.Equal("FontCffFdSelectInvalid", Assert.Single(context.Diagnostics).Code);
        Assert.Equal(new GlyphMetrics(530, 0), program.GetMetrics(2));
    }

    [Theory]
    [MemberData(nameof(MalformedFdSelects))]
    public void A_malformed_FDSelect_is_repaired_with_a_diagnostic(byte[] fdSelect)
    {
        var context = new FontProgramContext();
        FontProgram program = Parse(TwoFontDicts(fdSelect), context);

        Assert.Equal("FontCffFdSelectInvalid", Assert.Single(context.Diagnostics).Code);
        Assert.Equal(FirstBox, OutlineText.Of(program, 1));
    }

    [Fact]
    public void A_CID_keyed_program_without_an_FDArray_reads_its_glyphs_with_default_private_values()
    {
        CffBuilder builder = TwoFontDicts(null);
        builder.WriteFdArray = false;
        var context = new FontProgramContext();
        FontProgram program = Parse(builder.Build(), context);

        Assert.Equal("FontCffFdArrayMissing", Assert.Single(context.Diagnostics).Code);
        Assert.Equal(new GlyphMetrics(50, 0), program.GetMetrics(1));
    }

    [Fact]
    public void Strict_mode_throws_for_a_malformed_FDSelect()
    {
        var context = new FontProgramContext { ReadingMode = PdfReadingMode.Strict };

        var exception = Assert.Throws<DiagnosticException>(() => new CffFontProgramParser().Parse(TwoFontDicts([0, 0, 0, 9]).Build(), context));
        Assert.Equal("FontCffFdSelectInvalid", exception.Diagnostic.Code);
    }

    /// <summary>
    /// A CID-keyed font with three glyphs (CIDs 0, 264, 3284) and two Font DICTs. FD 0: nominalWidthX 500, three local subrs (bias
    /// 107), the last drawing a 100-unit box. FD 1: nominalWidthX 700, 1240 local subrs (bias 1131), the last drawing a 200-unit box.
    /// Each glyph moves to (10, 20) with a width argument and calls its FD's last subr, so reading it with the other FD's Private DICT
    /// would call a subr that does not exist.
    /// </summary>
    internal static CffBuilder TwoFontDicts(byte[]? fdSelect)
    {
        var builder = new CffBuilder
        {
            Name = "BroadsideCID-Test",
            Ros = ("Adobe", "Japan1", 2),
            Charset = CffBuilder.CharsetFormat0(264, 3284),
            FdSelect = fdSelect,
        };
        builder.Glyphs.Add((".notdef", CffBuilder.T2("endchar")));
        builder.Glyphs.Add(("cid264", CffBuilder.T2(50, 10, 20, "rmoveto", 2 - 107, "callsubr", "endchar")));
        builder.Glyphs.Add(("cid3284", CffBuilder.T2(30, 10, 20, "rmoveto", 1239 - 1131, "callsubr", "endchar")));
        var first = new CffFontDict { NominalWidthX = 500 };
        first.LocalSubrs.AddRange([CffBuilder.T2("return"), CffBuilder.T2("return"), CffBuilder.T2(100, 0, "rlineto", 0, 100, "rlineto", "return")]);
        var second = new CffFontDict { NominalWidthX = 700 };
        second.LocalSubrs.AddRange(Enumerable.Range(0, 1239).Select(_ => CffBuilder.T2("return")));
        second.LocalSubrs.Add(CffBuilder.T2(200, 0, "rlineto", 0, 200, "rlineto", "return"));
        builder.FontDicts.AddRange([first, second]);
        return builder;
    }

    private static Matrix Round(Matrix matrix) =>
        new(Math.Round(matrix.A, 9), Math.Round(matrix.B, 9), Math.Round(matrix.C, 9), Math.Round(matrix.D, 9), Math.Round(matrix.E, 9), Math.Round(matrix.F, 9));

    private static FontProgram Parse(CffBuilder builder, FontProgramContext? context = null) => Parse(builder.Build(), context);

    private static FontProgram Parse(byte[] font, FontProgramContext? context = null)
    {
        var parser = new CffFontProgramParser();
        Assert.True(parser.CanParse(font));
        return Assert.IsAssignableFrom<FontProgram>(parser.Parse(font, context ?? new FontProgramContext()));
    }
}
