using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Structure;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Builds the per-page MCID index of a tagged page (ISO 32000-2 §14.7.5.4): open the file from memory, read the structure tree root
/// and map every MCID of page 1 to its element through the page's <c>StructParents</c> parent-tree entry, checking each against the
/// element's <c>K</c>. This is what text extraction and the layout tree pay once per page. <c>Mcids</c> 10 is the corpus file
/// <c>tagged-structure.pdf</c>; 1000 and 10000 are synthetic pages with one paragraph element per 10 MCIDs. Building the index
/// allocates (one dictionary per page, one view per element), so <c>Allocated</c> is a baseline to watch, not a zero to assert.
/// </summary>
[MemoryDiagnoser]
public class StructureTreeBenchmarks
{
    private byte[] _bytes = [];

    /// <summary>The number of MCIDs on the page.</summary>
    [Params(10, 1000, 10000)]
    public int Mcids { get; set; }

    [GlobalSetup]
    public void Setup() => _bytes = Mcids == 10
        ? File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, "tagged-structure.pdf"))
        : TaggedPage(Mcids);

    [Benchmark]
    public int BuildPageMcidIndex()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        PdfStructureTreeRoot root = document.StructureTree!;
        return root.GetMarkedContentElements(document.Pages[0]).Count;
    }

    /// <summary>One page whose parent tree lists <paramref name="mcids"/> MCIDs, ten per paragraph element.</summary>
    private static byte[] TaggedPage(int mcids)
    {
        int paragraphs = mcids / 10;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /StructParents 0 >>",
        };
        var kids = new StringBuilder();
        var parents = new StringBuilder();
        for (int index = 0; index < paragraphs; index++)
        {
            kids.Append(CultureInfo.InvariantCulture, $"{6 + index} 0 R ");
            for (int item = 0; item < 10; item++)
            {
                parents.Append(CultureInfo.InvariantCulture, $"{6 + index} 0 R ");
            }
        }

        objects.Add($"<< /Type /StructTreeRoot /K [5 0 R] /ParentTree << /Nums [0 [{parents}]] >> /ParentTreeNextKey 1 >>");
        objects.Add($"<< /Type /StructElem /S /Document /P 4 0 R /K [{kids}] >>");
        for (int index = 0; index < paragraphs; index++)
        {
            string items = string.Join(' ', Enumerable.Range(index * 10, 10));
            objects.Add($"<< /Type /StructElem /S /P /P 5 0 R /Pg 3 0 R /K [{items}] >>");
        }

        var text = new StringBuilder("%PDF-2.0\n");
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

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /ID [<00> <00>] >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }
}
