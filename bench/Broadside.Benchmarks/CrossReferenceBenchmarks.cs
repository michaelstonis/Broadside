using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Parsing;

namespace Broadside.Benchmarks;

/// <summary>
/// The cross-reference information of a file of 100,000 objects (ISO 32000-2 §7.5.4, issue #237): reading its classic table, and
/// looking up every object number in the merged lookup. <see cref="Lookup"/> is the path every load of an object takes and must
/// allocate nothing (<c>Allocated</c> = <c>-</c>); <see cref="ReadTable"/> allocates the entries, 16 bytes each.
/// </summary>
[MemoryDiagnoser]
public class CrossReferenceBenchmarks
{
    private const int Objects = 100_000;

    private byte[] _table = [];
    private CrossReference? _crossReference;

    [GlobalSetup]
    public void Setup()
    {
        var table = new StringBuilder();
        table.Append(CultureInfo.InvariantCulture, $"xref\n0 {Objects + 1}\n0000000000 65535 f\r\n");
        for (int number = 1; number <= Objects; number++)
        {
            table.Append(CultureInfo.InvariantCulture, $"{number * 40L:D10} 00000 n\r\n");
        }

        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {Objects + 1} /Root 1 0 R >>\n");
        _table = Encoding.ASCII.GetBytes(table.ToString());
        _crossReference = new CrossReference([XrefTableReader.Read(_table, 0, new DiagnosticSink(strict: false))!]);
    }

    /// <summary>Reads the table and its trailer.</summary>
    [Benchmark]
    public int ReadTable() => XrefTableReader.Read(_table, 0, new DiagnosticSink(strict: false))!.Entries.Count;

    /// <summary>Looks up every listed object number, and as many numbers past the last.</summary>
    [Benchmark]
    public long Lookup()
    {
        long sum = 0;
        for (int number = 1; number <= 2 * Objects; number++)
        {
            if (_crossReference!.TryGetEntry(number, out XrefEntry entry))
            {
                sum += entry.Offset;
            }
        }

        return sum;
    }
}
