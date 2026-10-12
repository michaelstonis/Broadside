using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Embedded TrueType programs (ISO 32000-2 §9.9, issue #50). <see cref="Outlines"/> decodes every glyph of the program into one
/// reused <see cref="GlyphOutline"/>: glyph outlines are a hot path, so it must allocate nothing (<c>Allocated</c> = <c>-</c>).
/// <see cref="GlyphIds"/> is the cached §9.6.5.4 lookup the text interpreter makes per shown code, also allocation-free.
/// <see cref="Parse"/> is the one-time cost per program (the table directory and the program object).
/// </summary>
/// <remarks>
/// <c>composite</c> is the font of <c>tests/Corpus/text-truetype-composite.pdf</c> (every simple and composite form);
/// <c>real-world</c> is the TrueType program with the most glyphs among the first files of the fetched PDFBox corpus, or the
/// composite font again when the corpora are not fetched.
/// </remarks>
[MemoryDiagnoser]
public class TrueTypeBenchmarks
{
    private PdfDocument? _document;
    private PdfTrueTypeFont? _font;
    private FontProgram? _program;
    private byte[] _programBytes = [];
    private GlyphOutline _outline = new();

    /// <summary>Gets or sets which font is measured.</summary>
    [Params("composite", "real-world")]
    public string Font { get; set; } = "composite";

    [GlobalSetup]
    public void Setup()
    {
        (_document, _font) = Font == "real-world" && FindRealWorldFont() is { } found
            ? found
            : Open(File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, "text-truetype-composite.pdf")));
        _program = _font.Program!;
        _programBytes = _document.DecodeStream(_font.Descriptor!.FontFile2!).ToArray();
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

    /// <summary>Parses the program from its bytes: table directory, "head", "hhea", "maxp", "loca" and the "cmap" subtable records.</summary>
    [Benchmark]
    public int Parse() => new TrueTypeFontProgramParser().Parse(_programBytes, new FontProgramContext())!.GlyphCount;

    private static (PdfDocument, PdfTrueTypeFont) Open(byte[] bytes)
    {
        PdfDocument document = PdfDocument.Open(bytes);
        var font = (PdfTrueTypeFont)document.Pages[0].GetFont("F1")!;
        return (document, font);
    }

    /// <summary>The embedded TrueType font with the most glyphs among the first 40 files of the PDFBox corpus.</summary>
    private static (PdfDocument, PdfTrueTypeFont)? FindRealWorldFont()
    {
        if (CorpusLocator.RealWorldCorpusDirectory is not { } root || !Directory.Exists(Path.Combine(root, "pdfbox")))
        {
            return null;
        }

        (string File, int Page, CosName Name, int Glyphs)? best = null;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "pdfbox"), "*.pdf", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Take(40))
        {
            try
            {
                using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(file));
                for (int page = 0; page < document.Pages.Count; page++)
                {
                    if (document.Pages[page].Resources is not { } resources
                        || !resources.TryGetValue(new CosName("Font"), out CosObject? value)
                        || document.Resolve(value) is not CosDictionary fonts)
                    {
                        continue;
                    }

                    foreach (KeyValuePair<CosName, CosObject> entry in fonts)
                    {
                        if (document.GetFont(entry.Value) is PdfTrueTypeFont { Program: { } program } && program.GlyphCount > (best?.Glyphs ?? 0))
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
        return (opened, (PdfTrueTypeFont)opened.Pages[chosen.Page].GetFont(chosen.Name.Value)!);
    }
}
