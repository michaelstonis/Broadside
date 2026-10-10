using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.Fonts.Cmaps;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// CID-keyed CFF programs and predefined CMaps (ISO 32000-2 §9.7.4.2, §9.7.5.2; Adobe Technical Note #5176 §18-19, issue #54).
/// <see cref="Outlines"/> interprets every glyph of a CID-keyed program, each with the Private DICT its Font DICT gives it (a hot
/// path: <c>Allocated</c> = <c>-</c>). <see cref="CidGlyphIds"/> is the CID to glyph lookup through the inverse charset the text
/// interpreter makes per shown code (also <c>-</c>). <see cref="ParseUniJisUtf16"/> parses the largest Japanese predefined CMap of
/// the Broadside.Fonts.Cmaps package, the one-time cost per document that uses it (allocations expected).
/// </summary>
/// <remarks>The program is the one of <c>tests/Corpus/text-cidcff-predefined-cmap.pdf</c> (two Font DICTs, FDSelect format 3).</remarks>
[MemoryDiagnoser]
public class CidCffBenchmarks
{
    private static readonly int[] Cids = [0, 264, 3284, 3722, 1, 9000];

    private PdfDocument? _document;
    private PdfCidFont? _font;
    private FontProgram? _program;
    private GlyphOutline _outline = new();
    private ReadOnlyMemory<byte> _uniJis;

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, "text-cidcff-predefined-cmap.pdf")));
        var type0 = (PdfType0Font)_document.Pages[0].GetFont("F1")!;
        _font = type0.DescendantFont!;
        _program = _font.Program!;
        _outline = new GlyphOutline();
        ((IFontResolver)new PredefinedCMapResolver()).TryResolveResource(FontResourceKind.CMap, "UniJIS-UTF16-H", out _uniJis);
        _ = Outlines();
        _ = CidGlyphIds();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>Every glyph's outline into one reused buffer; returns the number of segments.</summary>
    [Benchmark]
    public int Outlines()
    {
        int segments = 0;
        for (int glyph = 0; glyph < _program!.GlyphCount; glyph++)
        {
            _program.GetOutline(glyph, _outline);
            segments += _outline.Path.Verbs.Length;
        }

        return segments;
    }

    /// <summary>The glyph id of a few CIDs (present, absent, past the charset) through the inverse charset.</summary>
    [Benchmark(OperationsPerInvoke = 6)]
    public int CidGlyphIds()
    {
        int total = 0;
        foreach (int cid in Cids)
        {
            total += _font!.GetGlyphId(cid);
        }

        return total;
    }

    /// <summary>Parses UniJIS-UTF16-H (188 KB of CMap text) as a document does on first use.</summary>
    [Benchmark]
    public int ParseUniJisUtf16() => CMap.Parse(_uniJis.Span).ReadCode([0x4E, 0x00]).Length;
}
