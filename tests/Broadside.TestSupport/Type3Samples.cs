using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Synthetic pages of Type 3 text (ISO 32000-2 §9.6.4) for allocation tests and benchmarks of Type 3 glyphs in the display list and
/// the rasterizer. Linked as source into the benchmark project.
/// </summary>
public static class Type3Samples
{
    /// <summary>
    /// One page showing at least <paramref name="glyphs"/> glyphs of a Type 3 font <c>/T3</c> (FontMatrix 0.001, 600-unit widths)
    /// whose four glyphs cover every kind of description: <c>a</c> a <c>d1</c> shape with curves and an even-odd hole, <c>b</c> a
    /// <c>d1</c> 8x8 inline image mask, <c>c</c> a <c>d0</c> glyph that fills and strokes in its own colours, <c>d</c> a <c>d1</c>
    /// glyph painting a form of the font's resources (clipped to its bounding box). Lines alternate three colours and two sizes.
    /// </summary>
    /// <param name="glyphs">The least number of glyphs shown.</param>
    /// <returns>The file.</returns>
    public static byte[] Type3Heavy(int glyphs)
    {
        const string line = "abcdabcdabcdabcdabcdabcdabcd";
        var content = new StringBuilder();
        string[] colours = ["0 0 0", "0.8 0.1 0.1", "0.1 0.2 0.7"];
        int shown = 0;
        for (int index = 0; shown < glyphs; index++)
        {
            content.Append(CultureInfo.InvariantCulture, $"{colours[index % 3]} rg BT /T3 {(index % 2 == 0 ? 10 : 14)} Tf 20 {780 - (index % 48 * 16)} Td ({line}) Tj ET\n");
            shown += line.Length;
        }

        const string a = "600 0 0 0 600 700 d1 50 0 m 50 400 l 50 750 550 750 550 400 c 550 0 l h 200 100 m 400 100 l 400 300 l 200 300 l h f*";
        const string b = "600 0 0 0 600 600 d1 q 600 0 0 600 0 0 cm BI /W 8 /H 8 /IM true /D [1 0] ID ªUªUªUªU EI Q";
        const string c = "600 0 d0 0.2 0.6 0.2 rg 0 0 1 RG 20 w 1 j 50 50 m 300 650 l 550 50 l h B";
        const string d = "600 0 0 0 600 600 d1 /Fm Do 100 100 400 400 re f";
        return ContentSamples.OnePage(
            Encoding.ASCII.GetBytes(content.ToString()),
            "/Font << /T3 5 0 R >>",
            "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 600 700] /FontMatrix [0.001 0 0 0.001 0 0]"
            + " /CharProcs << /a 6 0 R /b 7 0 R /c 8 0 R /d 9 0 R >> /Encoding << /Type /Encoding /Differences [97 /a /b /c /d] >>"
            + " /FirstChar 97 /LastChar 100 /Widths [600 600 600 600] /Resources << /XObject << /Fm 10 0 R >> >> >>",
            ContentSamples.Stream(a),
            ContentSamples.Stream(b),
            ContentSamples.Stream(c),
            ContentSamples.Stream(d),
            ContentSamples.Stream("0 0 m 600 600 l 600 0 l f", "/Type /XObject /Subtype /Form /BBox [0 0 300 600]"));
    }
}
