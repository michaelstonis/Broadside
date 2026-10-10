using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Shows about 10,000 glyphs in Helvetica (text state operators, <c>TJ</c> arrays with adjustments, <c>'</c> and <c>"</c>) through
/// the content interpreter into a processor that takes every glyph event and does nothing (ISO 32000-2 §9.3, §9.4). A hot path:
/// <c>Allocated</c> must read <c>-</c>.
/// </summary>
[MemoryDiagnoser]
public class TextShowBenchmarks
{
    private readonly ContentProcessor _processor = new GlyphSink();
    private PdfDocument? _document;
    private PdfPage? _page;

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(OnePage(ContentSamples.TextHeavy(10_000)));
        _page = _document.Pages[0];
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public void ShowGlyphs() => _page!.ProcessContent(_processor);

    private static byte[] OnePage(byte[] content)
    {
        var text = new StringBuilder();
        var offsets = new List<int>();
        text.Append("%PDF-1.7\n");
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{Encoding.ASCII.GetString(content)}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        ];
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(text.Length);
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>Takes glyph events and ignores them.</summary>
    private sealed class GlyphSink : ContentProcessor
    {
        public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Text;
    }
}
