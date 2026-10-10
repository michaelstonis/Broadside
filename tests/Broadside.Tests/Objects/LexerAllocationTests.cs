using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Objects;

/// <summary>
/// The lexer is a hot path and allocates nothing per token (CLAUDE.md, "Code conventions"). This is the build-breaking half of
/// that rule; <c>LexerBenchmarks</c> in <c>bench/Broadside.Benchmarks</c> is the measuring half. It drives the internal lexer
/// because no public seam exposes tokens.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class LexerAllocationTests
{
    [Fact]
    public void Tokenizing_the_whole_corpus_allocates_nothing()
    {
        byte[] bytes = [.. Corpus.AllFileNames.SelectMany(Corpus.Bytes)];
        int tokens = 0;

        long allocated = Allocations.Measure(() => tokens = Tokenize(bytes), warmUpCalls: 1);

        Assert.True(tokens > 1_000, $"Only {tokens} tokens; the input is too small to show per-token allocations.");
        Assert.Equal(0, allocated);
    }

    private static int Tokenize(ReadOnlySpan<byte> bytes)
    {
        var lexer = new CosLexer(bytes);
        int count = 0;
        while (lexer.Next().Kind != CosTokenKind.EndOfInput)
        {
            count++;
        }

        return count;
    }
}
