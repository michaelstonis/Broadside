using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Reading the codes of strings shown in composite fonts (ISO 32000-2 §9.7.6.2-§9.7.6.3, issue #53): a hot path of the text
/// interpreter, so every benchmark here must allocate nothing (<c>Allocated</c> = <c>-</c>).
/// </summary>
/// <remarks>
/// <see cref="ReadCodes"/> splits 64 KiB into codes and maps each to a CID: through the built-in Identity-H, and through an embedded
/// CMap with the mixed one- and two-byte codespace of 90ms-RKSJ-H (a cidrange per lead byte, notdef ranges), parsed by the core.
/// <see cref="ReadGlyphs"/> adds the CIDFont's part: CID to glyph id (CIDToGIDMap) and width (W, DW), through the public font view
/// of <c>tests/Corpus/text-cid-embedded-cmap.pdf</c> (whatever <see cref="Encoding"/> says).
/// </remarks>
[MemoryDiagnoser]
public class CMapBenchmarks
{
    private const int TextLength = 64 * 1024;

    private CMap _cmap = CMap.IdentityH;
    private byte[] _text = [];
    private PdfDocument? _document;
    private PdfType0Font? _font;

    /// <summary>Gets or sets which CMap reads the text.</summary>
    [Params("Identity-H", "mixed")]
    public string Encoding { get; set; } = "Identity-H";

    [GlobalSetup]
    public void Setup()
    {
        _cmap = Encoding == "mixed" ? CMap.Parse(System.Text.Encoding.Latin1.GetBytes(MixedCMap()), new CMapContext()) : CMap.IdentityH;
        _text = new byte[TextLength];
        for (int index = 0; index < _text.Length; index += 2)
        {
            // Lead bytes cycle through the one-byte range and both two-byte ranges of the mixed codespace.
            _text[index] = (byte)((index / 2 % 3) switch { 0 => 0x41 + (index % 26), 1 => 0x88 + (index % 20), _ => 0xE0 + (index % 12) });
            _text[index + 1] = (byte)(0x40 + (index % 0xBC));
        }

        _document = PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, "text-cid-embedded-cmap.pdf"));
        _font = (PdfType0Font)_document.Pages[0].GetFont("F1")!;
        _ = ReadCodes();
        _ = ReadGlyphs();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>Splits 64 KiB into codes and maps each to its CID; returns the sum of the CIDs.</summary>
    [Benchmark]
    public long ReadCodes()
    {
        long total = 0;
        ReadOnlySpan<byte> text = _text;
        while (!text.IsEmpty)
        {
            CharacterCode code = _cmap.ReadCode(text);
            total += _cmap.GetCid(code);
            text = text[code.Length..];
        }

        return total;
    }

    /// <summary>Reads 64 KiB through a Type 0 font: code, CID, glyph id and width per glyph.</summary>
    [Benchmark]
    public double ReadGlyphs()
    {
        double total = 0;
        ReadOnlySpan<byte> text = _text;
        while (!text.IsEmpty)
        {
            CidGlyph glyph = _font!.ReadGlyph(text);
            total += glyph.Width + glyph.GlyphId;
            text = text[glyph.Code.Length..];
        }

        return total;
    }

    /// <summary>A CMap with the 90ms-RKSJ-H codespace, a cidrange per lead byte and notdef ranges.</summary>
    private static string MixedCMap()
    {
        var text = new StringBuilder("/CMapName /Bench-H def 4 begincodespacerange <00> <80> <8140> <9FFC> <A0> <DF> <E040> <FCFC> endcodespacerange\n");
        text.Append("1 begincidrange <20> <7E> 231 endcidrange\n");
        for (int lead = 0x81; lead <= 0x9F; lead++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"1 begincidrange <{lead:X2}40> <{lead:X2}7E> {633 + ((lead - 0x81) * 188)} endcidrange\n");
        }

        text.Append("1 beginnotdefrange <8140> <9FFC> 1 endnotdefrange 1 beginnotdefrange <E040> <FCFC> 2 endnotdefrange\n");
        return text.ToString();
    }
}
