using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.Fonts.Cff;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Embedded CFF programs (ISO 32000-2 §9.9; Adobe Technical Notes #5176 and #5177, issue #51). <see cref="Outlines"/> interprets
/// the Type 2 charstring of every glyph into one reused <see cref="GlyphOutline"/>: charstring interpretation is a hot path, so it
/// must allocate nothing (<c>Allocated</c> = <c>-</c>). <see cref="GlyphIds"/> is the cached §9.6.5.2 lookup the text interpreter
/// makes per shown code. <see cref="Parse"/> is the one-time cost per program (INDEXes, DICTs, charset, encoding).
/// </summary>
/// <remarks>
/// <c>corpus</c> is the font of <c>tests/Corpus/text-cff-embedded.pdf</c> (hints, subroutines, curves, flex, seac); <c>real-world</c>
/// is the CFF program with the most glyphs among the first files of the fetched pdf.js corpus, or the corpus font again when the
/// corpora are not fetched.
/// </remarks>
[MemoryDiagnoser]
public class CffBenchmarks
{
    private PdfDocument? _document;
    private PdfType1Font? _font;
    private FontProgram? _program;
    private byte[] _programBytes = [];
    private GlyphOutline _outline = new();

    /// <summary>Gets or sets which font is measured.</summary>
    [Params("corpus", "real-world")]
    public string Font { get; set; } = "corpus";

    [GlobalSetup]
    public void Setup()
    {
        (_document, _font) = Font == "real-world" && FindRealWorldFont() is { } found
            ? found
            : Open(File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, "text-cff-embedded.pdf")));
        _program = _font.Program!;
        _programBytes = _document.DecodeStream(_font.Descriptor!.FontFile3!).ToArray();
        _outline = new GlyphOutline();
        _ = Outlines();
        _ = GlyphIds();
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

    /// <summary>The glyph id of every code, once the font has worked them out.</summary>
    [Benchmark(OperationsPerInvoke = 256)]
    public int GlyphIds()
    {
        int total = 0;
        for (int code = 0; code < 256; code++)
        {
            total += _font!.GetGlyphId((byte)code);
        }

        return total;
    }

    /// <summary>Parses the program from its bytes: header, INDEXes, Top and Private DICTs, charset and encoding.</summary>
    [Benchmark]
    public int Parse() => new CffFontProgramParser().Parse(_programBytes, new FontProgramContext())!.GlyphCount;

    private static (PdfDocument, PdfType1Font) Open(byte[] bytes)
    {
        PdfDocument document = PdfDocument.Open(bytes);
        var font = (PdfType1Font)document.Pages[0].GetFont("F1")!;
        return (document, font);
    }

    /// <summary>The embedded CFF font with the most glyphs among the first 200 files of the pdf.js corpus.</summary>
    private static (PdfDocument, PdfType1Font)? FindRealWorldFont()
    {
        if (CorpusLocator.RealWorldCorpusDirectory is not { } root || !Directory.Exists(Path.Combine(root, "pdfjs")))
        {
            return null;
        }

        (string File, int Page, CosName Name, int Glyphs)? best = null;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "pdfjs"), "*.pdf", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Take(200))
        {
            try
            {
                using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(file));
                for (int page = 0; page < Math.Min(document.Pages.Count, 4); page++)
                {
                    if (document.Pages[page].Resources is not { } resources
                        || !resources.TryGetValue(new CosName("Font"), out CosObject? value)
                        || document.Resolve(value) is not CosDictionary fonts)
                    {
                        continue;
                    }

                    foreach (KeyValuePair<CosName, CosObject> entry in fonts)
                    {
                        if (document.GetFont(entry.Value) is PdfType1Font { Program: { Format: FontProgramFormat.Cff } program } && program.GlyphCount > (best?.Glyphs ?? 0))
                        {
                            best = (file, page, entry.Key, program.GlyphCount);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Files the reader rejects are not candidates.
            }
        }

        if (best is not { } chosen)
        {
            return null;
        }

        PdfDocument opened = PdfDocument.Open(File.ReadAllBytes(chosen.File));
        return (opened, (PdfType1Font)opened.Pages[chosen.Page].GetFont(chosen.Name.Value)!);
    }
}
