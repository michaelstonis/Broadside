using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Placeholder that exercises the harness end to end: locate the corpus, read every file. It is replaced by real
/// parser and renderer benchmarks as those land; keep <see cref="MemoryDiagnoserAttribute"/> on every benchmark class so
/// allocation regressions on hot paths are visible (see CLAUDE.md, "Code conventions").
/// </summary>
[MemoryDiagnoser]
public class CorpusBenchmarks
{
    private string[] _files = [];

    [GlobalSetup]
    public void Setup() => _files = CorpusLocator.CorpusFiles();

    [Benchmark]
    public long ReadAllCorpusFiles()
    {
        long total = 0;
        foreach (string file in _files)
        {
            total += File.ReadAllBytes(file).Length;
        }

        return total;
    }
}
