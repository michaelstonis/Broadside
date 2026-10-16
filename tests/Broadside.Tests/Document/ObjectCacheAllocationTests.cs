using System.Globalization;
using System.Text;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// What loading objects allocates depends on how many objects there are, never on their numbers (issue #288). The object cache
/// (ISO 32000-2 §7.5.3, §7.5.4) hashed references with a per-process random seed, and its concurrent dictionary grew its table and
/// its lock array as soon as any one lock's share passed a budget, so the same file allocated a different number of bytes in every
/// process and the benchmark gate had to store ranges. Here the generation number, which changes only the hash, stands in for the
/// random seed: every generation must allocate exactly the same bytes. In the heavy collection, like the other allocation tests.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ObjectCacheAllocationTests
{
    private const int Objects = 2000;

    [Fact]
    public void Loading_objects_allocates_the_same_bytes_whatever_their_hash()
    {
        var allocated = new List<long>();
        for (int generation = 0; generation < 6; generation++)
        {
            byte[] bytes = File(generation);
            CosReference[] references = [.. Enumerable.Range(1, Objects).Select(number => new CosReference(number, generation))];
            Action load = () =>
            {
                using PdfDocument document = PdfDocument.Open(bytes);
                foreach (CosReference reference in references)
                {
                    _ = document.Resolve(reference);
                }
            };
            allocated.Add(Allocations.Measure(load, warmUpCalls: 5));
        }

        Assert.All(allocated, bytes => Assert.Equal(allocated[0], bytes));
    }

    private static byte[] File(int generation)
    {
        var text = new StringBuilder("%PDF-2.0\n");
        var offsets = new int[Objects + 1];
        offsets[1] = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"1 {generation} obj\n<< /Type /Catalog /Pages 2 {generation} R >>\nendobj\n");
        offsets[2] = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"2 {generation} obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        for (int number = 3; number <= Objects; number++)
        {
            offsets[number] = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"{number} {generation} obj\n<< /N {number} >>\nendobj\n");
        }

        int xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {Objects + 1}\n0000000000 65535 f \n");
        for (int number = 1; number <= Objects; number++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offsets[number]:D10} {generation:D5} n \n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {Objects + 1} /Root 1 {generation} R /ID [<00> <00>] >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }
}
