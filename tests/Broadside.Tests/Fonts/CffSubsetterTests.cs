using System.Text;
using Broadside.Fonts;
using Broadside.Fonts.Cff;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

#pragma warning disable CA5394 // Reproducible test data, not security.

/// <summary>
/// The CFF subsetter (issue #151) over seeded random subroutine-heavy programs: more than 1,240 local and global subroutines (bias
/// 1131), nested calls across both INDEXes, <c>hintmask</c> operators inside subroutines whose mask length depends on the calling
/// glyph's stems, and mask bytes that read as operators. Every glyph of every random subset draws as in the original, the subset
/// has no subroutines left to dangle, and only the Font DICTs used survive. Adobe Technical Notes #5176 and #5177; ISO 32000-2
/// §9.7.4.2 and §9.9.2.
/// </summary>
public sealed class CffSubsetterTests
{
    private const int Glyphs = 400;
    private const int Subrs = 1300;

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void Random_subsets_of_a_subroutine_heavy_program_draw_every_glyph_as_the_original_with_no_subroutines_left(int seed, bool cidKeyed)
    {
        byte[] program = Program(seed, cidKeyed, computedCall: false);
        CffFontProgram original = Parse(program);
        var random = new Random(seed);
        for (int round = 0; round < 8; round++)
        {
            int[] glyphs = [.. Enumerable.Range(0, 1 + random.Next(40)).Select(_ => random.Next(Glyphs))];
            SubsetProgram subset = CffSubsetter.Subset(program, glyphs)!;
            CffFontProgram result = Parse(subset.Program);

            int[] expected = [0, .. glyphs.Where(glyph => glyph != 0).Distinct().Order()];
            Assert.Equal(expected, subset.Glyphs);
            Assert.Equal(subset.Glyphs.Length, result.GlyphCount);
            Assert.True(result.Font.IsCidKeyed);
            Assert.Equal(0, result.Font.GlobalSubrs.Count);
            for (int glyph = 0; glyph < subset.Glyphs.Length; glyph++)
            {
                Assert.Equal(0, result.Font.GetPrivate(glyph).Subrs.Count);
                Assert.True(result.Font.TryGetGlyphForCid(subset.Glyphs[glyph], out int found) && found == glyph);
                Assert.Equal(Outline(original, subset.Glyphs[glyph]), Outline(result, glyph));
                Assert.Equal(original.GetMetrics(subset.Glyphs[glyph]).AdvanceWidth, result.GetMetrics(glyph).AdvanceWidth);
            }

            // Font DICTs (CID-keyed: one per third of the glyphs) are renumbered in FDArray order, keeping only those used.
            int[] usedDicts = [.. subset.Glyphs.Select(original.Font.GetFontDict).Distinct().Order()];
            for (int glyph = 0; glyph < subset.Glyphs.Length; glyph++)
            {
                Assert.Equal(Array.IndexOf(usedDicts, original.Font.GetFontDict(subset.Glyphs[glyph])), result.Font.GetFontDict(glyph));
            }
        }
    }

    [Fact]
    public void A_subroutine_number_computed_by_arithmetic_keeps_every_subroutine_and_charstring_as_it_is()
    {
        byte[] program = Program(5, cidKeyed: false, computedCall: true);
        CffFontProgram original = Parse(program);
        int[] glyphs = [1, 2, 3, 50, 399];
        SubsetProgram subset = CffSubsetter.Subset(program, glyphs)!;
        CffFontProgram result = Parse(subset.Program);

        Assert.Equal(original.Font.GlobalSubrs.Count, result.Font.GlobalSubrs.Count);
        for (int glyph = 0; glyph < subset.Glyphs.Length; glyph++)
        {
            Assert.Equal(Outline(original, subset.Glyphs[glyph]), Outline(result, glyph));
        }
    }

    [Fact]
    public void An_accent_after_a_call_out_of_range_keeps_its_components_as_the_interpreter_skips_the_call()
    {
        // Fuzz finding (cff-subset): the interpreter pops the operand of a call out of range and goes on, here to an endchar accent.
        var builder = new CffBuilder { Name = "SkippedCall" };
        builder.GlobalSubrs.Add(CffBuilder.T2(0, 0, "rmoveto", 10, 10, "rlineto", "return"));
        builder.Glyphs.Add((".notdef", CffBuilder.T2("endchar")));
        builder.Glyphs.Add(("A", CffBuilder.T2(600, 0, 0, "rmoveto", 300, 700, "rlineto", 300, -700, "rlineto", "endchar")));
        builder.Glyphs.Add(("acute", CffBuilder.T2(300, 0, 750, "rmoveto", 100, 100, "rlineto", 50, -50, "rlineto", "endchar")));
        builder.Glyphs.Add(("Aacute", CffBuilder.T2(-48, -76, -30, "callgsubr", 750, 65, 194, "endchar")));
        byte[] program = builder.Build();
        CffFontProgram original = Parse(program);
        Assert.StartsWith("Complete MoveTo LineTo LineTo Close MoveTo LineTo LineTo Close ", Outline(original, 3), StringComparison.Ordinal);

        SubsetProgram subset = CffSubsetter.Subset(program, [3])!;
        CffFontProgram result = Parse(subset.Program);
        Assert.Equal(Outline(original, 3), Outline(result, subset.NewId(3)));
    }

    [Fact]
    public void A_program_that_is_not_cff_or_has_type_1_charstrings_is_not_subset()
    {
        Assert.Null(CffSubsetter.Subset(new byte[] { 0, 1, 0, 0, 0, 0 }, [1]));
        var builder = new CffBuilder { TopDictPrefix = [.. CffBuilder.DictInteger(1), 12, 6] };
        builder.Glyphs.Add((".notdef", CffBuilder.T2("endchar")));
        Assert.Null(CffSubsetter.Subset(builder.Build(), [0]));
    }

    /// <summary>
    /// A random program: subroutine i (local or global) is at level (i / 2) % 3 and calls only lower levels of the same parity;
    /// even subroutines serve glyphs with at most 8 stems (one mask byte), odd ones glyphs with 9 to 16 (two).
    /// </summary>
    private static byte[] Program(int seed, bool cidKeyed, bool computedCall)
    {
        var random = new Random(seed);
        int bias = 1131;
        byte[] Segment() => random.Next(2) == 0
            ? CffBuilder.T2(random.Next(-200, 200), random.Next(-200, 200), "rlineto")
            : CffBuilder.T2(random.Next(-200, 200), random.Next(-200, 200), random.Next(-200, 200), random.Next(-200, 200), random.Next(-200, 200), random.Next(-200, 200), "rrcurveto");
        byte[] Mask(int bytes) => [.. Enumerable.Range(0, bytes).Select(_ => (byte)(random.Next(3) == 0 ? 0x0A : random.Next(256)))];
        int Callee(int parity, int belowLevel)
        {
            int level = random.Next(belowLevel);
            int block = random.Next(Subrs / 6);
            return (block * 6) + (level * 2) + parity;
        }

        byte[] Subr(int index)
        {
            int parity = index % 2;
            int level = (index / 2) % 3;
            var body = new List<byte>();
            if (random.Next(2) == 0)
            {
                body.AddRange(CffBuilder.T2("hintmask", Mask(parity + 1)));
            }

            for (int segment = random.Next(1, 4); segment > 0; segment--)
            {
                body.AddRange(Segment());
            }

            if (level > 0)
            {
                body.AddRange(CffBuilder.T2(Callee(parity, level) - bias, random.Next(2) == 0 ? "callsubr" : "callgsubr"));
            }

            body.Add(11);
            return [.. body];
        }

        var builder = new CffBuilder { Name = "RandomSubrs", NominalWidthX = 500 };
        builder.GlobalSubrs.AddRange(Enumerable.Range(0, Subrs).Select(Subr));
        builder.Glyphs.Add((".notdef", CffBuilder.T2("endchar")));
        for (int glyph = 1; glyph < Glyphs; glyph++)
        {
            int stems = random.Next(2, 17);
            int horizontal = random.Next(1, stems);
            int parity = stems > 8 ? 1 : 0;
            var code = new List<object> { random.Next(-100, 600) };
            code.AddRange(Enumerable.Range(0, horizontal * 2).Select(_ => (object)random.Next(1, 40)));
            code.Add("hstemhm");
            code.AddRange(Enumerable.Range(0, (stems - horizontal) * 2).Select(_ => (object)random.Next(1, 40)));
            code.Add("hintmask");
            code.Add(Mask((stems + 7) / 8));
            code.AddRange([random.Next(-50, 50), random.Next(-50, 50), "rmoveto"]);
            for (int call = random.Next(0, 5); call > 0; call--)
            {
                code.AddRange([Callee(parity, 3) - bias, random.Next(2) == 0 ? "callsubr" : "callgsubr"]);
            }

            if (computedCall && glyph % 7 == 0)
            {
                // A subroutine number the walk cannot read: (n - bias - 1) + 1.
                code.AddRange([Callee(parity, 3) - bias - 1, 1, "add", "callgsubr"]);
            }

            code.Add("endchar");
            builder.Glyphs.Add(($"g{glyph}", CffBuilder.T2([.. code])));
        }

        double[] matrix = [0.001, 0, 0, 0.001, 0, 0];
        if (cidKeyed)
        {
            builder.Ros = ("Adobe", "Japan1", 4);
            for (int dict = 0; dict < 3; dict++)
            {
                var fontDict = new CffFontDict { FontMatrix = matrix, NominalWidthX = 500 };
                fontDict.LocalSubrs.AddRange(Enumerable.Range(0, Subrs).Select(Subr));
                builder.FontDicts.Add(fontDict);
            }

            builder.Charset = CffBuilder.CharsetFormat0([.. Enumerable.Range(1, Glyphs - 1).Select(glyph => glyph * 3)]);
            builder.FdSelect = CffBuilder.FdSelectFormat3(Glyphs, (0, 0), (Glyphs / 3, 1), (2 * Glyphs / 3, 2));
        }
        else
        {
            builder.FontMatrix = matrix;
            builder.LocalSubrs.AddRange(Enumerable.Range(0, Subrs).Select(Subr));
        }

        return builder.Build();
    }

    private static CffFontProgram Parse(byte[] program) => Assert.IsType<CffFontProgram>(new CffFontProgramParser().Parse(program, new FontProgramContext()));

    /// <summary>A glyph's outline in text space (through the program's matrix) as text, so equal outlines compare equal.</summary>
    private static string Outline(FontProgram program, int glyph)
    {
        var outline = new GlyphOutline();
        GlyphOutlineStatus status = program.GetOutline(glyph, outline);
        Assert.True(status is GlyphOutlineStatus.Complete or GlyphOutlineStatus.Empty, $"glyph {glyph}: {status}");
        var description = new StringBuilder().Append(status).Append(' ');
        foreach (PathVerb verb in outline.Path.Verbs)
        {
            description.Append(verb).Append(' ');
        }

        Matrix matrix = program.FontMatrix;
        foreach (PathPoint point in outline.Path.Points)
        {
            description.Append(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Round((point.X * matrix.A) + (point.Y * matrix.C), 9):R},{Math.Round((point.X * matrix.B) + (point.Y * matrix.D), 9):R} ");
        }

        return description.ToString();
    }
}
