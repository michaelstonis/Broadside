using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Runs a page's content through the content interpreter into a processor that asks for every event and does nothing (ISO 32000-2
/// §7.8.2, §8.4, §8.5). A hot path: <c>Allocated</c> must read <c>-</c> for the synthetic page; the corpus page also decodes its
/// filtered stream, which allocates the decoded bytes once per run.
/// </summary>
[MemoryDiagnoser]
public class ContentInterpreterBenchmarks
{
    private readonly ContentProcessor _processor = new NullProcessor();
    private PdfDocument? _document;
    private PdfPage? _page;

    /// <summary>The page: one whose content is a synthetic 1 MB path-heavy stream, or the page of <c>filter-chain.pdf</c>.</summary>
    [Params("path-heavy-1mb", "filter-chain.pdf")]
    public string Page { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _document = Page == "path-heavy-1mb"
            ? PdfDocument.Open(OnePage(ContentSamples.PathHeavy(1 << 20)))
            : PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, Page));
        _page = _document.Pages[0];
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    [Benchmark]
    public void Interpret() => _page!.ProcessContent(_processor);

    private static byte[] OnePage(byte[] content)
    {
        var text = new StringBuilder();
        var offsets = new List<int>();
        text.Append("%PDF-1.7\n");
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{Encoding.ASCII.GetString(content)}\nendstream",
        ];
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(text.Length);
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = text.Length;
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>Asks for every event and ignores them.</summary>
    private sealed class NullProcessor : ContentProcessor
    {
    }
}
