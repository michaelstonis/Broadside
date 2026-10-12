namespace Broadside.Tools.BenchGate.Tests;

/// <summary>The job summary names every regression, and real BenchmarkDotNet reports are read as the gate expects.</summary>
public sealed class SummaryTests
{
    [Fact]
    public void The_summary_has_one_row_per_regression()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 40, ResultsTree.Around(1000))
            .AddRounds("base", 2, ResultsTree.Flate, 0, ResultsTree.Around(500))
            .AddRounds("head", 2, ResultsTree.Flate, 0, ResultsTree.Around(650))
            .Add("confirm/base", ResultsTree.Flate, 0, ResultsTree.Around(500))
            .Add("confirm/head", ResultsTree.Flate, 0, ResultsTree.Around(650));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0), (ResultsTree.Flate, 0));

        string summary = BenchmarkGate.Run(baseline, tree.Root).ToMarkdown("abc1234");

        Assert.Contains("**Failed**: 2 regression(s) over 2 benchmark case(s).", summary, StringComparison.Ordinal);
        Assert.Contains("times with the base commit `abc1234`", summary, StringComparison.Ordinal);
        Assert.Contains(
            "| `LexerBenchmarks.Tokenize(File: \"empty-page.pdf\")` | allocation changed | allocates 40 B per call; the baseline is 0 B |",
            summary,
            StringComparison.Ordinal);
        Assert.Contains(
            "| `FlateBenchmarks.Decode` | slower | 1.30x: 500.00 ns on base, 650.00 ns here (rounds 1.30, 1.30; rerun 1.30) |",
            summary,
            StringComparison.Ordinal);
        Assert.Contains("bench/baselines/gate/README.md", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_shows_the_baseline_lines_an_intended_change_needs()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("head", 1, ResultsTree.Lexer, 40, ResultsTree.Around(1000))
            .AddRounds("head", 1, ResultsTree.Flate, 16, ResultsTree.Around(500));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0), ("Broadside.Benchmarks.GoneBenchmarks.Gone", 8));

        string summary = BenchmarkGate.Run(baseline, tree.Root).ToMarkdown();

        Assert.Contains(
            """
            ```diff
            -    "Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \"empty-page.pdf\")": 0,
            +    "Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \"empty-page.pdf\")": 40,
            +    "Broadside.Benchmarks.FlateBenchmarks.Decode": 16,
            -    "Broadside.Benchmarks.GoneBenchmarks.Gone": 8,
            ```
            """.ReplaceLineEndings(),
            summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_passing_summary_says_so()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        string summary = BenchmarkGate.Run(baseline, tree.Root).ToMarkdown();

        Assert.StartsWith("## Benchmark gate", summary, StringComparison.Ordinal);
        Assert.Contains("**Passed** over 1 benchmark case(s).", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("| Benchmark | Finding |", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_BenchmarkDotNet_report_is_read()
    {
        // The Phase 1 baseline's lexer report (#48): BenchmarkDotNet 0.15's full compressed JSON exporter.
        string path = Path.Combine(RepositoryRoot(), "bench", "baselines", "phase1", "LexerBenchmarks.json");

        IReadOnlyList<BenchmarkCase> cases = GateResults.ReadReport(path);

        BenchmarkCase first = cases[0];
        Assert.Equal("Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \"empty-page.pdf\")", first.FullName);
        Assert.Equal(31, first.NanosecondsPerOperation.Count); // Statistics.N in the report
        Assert.InRange(first.NanosecondsPerOperation.Average(), 1603.2, 1603.3); // Statistics.Mean
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Broadside.slnx not found above the test assembly.");
    }
}
