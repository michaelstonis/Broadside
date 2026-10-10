using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Mapping character codes to Unicode (ISO 32000-2 §9.10, issue #58), which text extraction does once per glyph: every benchmark
/// here must allocate nothing (<c>Allocated</c> = <c>-</c>).
/// </summary>
/// <remarks>
/// <see cref="ToUnicodeLookups"/> looks up 10,000 two-byte codes in a parsed ToUnicode CMap with bfchar, incremented bfrange and
/// array bfrange entries (internal <c>ToUnicodeMap</c>). <see cref="GlyphNameLookups"/> maps the 10,000 glyph names of the WinAnsi
/// codes 0x20-0x7E, round robin, through the Adobe Glyph List algorithm. <see cref="FontLookups"/> goes through the public
/// <see cref="PdfFont.GetUnicode(CharacterCode, Span{char}, out UnicodeSource)"/> of <c>text-tounicode-bf.pdf</c>'s Type 0 font.
/// </remarks>
[MemoryDiagnoser]
public class ToUnicodeBenchmarks
{
    private const int Lookups = 10_000;

    private static readonly string[] WinAnsiNames =
    [
        "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand", "quotesingle", "parenleft", "parenright",
        "asterisk", "plus", "comma", "hyphen", "period", "slash", "zero", "one", "two", "three", "four", "five", "six", "seven",
        "eight", "nine", "colon", "semicolon", "less", "equal", "greater", "question", "at", "A", "B", "C", "D", "E", "F", "G",
        "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "bracketleft", "backslash",
        "bracketright", "asciicircum", "underscore", "grave", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n",
        "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "braceleft", "bar", "braceright", "asciitilde",
    ];

    private readonly char[] _buffer = new char[512];
    private ToUnicodeMap _map = ToUnicodeMap.Identity;
    private PdfDocument? _document;
    private PdfFont? _font;

    [GlobalSetup]
    public void Setup()
    {
        var text = new StringBuilder("/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
        text.Append("100 beginbfchar\n");
        for (int index = 0; index < 100; index++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"<{0x3000 + (index * 7):X4}> <{0x4E00 + index:X4}>\n");
        }

        text.Append("endbfchar\n3 beginbfrange\n<0000> <00FF> <0000>\n<0100> <01FF> <0100>\n<2000> <2002> [<00660066> <00660069> <D840DC3E>]\nendbfrange\nendcmap end end\n");
        _map = ToUnicodeMap.Parse(Encoding.Latin1.GetBytes(text.ToString()), new CMapContext());
        _document = PdfDocument.Open(File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, "text-tounicode-bf.pdf")));
        _font = _document.Pages[0].GetFont("F1")!;
        _ = ToUnicodeLookups();
        _ = GlyphNameLookups();
        _ = FontLookups();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>10,000 lookups in a ToUnicode CMap: bfchar, incremented bfrange and array bfrange codes in turn; returns the units written.</summary>
    [Benchmark]
    public long ToUnicodeLookups()
    {
        long total = 0;
        for (int index = 0; index < Lookups; index++)
        {
            uint code = (index % 3) switch
            {
                0 => (uint)(0x3000 + (index % 100 * 7)),
                1 => (uint)(index % 0x200),
                _ => (uint)(0x2000 + (index % 3)),
            };
            if (_map.TryMap(code, 2, _buffer, out int written, out _))
            {
                total += written;
            }
        }

        return total;
    }

    /// <summary>10,000 WinAnsi glyph names through the Adobe Glyph List algorithm; returns the units written.</summary>
    [Benchmark]
    public long GlyphNameLookups()
    {
        long total = 0;
        for (int index = 0; index < Lookups; index++)
        {
            total += AdobeGlyphList.MapName(WinAnsiNames[index % WinAnsiNames.Length], zapfDingbats: false, _buffer, out _);
        }

        return total;
    }

    /// <summary>10,000 codes 1-6 through the font's public mapping; returns the units written.</summary>
    [Benchmark]
    public long FontLookups()
    {
        long total = 0;
        for (int index = 0; index < Lookups; index++)
        {
            total += _font!.GetUnicode(new CharacterCode((uint)(1 + (index % 6)), 2, IsValid: true), _buffer, out _);
        }

        return total;
    }
}
