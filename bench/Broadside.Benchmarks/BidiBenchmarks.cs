using BenchmarkDotNet.Attributes;
using Broadside.Layout.Flow;

namespace Broadside.Benchmarks;

/// <summary>
/// Bidi levels by UAX #9, Unicode Bidirectional Algorithm (Unicode 16.0.0, issue #173): paragraphs of mixed Hebrew, Arabic,
/// Latin, numbers, brackets, an isolate and an embedding, about 4,000 UTF-16 code units, into a reused level buffer. Flow layout
/// resolves once per paragraph, so <see cref="Resolve"/> must allocate nothing per character (<c>Allocated</c> = <c>-</c>).
/// </summary>
[MemoryDiagnoser]
public class BidiBenchmarks
{
    private const string Sample =
        "\u05E9\u05DC\u05D5\u05DD (world) 123.45 \u05E2\u05D5\u05DC\u05DD [\u0627\u0644\u0639\u0631\u0628\u064A\u0629 \u0661\u0662] "
        + "\u2067abc\u2069 \u202Bdef\u202C mixed\u2029";

    private string _text = "";
    private byte[] _levels = [];

    [GlobalSetup]
    public void Setup()
    {
        _text = string.Concat(Enumerable.Repeat(Sample, 50));
        _levels = new byte[_text.Length];
        _ = Resolve();
    }

    /// <summary>The embedding levels of the paragraphs.</summary>
    [Benchmark]
    public int Resolve()
    {
        UnicodeBidiResolver.Instance.Resolve(_text, TextDirection.Auto, _levels);
        return _levels[^2];
    }
}
