using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Opens a merge of corpus documents that repeats the same font program and images, and saves it in full with and without
/// <see cref="PdfSaveOptions.MergeDuplicateObjects"/> (issue #634; ISO 32000-2 §7.3.10, §7.5.3). Finding duplicates reads every
/// candidate stream's encoded data once and hashes every candidate's structure once per round. Saving builds output by design, so
/// <c>Allocated</c> is a baseline to watch, not a zero to assert; the difference between the two methods is the merge pass.
/// </summary>
[MemoryDiagnoser]
public class DuplicateMergeBenchmarks
{
    private byte[] _bytes = [];

    /// <summary>How many copies of each source document the merged file holds.</summary>
    [Params(2, 16)]
    public int Copies { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sources = new List<PdfDocument>();
        try
        {
            for (int copy = 0; copy < Copies; copy++)
            {
                sources.Add(PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, "text-truetype-embedded.pdf")));
                sources.Add(PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, "duplicate-images.pdf")));
            }

            using PdfDocument merged = PdfDocument.Merge(sources);
            using var output = new MemoryStream();
            merged.Save(output, new PdfSaveOptions());
            _bytes = output.ToArray();
        }
        finally
        {
            foreach (PdfDocument source in sources)
            {
                source.Dispose();
            }
        }
    }

    /// <summary>The full save without merging, for comparison.</summary>
    [Benchmark(Baseline = true)]
    public void OpenAndSave()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        document.Save(Stream.Null, new PdfSaveOptions());
    }

    /// <summary>The full save with duplicate objects merged.</summary>
    [Benchmark]
    public void OpenAndSaveMerged()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        document.Save(Stream.Null, new PdfSaveOptions().WithMergeDuplicateObjects(true));
    }
}
