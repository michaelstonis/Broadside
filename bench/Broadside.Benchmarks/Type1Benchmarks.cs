using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.Fonts.Type1;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Embedded Type 1 programs (ISO 32000-2 §9.9, issue #52). <see cref="Outlines"/> interprets every glyph's charstring into one
/// reused <see cref="GlyphOutline"/>: glyph outlines are a hot path, so it must allocate nothing (<c>Allocated</c> = <c>-</c>).
/// <see cref="Metrics"/> reads every glyph's <c>hsbw</c>, also allocation-free. <see cref="Parse"/> is the one-time cost per
/// program (layout, tokenizing, eexec and charstring decryption into one buffer, the name index).
/// </summary>
/// <remarks>
/// <c>synthesized</c> is the font of <c>tests/Corpus/text-type1-embedded.pdf</c> (subroutines, flex, hint replacement, seac);
/// <c>real-world</c> is the Type 1 program with the most glyphs among the first files of the fetched pdf.js corpus, or the
/// synthesized font again when the corpora are not fetched.
/// </remarks>
[MemoryDiagnoser]
public class Type1Benchmarks
{
    private PdfDocument? _document;
    private FontProgram? _program;
    private byte[] _programBytes = [];
    private GlyphOutline _outline = new();

    /// <summary>Gets or sets which font is measured.</summary>
    [Params("synthesized", "real-world")]
    public string Font { get; set; } = "synthesized";

    [GlobalSetup]
    public void Setup()
    {
        (_document, PdfType1Font font) = Font == "real-world" && FindRealWorldFont() is { } found
            ? found
            : Open(File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, "text-type1-embedded.pdf")));
        _program = font.Program!;
        _programBytes = _document.DecodeStream(font.Descriptor!.FontFile!).ToArray();
        _outline = new GlyphOutline();
        _ = Outlines();
        _ = Metrics();
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

    /// <summary>Every glyph's advance width.</summary>
    [Benchmark]
    public double Metrics()
    {
        double total = 0;
        for (int glyph = 0; glyph < _program!.GlyphCount; glyph++)
        {
            total += _program.GetMetrics(glyph).AdvanceWidth;
        }

        return total;
    }

    /// <summary>Parses the program from its bytes.</summary>
    [Benchmark]
    public int Parse() => new Type1FontProgramParser().Parse(_programBytes, new FontProgramContext())!.GlyphCount;

    private static (PdfDocument, PdfType1Font) Open(byte[] bytes)
    {
        PdfDocument document = PdfDocument.Open(bytes);
        var font = (PdfType1Font)document.Pages[0].GetFont("F1")!;
        return (document, font);
    }

    /// <summary>The embedded Type 1 font with the most glyphs among the first 300 files of the pdf.js corpus.</summary>
    private static (PdfDocument, PdfType1Font)? FindRealWorldFont()
    {
        if (CorpusLocator.RealWorldCorpusDirectory is not { } root || !Directory.Exists(Path.Combine(root, "pdfjs")))
        {
            return null;
        }

        (string File, int Page, CosName Name, int Glyphs)? best = null;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "pdfjs"), "*.pdf", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Take(300))
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
                        if (document.GetFont(entry.Value) is PdfType1Font { Descriptor.FontFile: not null, Program: { } program } && program.GlyphCount > (best?.Glyphs ?? 0))
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
