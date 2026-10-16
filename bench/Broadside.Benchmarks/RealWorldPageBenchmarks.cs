using BenchmarkDotNet.Attributes;

namespace Broadside.Benchmarks;

/// <summary>
/// Parsing and text extraction over the pinned real-world pages of the rendering gate (issue #140; <see cref="RealWorldPages"/>),
/// the parse and text side of the Phase 3 baseline compared with PDFium and pdf.js in
/// <c>docs/benchmarks/phase-3-render-baselines.md</c> (ISO 32000-2 §7.5, §7.7.3, §9.10).
/// </summary>
/// <remarks>
/// <see cref="Open"/>: open the file from memory and load the page (its dictionary, inherited attributes and boxes), what PDFium's
/// <c>FPDF_LoadDocument</c> + <c>FPDF_LoadPage</c> and pdf.js's <c>getDocument</c> + <c>getPage</c> do before drawing.
/// <see cref="ExtractText"/>: the page of a document opened and extracted once in <see cref="Setup"/>, through
/// <see cref="PdfPage.ExtractText()"/> and its plain text.
/// </remarks>
[MemoryDiagnoser]
public class RealWorldPageBenchmarks
{
    private byte[] _bytes = [];
    private int _index;
    private PdfDocument? _document;

    /// <summary>Gets or sets the page: <c>corpus/path#page</c>, or the fallback under <c>tests/Corpus/</c>.</summary>
    [ParamsSource(nameof(Pages))]
    public string Page { get; set; } = string.Empty;

    /// <summary>The pinned pages present on this machine, else the fallback.</summary>
    /// <returns>The parameter values.</returns>
    public static IEnumerable<string> Pages() => RealWorldPages.Available();

    [GlobalSetup]
    public void Setup()
    {
        (_bytes, _index) = RealWorldPages.Read(Page);
        _document = RealWorldPages.Engine.Open(_bytes);
        ExtractText();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public double Open()
    {
        using PdfDocument document = RealWorldPages.Engine.Open(_bytes);
        return document.Pages[_index].MediaBox.Width;
    }

    [Benchmark]
    public int ExtractText() => _document!.Pages[_index].ExtractText().PlainText.Length;
}
