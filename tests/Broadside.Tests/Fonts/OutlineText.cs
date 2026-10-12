using System.Globalization;
using System.Text;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Tests.Fonts;

/// <summary>Writes a glyph outline as compact path text (<c>M x,y L x,y Q cx,cy x,y C … Z</c>) so tests compare whole outlines.</summary>
internal static class OutlineText
{
    /// <summary>The outline of one glyph of a program, as text; the status first when it is not <see cref="GlyphOutlineStatus.Complete"/>.</summary>
    public static string Of(FontProgram program, int glyphId)
    {
        var outline = new GlyphOutline();
        GlyphOutlineStatus status = program.GetOutline(glyphId, outline);
        return status == GlyphOutlineStatus.Complete ? Of(outline.Path) : status.ToString();
    }

    /// <summary>A path as text.</summary>
    public static string Of(PathView path)
    {
        var text = new StringBuilder();
        int point = 0;
        foreach (PathVerb verb in path.Verbs)
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            int count = verb switch { PathVerb.MoveTo or PathVerb.LineTo => 1, PathVerb.QuadTo => 2, PathVerb.CubicTo => 3, _ => 0 };
            text.Append(verb switch { PathVerb.MoveTo => 'M', PathVerb.LineTo => 'L', PathVerb.QuadTo => 'Q', PathVerb.CubicTo => 'C', _ => 'Z' });
            for (int index = 0; index < count; index++)
            {
                PathPoint p = path.Points[point++];
                text.Append(' ').Append(Number(p.X)).Append(',').Append(Number(p.Y));
            }
        }

        return text.ToString();
    }

    private static string Number(double value) => Math.Round(value, 4).ToString(CultureInfo.InvariantCulture);
}
