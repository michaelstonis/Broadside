using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Synthetic content streams for the content interpreter's allocation tests and benchmarks: deterministic, well-formed, and heavy
/// in the operators a path renderer sees most (ISO 32000-2 §8.4.4, §8.5).
/// </summary>
public static class ContentSamples
{
    /// <summary>
    /// A path-heavy content stream of at least <paramref name="minimumLength"/> bytes: blocks of state changes, paths built with
    /// every construction operator, every painting operator, and clips, each block balanced in <c>q</c>/<c>Q</c>.
    /// </summary>
    public static byte[] PathHeavy(int minimumLength)
    {
        var text = new StringBuilder(minimumLength + 512);
        int block = 0;
        while (text.Length < minimumLength)
        {
            double x = block % 500 * 1.25;
            double y = block % 700 * 0.75;
            text.Append(CultureInfo.InvariantCulture, $"q 1 0 0 1 {x} {y} cm {block % 7 + 0.5} w {block % 3} J {block % 3} j 4 M [3 {block % 4 + 1}] 0.5 d /Perceptual ri 1 i\n");
            text.Append(CultureInfo.InvariantCulture, $"0 0 m 100.5 0 l 100 100.25 l 50 150 25 175 0 100 c h S\n");
            text.Append(CultureInfo.InvariantCulture, $"10 10 m 20 30 40 50 v 60 70 80 90 y s\n");
            text.Append(CultureInfo.InvariantCulture, $"{x} {y} 50 -25 re f 0 0 10 10 re f* 5 5 m 6 6 l B 7 7 m 8 8 l B* 1 1 m 2 2 l b 3 3 m 4 4 l b*\n");
            text.Append("0 0 200 200 re W n 0 0 300 300 re W* n BX 1 2 3 newop EX Q\n");
            block++;
        }

        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>
    /// A text-heavy content stream showing at least <paramref name="glyphs"/> glyphs in the font resource <c>/F1</c>: text objects with
    /// every text state operator, <c>TJ</c> arrays with adjustments, and the positioning and next-line operators (ISO 32000-2 §9.3,
    /// §9.4).
    /// </summary>
    public static byte[] TextHeavy(int glyphs)
    {
        var text = new StringBuilder(glyphs * 2);
        int shown = 0;
        int line = 0;
        while (shown < glyphs)
        {
            text.Append(CultureInfo.InvariantCulture, $"BT /F1 {10 + (line % 3)} Tf {line % 2} Tc 0.5 Tw 95 Tz 12 TL {line % 3} Tr 0 Ts 72 {700 - (line % 50 * 12)} Td\n");
            text.Append("[(Hello, ) -250 (Broadside) 120 (text) -33.5 (show)] TJ T* (word spacing 32) Tj 1 0.5 (quoted) \" (next) '\n");
            text.Append("ET\n");
            shown += 47;
            line++;
        }

        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
