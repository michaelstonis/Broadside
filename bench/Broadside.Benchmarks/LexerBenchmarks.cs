using BenchmarkDotNet.Attributes;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Tokenizes a whole corpus file with the COS lexer (ISO 32000-2 §7.2). The lexer is a hot path: <c>Allocated</c> must read
/// <c>-</c>; <c>Broadside.Tests.Objects.LexerAllocationTests</c> fails the build if it does not.
/// </summary>
[MemoryDiagnoser]
public class LexerBenchmarks
{
    private byte[] _bytes = [];

    /// <summary>The corpus file to tokenize: a text-only file and one with binary stream data.</summary>
    [Params("empty-page.pdf", "text-truetype-embedded.pdf")]
    public string File { get; set; } = "";

    [GlobalSetup]
    public void Setup() => _bytes = System.IO.File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, File));

    [Benchmark]
    public int Tokenize()
    {
        var lexer = new CosLexer(_bytes);
        int count = 0;
        while (lexer.Next().Kind != CosTokenKind.EndOfInput)
        {
            count++;
        }

        return count;
    }
}
