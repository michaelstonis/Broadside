using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Content;

namespace Broadside.Benchmarks;

/// <summary>
/// Shows 4,000 glyphs of a Type 3 font (ISO 32000-2 §9.6.4) whose descriptions run nested: a <c>d1</c> square that paints a form
/// from the font's resources and a <c>d0</c> triangle that sets its colour, into a processor that enters every glyph and takes its
/// paths. A hot path (one nested run per glyph): <c>Allocated</c> must read <c>-</c>.
/// </summary>
[MemoryDiagnoser]
public class Type3GlyphBenchmarks
{
    private readonly ContentProcessor _processor = new Type3Sink();
    private PdfDocument? _document;
    private PdfPage? _page;

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(OnePage());
        _page = _document.Pages[0];
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public void RunGlyphDescriptions() => _page!.ProcessContent(_processor);

    private static byte[] OnePage()
    {
        string content = "BT /T3 12 Tf " + string.Concat(Enumerable.Repeat("(abab) Tj\n", 1_000)) + "ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /T3 5 0 R >> >> /Contents 4 0 R >>",
            Stream(content, string.Empty),
            "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 6 0 R /b 7 0 R >> "
                + "/Encoding << /Type /Encoding /Differences [97 /a /b] >> /FirstChar 97 /LastChar 98 /Widths [1000 500] "
                + "/Resources << /XObject << /Fm 8 0 R >> >> >>",
            Stream("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f /Fm Do", string.Empty),
            Stream("500 0 d0 1 0 0 rg 0 0 m 500 1000 l 500 0 l f", string.Empty),
            Stream("0 0 10 10 re f", "/Type /XObject /Subtype /Form /BBox [0 0 100 100]"),
        ];
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
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

        static string Stream(string data, string entries) => $"<< {entries} /Length {data.Length} >>\nstream\n{data}\nendstream";
    }

    /// <summary>Enters every Type 3 glyph and ignores what it paints.</summary>
    private sealed class Type3Sink : ContentProcessor
    {
        public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Paths | ContentEvents.Forms | ContentEvents.Type3GlyphContent;

        public override ContentVisit BeginType3Glyph(in GlyphEvent glyph, ContentContext context) => ContentVisit.Enter;
    }
}
