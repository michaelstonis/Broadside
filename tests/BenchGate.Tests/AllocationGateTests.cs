namespace Broadside.Tools.BenchGate.Tests;

/// <summary>Allocations are compared with the stored baseline and must match it exactly, in either direction.</summary>
public sealed class AllocationGateTests
{
    [Fact]
    public void Allocations_equal_to_the_baseline_pass()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.True(report.Passed);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void An_allocation_the_baseline_does_not_have_fails()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 24, ResultsTree.Around(1000));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.False(report.Passed);
        GateFinding finding = Assert.Single(report.Failures);
        Assert.Equal(ResultsTree.Lexer, finding.Benchmark);
        Assert.Equal(FindingKind.AllocationChanged, finding.Kind);
        Assert.Equal("allocates 24 B per operation; the baseline is 0 B", finding.Message);
    }

    [Fact]
    public void Fewer_allocations_than_the_baseline_also_fail_until_the_baseline_is_updated()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 64, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 64));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        GateFinding finding = Assert.Single(report.Failures);
        Assert.Equal(FindingKind.AllocationChanged, finding.Kind);
        Assert.Equal("allocates 0 B per operation; the baseline is 64 B", finding.Message);
    }

    [Fact]
    public void A_benchmark_missing_from_the_baseline_fails()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Flate, 0, ResultsTree.Around(500));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        GateFinding finding = Assert.Single(report.Failures);
        Assert.Equal(ResultsTree.Flate, finding.Benchmark);
        Assert.Equal(FindingKind.NotInBaseline, finding.Kind);
    }

    [Fact]
    public void A_baseline_benchmark_that_no_longer_runs_fails()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0), (ResultsTree.Flate, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        GateFinding finding = Assert.Single(report.Failures);
        Assert.Equal(ResultsTree.Flate, finding.Benchmark);
        Assert.Equal(FindingKind.NotRun, finding.Kind);
    }

    [Fact]
    public void Recording_writes_a_baseline_that_the_same_results_pass()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Flate, 112, ResultsTree.Around(500));
        string recorded = Path.Combine(tree.Root, "recorded.json");

        BenchmarkGate.Record(tree.Root, recorded);
        GateReport report = BenchmarkGate.Run(recorded, tree.Root);

        Assert.True(report.Passed);
        Assert.Equal(
            """
            {
              "Benchmarks": {
                "Broadside.Benchmarks.FlateBenchmarks.Decode": 112,
                "Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \"empty-page.pdf\")": 0
              }
            }

            """.ReplaceLineEndings("\n"),
            File.ReadAllText(recorded));
    }
}
