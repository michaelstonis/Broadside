using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.IO;
using Broadside.Parsing;

namespace Broadside.Benchmarks;

/// <summary>
/// Cross-reference reconstruction at scale (issue #41): the one-pass scan over a synthesized file of <see cref="ObjectCount"/>
/// objects with no cross-reference table, and a lenient open of that file, which rebuilds the table from the scan. The scan reads
/// the file in windows and allocates only for the positions it finds, so its <c>Allocated</c> grows with the number of objects,
/// not with the file length, and its time is linear in the file length.
/// </summary>
[MemoryDiagnoser]
public class ReconstructionBenchmarks
{
    /// <summary>How many objects the synthesized file holds.</summary>
    public const int ObjectCount = 10_000;

    private byte[] _bytes = [];

    [GlobalSetup]
    public void Setup()
    {
        var text = new StringBuilder("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        for (int number = 3; number < ObjectCount; number++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Index {number} /Data (some bytes of data in a string) >>\nendobj\n");
        }

        _bytes = Encoding.Latin1.GetBytes(text.Append("%%EOF\n").ToString());
    }

    /// <summary>Scans the file once for object headers and file-structure keywords.</summary>
    [Benchmark]
    public int Scan()
    {
        using PdfSource source = PdfSource.FromMemory(_bytes);
        return FileScan.Run(source).Objects.Count;
    }

    /// <summary>Opens the file, which has no cross-reference table, so the table is rebuilt from the scan.</summary>
    [Benchmark]
    public int OpenAndRebuild()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        return document.Diagnostics.Count;
    }
}
