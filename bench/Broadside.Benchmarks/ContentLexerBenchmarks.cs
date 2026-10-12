using BenchmarkDotNet.Attributes;
using Broadside.Content;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Reads a content stream into operators and operands with the content reader (ISO 32000-2 §7.8.2). A hot path: <c>Allocated</c>
/// must read <c>-</c>; <c>Broadside.Tests.Content.ContentAllocationTests</c> fails the build if it does not.
/// </summary>
[MemoryDiagnoser]
public class ContentLexerBenchmarks
{
    private readonly OperandArena _arena = new();
    private byte[] _content = [];

    /// <summary>The content: a synthetic 1 MB path-heavy stream, or the decoded content of <c>filter-chain.pdf</c>.</summary>
    [Params("path-heavy-1mb", "filter-chain.pdf")]
    public string Content { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        if (Content == "path-heavy-1mb")
        {
            _content = ContentSamples.PathHeavy(1 << 20);
            return;
        }

        using PdfDocument document = PdfDocument.Open(Path.Combine(CorpusLocator.CorpusDirectory, Content));
        var stream = (CosStream)document.Resolve(document.Pages[0].Dictionary[new CosName("Contents")]);
        _content = document.DecodeStream(stream).ToArray();
    }

    [Benchmark]
    public int Read()
    {
        var reader = new ContentReader(_content, _arena);
        int count = 0;
        while (reader.Next(out _))
        {
            _arena.Clear();
            count++;
        }

        _arena.Clear();
        return count;
    }
}
