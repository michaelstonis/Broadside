using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The Registry-Ordering-UCS2 table of a character collection is parsed once per engine and shared by every document it opens
/// (ISO 32000-2 §9.10.2 step d, issue #256).
/// </summary>
/// <remarks>
/// Both benchmarks open <c>tests/Corpus/text-cidcff-predefined-cmap.pdf</c> (a Type 0 font over Adobe-Japan1 with the predefined
/// CMap 90ms-RKSJ-H) and take the Unicode text of its first code through the public font view. <see cref="FirstLookupFirstDocument"/>
/// does it on a new engine, so the document pays for parsing the 90ms-RKSJ-H CMap and the Adobe-Japan1-UCS2 table (about 290 KB of text);
/// <see cref="FirstLookupSecondDocument"/> does it on an engine whose first document already did, so the lookup finds both tables
/// parsed. The difference between the two is the parse that sharing removes for every document after the first.
/// </remarks>
[MemoryDiagnoser]
public class CidToUnicodeSharingBenchmarks
{
    private byte[] _file = [];
    private PdfEngine _warm = new();

    [GlobalSetup]
    public void Setup()
    {
        _file = File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, "text-cidcff-predefined-cmap.pdf"));
        _warm = NewEngine();
        _ = Lookup(_warm);
    }

    /// <summary>Opens the file on a new engine and maps its first code to Unicode; the document parses the CMap and the UCS2 table.</summary>
    [Benchmark]
    public int FirstLookupFirstDocument() => Lookup(NewEngine());

    /// <summary>Opens the file on an engine that already served one and maps its first code to Unicode; both tables are shared.</summary>
    [Benchmark]
    public int FirstLookupSecondDocument() => Lookup(_warm);

    private static PdfEngine NewEngine() => new(new PdfOptions().UsePredefinedCMaps());

    private int Lookup(PdfEngine engine)
    {
        using PdfDocument document = engine.Open(_file);
        var font = (PdfType0Font)document.Pages[0].GetFont("F1")!;
        Span<char> buffer = stackalloc char[8];
        return font.GetUnicode(new CharacterCode(0x93FA, 2, IsValid: true), buffer, out _);
    }
}
