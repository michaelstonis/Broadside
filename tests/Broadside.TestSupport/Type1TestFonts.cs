using System.Globalization;
using System.Text;
using Broadside.Fonts;
using Broadside.Fonts.Cff;
using Broadside.Graphics;

namespace Broadside.TestSupport;

/// <summary>
/// Builds Type 1 programs from the glyphs of a CFF program (such as the Standard 14 package's Foxit Symbol and Dingbats, which are
/// bare CFF) for the Type 1 to CFF conversion tests (issue #152). Each charstring uses what a Type 1 program from a font tool
/// does: <c>hsbw</c> with a side bearing, a stem hint, hint replacement through OtherSubr 3 and Subr 4 (Type 1 Font Format §8.2), the
/// first pair of consecutive curves drawn as flex through Subrs 0-2 (§8.3), and <c>div</c> for non-integer coordinates.
/// </summary>
public static class Type1TestFonts
{
    /// <summary>The side bearing every glyph's <c>hsbw</c> sets; the first move is relative to it.</summary>
    private const int SideBearing = 10;

    /// <summary>Builds a PFB program with the CFF program's glyph names, outlines and (rounded) advance widths.</summary>
    /// <param name="cff">A bare, name-keyed CFF program.</param>
    /// <returns>The PFB file.</returns>
    public static byte[] FromCff(ReadOnlyMemory<byte> cff)
    {
        FontProgram program = new CffFontProgramParser().Parse(cff, new FontProgramContext())
            ?? throw new ArgumentException("Not a CFF program.", nameof(cff));
        Matrix m = program.FontMatrix;
        var builder = new Type1Builder
        {
            FontName = program.PostScriptName ?? "Type1Test",
            FontMatrix = string.Create(CultureInfo.InvariantCulture, $"[{m.A:R} {m.B:R} {m.C:R} {m.D:R} {m.E:R} {m.F:R}] readonly def"),
        };

        // Type 1 Font Format §8.4: the flex and hint replacement Subrs 0-3, and Subr 4, a hint set the glyphs switch to.
        builder.Subr("3 0 callothersubr pop pop setcurrentpoint return")
            .Subr("0 1 callothersubr return")
            .Subr("0 2 callothersubr return")
            .Subr("return")
            .Subr("0 40 vstem return");

        var outline = new GlyphOutline();
        for (int glyph = 0; glyph < program.GlyphCount; glyph++)
        {
            outline.Clear();
            _ = program.GetOutline(glyph, outline);
            string name = glyph == 0 ? ".notdef" : program.GetGlyphName(glyph) ?? $"g{glyph}";
            int width = (int)Math.Round(program.GetMetrics(glyph).AdvanceWidth);
            _ = builder.Glyph(name, Charstring(outline.Path, width));
        }

        return builder.BuildPfb();
    }

    private static string Charstring(PathView path, int width)
    {
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{SideBearing} {width} hsbw 0 20 hstem 4 1 3 callothersubr pop callsubr ");
        ReadOnlySpan<PathVerb> verbs = path.Verbs;
        ReadOnlySpan<PathPoint> points = path.Points;
        var current = new PathPoint(SideBearing, 0);
        bool flexed = false;
        int at = 0;
        for (int index = 0; index < verbs.Length; index++)
        {
            switch (verbs[index])
            {
                case PathVerb.MoveTo:
                    Delta(text, ref current, points[at++]);
                    text.Append("rmoveto ");
                    break;
                case PathVerb.LineTo:
                    Delta(text, ref current, points[at++]);
                    text.Append("rlineto ");
                    break;
                case PathVerb.CubicTo when !flexed && index + 1 < verbs.Length && verbs[index + 1] == PathVerb.CubicTo:
                    // Flex (§8.3): 1 callsubr, the reference point (the joint) and six points each as rmoveto 2 callsubr, then
                    // the flex height and the absolute end point for 0 callsubr.
                    flexed = true;
                    text.Append("1 callsubr ");
                    Delta(text, ref current, points[at + 2]);
                    text.Append("rmoveto 2 callsubr ");
                    for (int point = 0; point < 6; point++)
                    {
                        Delta(text, ref current, points[at + point]);
                        text.Append("rmoveto 2 callsubr ");
                    }

                    text.Append("50 ");
                    Value(text, points[at + 5].X);
                    Value(text, points[at + 5].Y);
                    text.Append("0 callsubr ");
                    at += 6;
                    index++;
                    break;
                case PathVerb.CubicTo:
                    Delta(text, ref current, points[at++]);
                    Delta(text, ref current, points[at++]);
                    Delta(text, ref current, points[at++]);
                    text.Append("rrcurveto ");
                    break;
                case PathVerb.QuadTo:
                    throw new NotSupportedException("A CFF outline has no quadratic curves.");
                case PathVerb.Close:
                    text.Append("closepath ");
                    break;
            }
        }

        return text.Append("endchar").ToString();
    }

    private static void Delta(StringBuilder text, ref PathPoint current, PathPoint point)
    {
        Value(text, point.X - current.X);
        Value(text, point.Y - current.Y);
        current = point;
    }

    /// <summary>An integer, or a fraction as <c>numerator 65536 div</c> (§6.4).</summary>
    private static void Value(StringBuilder text, double value)
    {
        if (value == Math.Round(value))
        {
            text.Append(CultureInfo.InvariantCulture, $"{(int)value} ");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"{(int)Math.Round(value * 65536)} 65536 div ");
        }
    }
}
