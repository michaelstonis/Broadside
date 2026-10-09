using BenchmarkDotNet.Attributes;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Lazy loading through the cross-reference table and the parse-once object cache (ISO 32000-2 §7.5.4, issue #45).
/// <see cref="ResolveCached"/> is the hot path every reference takes once its object is loaded: a lock-free cache lookup that must
/// allocate nothing (<c>Allocated</c> = <c>-</c>). The open-and-resolve benchmarks are baselines: loading allocates the objects.
/// </summary>
[MemoryDiagnoser]
public class LazyLoadingBenchmarks
{
    private string _path = "";
    private byte[] _bytes = [];
    private PdfDocument? _document;
    private CosReference[] _references = [];

    /// <summary>A file of plain objects with an inherited page tree, and one whose objects sit in an object stream.</summary>
    [Params("page-tree-inherited.pdf", "object-stream.pdf")]
    public string File { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _path = Path.Combine(CorpusLocator.CorpusDirectory, File);
        _bytes = System.IO.File.ReadAllBytes(_path);
        _document = PdfDocument.Open(_bytes);
        long size = ((CosInteger)_document.Trailer[new CosName("Size")]).Value;
        _references = [.. Enumerable.Range(1, (int)size - 1).Select(number => new CosReference(number, 0))];
        _ = ResolveAll(_document);
    }

    [GlobalCleanup]
    public void Cleanup() => _document?.Dispose();

    /// <summary>Resolves every object of a document that has loaded them all: cache hits only.</summary>
    [Benchmark]
    public int ResolveCached() => ResolveAll(_document!);

    /// <summary>Opens the file from memory and resolves every object once: one parse each.</summary>
    [Benchmark]
    public int OpenFromBytesAndResolveAll()
    {
        using PdfDocument document = PdfDocument.Open(_bytes);
        return ResolveAll(document);
    }

    /// <summary>Opens the file by path (memory-mapped) and resolves every object once.</summary>
    [Benchmark]
    public int OpenFromPathAndResolveAll()
    {
        using PdfDocument document = PdfDocument.Open(_path);
        return ResolveAll(document);
    }

    private int ResolveAll(PdfDocument document)
    {
        int found = 0;
        foreach (CosReference reference in _references)
        {
            if (document.Resolve(reference) is not CosNull)
            {
                found++;
            }
        }

        return found;
    }
}
