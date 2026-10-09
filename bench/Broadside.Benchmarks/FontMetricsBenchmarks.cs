using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Fonts;

namespace Broadside.Benchmarks;

/// <summary>
/// The per-glyph lookups of simple fonts (ISO 32000-2 §9.6.2, §9.6.5, issue #49): glyph name and width of every code of the
/// fourteen non-embedded Standard 14 fonts. The text interpreter asks for these once per shown glyph, so <see cref="NameAndWidth"/>
/// must allocate nothing (<c>Allocated</c> = <c>-</c>); <see cref="BuildMetrics"/> is the one-time cost per font.
/// </summary>
[MemoryDiagnoser]
public class FontMetricsBenchmarks
{
    private static readonly string[] Names =
    [
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique", "Helvetica", "Helvetica-Bold", "Helvetica-Oblique",
        "Helvetica-BoldOblique", "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic", "Symbol", "ZapfDingbats",
    ];

    private byte[] _bytes = [];
    private PdfDocument? _document;
    private PdfSimpleFont[] _fonts = [];

    [GlobalSetup]
    public void Setup()
    {
        _bytes = FileWithFonts();
        _document = PdfDocument.Open(_bytes);
        PdfPage page = _document.Pages[0];
        _fonts = [.. Enumerable.Range(1, Names.Length).Select(index => (PdfSimpleFont)page.GetFont("F" + index.ToString(CultureInfo.InvariantCulture))!)];
        _ = NameAndWidth();
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>Glyph name and width of the 256 codes of each of the 14 fonts: 3,584 lookups of each.</summary>
    [Benchmark(OperationsPerInvoke = 256 * 14)]
    public double NameAndWidth()
    {
        double total = 0;
        foreach (PdfSimpleFont font in _fonts)
        {
            for (int code = 0; code < 256; code++)
            {
                total += font.GetWidth((byte)code) + font.GetGlyphName((byte)code).Length;
            }
        }

        return total;
    }

    /// <summary>Opens the file and computes the 256 names and widths of each of the 14 fonts once.</summary>
    [Benchmark]
    public double BuildMetrics()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        PdfPage page = document.Pages[0];
        double total = 0;
        for (int index = 1; index <= Names.Length; index++)
        {
            total += ((PdfSimpleFont)page.GetFont("F" + index.ToString(CultureInfo.InvariantCulture))!).GetWidth(65);
        }

        return total;
    }

    /// <summary>One page whose resources name the fourteen fonts F1 to F14, objects 4 to 17.</summary>
    private static byte[] FileWithFonts()
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << "
                + string.Concat(Names.Select((_, index) => string.Create(CultureInfo.InvariantCulture, $"/F{index + 1} {index + 4} 0 R "))) + ">> >> >>",
        };
        objects.AddRange(Names.Select(name => $"<< /Type /Font /Subtype /Type1 /BaseFont /{name} /Encoding << /Differences [ 128 /Euro /bullet ] >> >>"));

        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Count; index++)
        {
            offsets.Add(text.Length);
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
