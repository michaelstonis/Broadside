using BenchmarkDotNet.Attributes;
using Broadside.Layout.Flow;

namespace Broadside.Benchmarks;

/// <summary>
/// Line break opportunities by UAX #14, Unicode Line Breaking Algorithm (Unicode 16.0.0, issue #172): one paragraph of mixed Latin,
/// CJK, Hangul, numbers, quotation marks and emoji, about 4,000 UTF-16 code units, into a reused list. Flow layout asks for this
/// once per paragraph, so <see cref="FindBreaks"/> must allocate nothing per character (<c>Allocated</c> = <c>-</c>).
/// </summary>
[MemoryDiagnoser]
public class LineBreakingBenchmarks
{
    private readonly List<BreakOpportunity> _breaks = new(4096);
    private string _text = "";

    [GlobalSetup]
    public void Setup()
    {
        _text = string.Concat(Enumerable.Repeat(
            "The well-known café’s “menu” costs $12.50 (incl. tax); 日本語のテキスト, "
            + "한국어 텍스트 and 👍🏽 emoji­code.\n",
            48));
        _ = FindBreaks();
    }

    /// <summary>The break opportunities of the paragraph.</summary>
    [Benchmark]
    public int FindBreaks()
    {
        _breaks.Clear();
        UnicodeLineBreaker.Instance.FindBreaks(_text, _breaks);
        return _breaks.Count;
    }
}
