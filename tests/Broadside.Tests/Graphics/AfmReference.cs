using System.Globalization;
using System.Text;
using Broadside.TestSupport;

namespace Broadside.Tests.Graphics;

/// <summary>
/// One Adobe Core 14 AFM file read independently of the core's generated tables (Adobe Technical Note #5004 §8 and §9:
/// <c>C</c>, <c>WX</c> and <c>N</c> of each character metric line, <c>KPX</c> kerning pairs and the global keys), the source of
/// truth canvas text measurements are checked against.
/// </summary>
internal sealed class AfmReference
{
    /// <summary>The glyph names of the printable ASCII characters other than letters and digits (Adobe Standard Encoding names).</summary>
    private static readonly Dictionary<char, string> PunctuationNames = new()
    {
        [' '] = "space", ['!'] = "exclam", ['"'] = "quotedbl", ['#'] = "numbersign", ['$'] = "dollar", ['%'] = "percent",
        ['&'] = "ampersand", ['\''] = "quotesingle", ['('] = "parenleft", [')'] = "parenright", ['*'] = "asterisk", ['+'] = "plus",
        [','] = "comma", ['-'] = "hyphen", ['.'] = "period", ['/'] = "slash", [':'] = "colon", [';'] = "semicolon", ['<'] = "less",
        ['='] = "equal", ['>'] = "greater", ['?'] = "question", ['@'] = "at", ['['] = "bracketleft", ['\\'] = "backslash",
        [']'] = "bracketright", ['^'] = "asciicircum", ['_'] = "underscore", ['`'] = "grave", ['{'] = "braceleft", ['|'] = "bar",
        ['}'] = "braceright", ['~'] = "asciitilde",
        ['0'] = "zero", ['1'] = "one", ['2'] = "two", ['3'] = "three", ['4'] = "four", ['5'] = "five", ['6'] = "six", ['7'] = "seven",
        ['8'] = "eight", ['9'] = "nine",
        ['é'] = "eacute", ['ß'] = "germandbls", ['€'] = "Euro", ['–'] = "endash", ['“'] = "quotedblleft", ['”'] = "quotedblright",
        ['ü'] = "udieresis", ['Œ'] = "OE", ['©'] = "copyright", ['…'] = "ellipsis",
    };

    private readonly Dictionary<string, string> _keys = [];

    private AfmReference()
    {
    }

    /// <summary>Gets the WX width of each glyph, by name.</summary>
    public Dictionary<string, double> Widths { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the KPX x adjustment of each kerning pair.</summary>
    public Dictionary<(string Left, string Right), double> Kerning { get; } = [];

    /// <summary>Reads <c>src/Broadside/Fonts/Data/Afm/{fontName}.afm</c>.</summary>
    public static AfmReference Read(string fontName)
    {
        string path = Path.Combine(Corpus.Directory, "..", "..", "src", "Broadside", "Fonts", "Data", "Afm", fontName + ".afm");
        var file = new AfmReference();
        bool inMetrics = false;
        foreach (string raw in File.ReadLines(path, Encoding.ASCII))
        {
            string line = raw.Trim();
            string key = line.Split(' ', 2)[0];
            if (key.Length == 0)
            {
                continue;
            }

            if (key == "StartCharMetrics")
            {
                inMetrics = true;
            }
            else if (key == "EndCharMetrics")
            {
                inMetrics = false;
            }
            else if (inMetrics)
            {
                Dictionary<string, string> fields = line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(field => field.Split(' ', 2))
                    .GroupBy(parts => parts[0])
                    .ToDictionary(group => group.Key, group => group.First()[1]);
                file.Widths[fields["N"]] = double.Parse(fields["WX"], CultureInfo.InvariantCulture);
            }
            else if (key == "KPX")
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                file.Kerning[(parts[1], parts[2])] = double.Parse(parts[3], CultureInfo.InvariantCulture);
            }
            else
            {
                file._keys.TryAdd(key, line.Length > key.Length ? line[(key.Length + 1)..] : string.Empty);
            }
        }

        return file;
    }

    /// <summary>The Standard Encoding glyph name of an ASCII letter, digit or punctuation character (and a few others the tests use).</summary>
    public static string NameOf(char character) =>
        char.IsAsciiLetter(character) ? character.ToString() : PunctuationNames[character];

    /// <summary>A global numeric key, such as <c>Ascender</c>.</summary>
    public double Number(string key) => double.Parse(_keys[key].Split(' ')[0], CultureInfo.InvariantCulture);

    /// <summary>The advance of each character of <paramref name="text"/>, in glyph space units, by its glyph name.</summary>
    public double[] Advances(string text) => [.. text.Select(character => Widths[NameOf(character)])];

    /// <summary>The kerning between two characters, in glyph space units (0 when the file has no pair).</summary>
    public double Kern(char left, char right) => Kerning.GetValueOrDefault((NameOf(left), NameOf(right)));
}
