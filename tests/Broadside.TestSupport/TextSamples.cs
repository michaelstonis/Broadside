using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Pages with text in an embedded font built in memory, for rendering tests, benchmarks and fuzzing that need glyph outlines without
/// depending on the operating system's fonts (ISO 32000-2 §9.6.3, §9.9).
/// </summary>
public static class TextSamples
{
    private static readonly Lazy<byte[]> Program = new(BuildProgram);

    /// <summary>
    /// Gets a TrueType program (1000 units per em) whose (3, 1) "cmap" maps every printable ASCII character to its own glyph: the
    /// space to an empty glyph, every other character to a rectangle of its own size, odd ones with a rounded second contour of
    /// quadratic curves.
    /// </summary>
    public static byte[] FontProgram => Program.Value;

    /// <summary>
    /// A one-page file whose content stream is <paramref name="content"/> with <see cref="FontProgram"/> as the font resource
    /// <c>/F1</c>: a TrueType font with WinAnsiEncoding and every printable code 600 units wide, embedded with <c>/FontFile2</c>.
    /// </summary>
    /// <param name="content">The page's content.</param>
    /// <param name="resources">More entries of the page's resource dictionary.</param>
    /// <returns>The file.</returns>
    public static byte[] Page(byte[] content, string resources = "")
    {
        byte[] program = FontProgram;
        string widths = string.Join(' ', Enumerable.Repeat("600", 95));
        string font = string.Create(CultureInfo.InvariantCulture, $"<< /Type /Font /Subtype /TrueType /BaseFont /BroadsideSample /FirstChar 32 /LastChar 126 /Widths [{widths}] /Encoding /WinAnsiEncoding /FontDescriptor 6 0 R >>");
        const string descriptor = "<< /Type /FontDescriptor /FontName /BroadsideSample /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 100 /FontFile2 7 0 R >>";
        string stream = string.Create(CultureInfo.InvariantCulture, $"<< /Length {program.Length} /Length1 {program.Length} >>\nstream\n") + Encoding.Latin1.GetString(program) + "\nendstream";
        return ContentSamples.OnePage(content, "/Font << /F1 5 0 R >> " + resources, font, descriptor, stream);
    }

    private static byte[] BuildProgram()
    {
        var builder = new TrueTypeBuilder
        {
            Cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((32, 126, 1 - 32)))),
        };
        builder.Glyphs.Add([]);
        for (int code = 32; code <= 126; code++)
        {
            if (code == 32)
            {
                builder.Glyphs.Add([]);
                continue;
            }

            int right = 100 + (code % 7 * 60);
            int top = 400 + (code % 5 * 60);
            (int X, int Y, bool On)[] box = [(50, 0, true), (50, top, true), (right, top, true), (right, 0, true)];
            builder.Glyphs.Add(code % 2 == 0
                ? TrueTypeBuilder.Simple(box)
                : TrueTypeBuilder.Simple(
                    box,
                    [(300, 100, true), (450, 100, false), (450, 250, true), (450, 400, false), (300, 400, true), (150, 400, false), (150, 250, true), (150, 100, false)]));
        }

        builder.Metrics = [.. Enumerable.Range(0, builder.Glyphs.Count).Select(glyph => ((ushort)600, (short)(glyph > 1 ? 50 : 0)))];
        return builder.Build();
    }
}
