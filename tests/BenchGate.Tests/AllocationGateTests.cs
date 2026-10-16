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
        Assert.Equal("allocates 24 B per call; the baseline is 0 B", finding.Message);
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
        Assert.Equal("allocates 0 B per call; the baseline is 64 B", finding.Message);
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

    [Fact]
    public void A_case_that_ran_without_an_allocation_figure_fails()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("head", 1, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("round-1/head", ResultsTree.Flate, 0, ResultsTree.Around(500));
        File.Delete(Path.Combine(tree.Root, "allocations", "head", "round-1.json"));
        tree.AddAllocation("allocations/head", "round-1", ResultsTree.Lexer, 0);
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0), (ResultsTree.Flate, 0));

        GateFinding finding = Assert.Single(BenchmarkGate.Run(baseline, tree.Root).Failures);

        Assert.Equal(ResultsTree.Flate, finding.Benchmark);
        Assert.Equal(FindingKind.NoAllocationFigure, finding.Kind);
    }

    [Fact]
    public void A_range_in_the_baseline_file_is_refused()
    {
        using var tree = new ResultsTree();
        string ranged = tree.WriteBaselineText(
            """{ "Benchmarks": { "Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \"empty-page.pdf\")": { "Min": 6800000, "Max": 6820000 } } }""");

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => AllocationBaseline.Load(ranged));

        Assert.Contains("must be a whole number of bytes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Figures_that_differ_between_processes_fail_even_when_one_matches_the_baseline()
    {
        using var tree = new ResultsTree();
        tree.Add("round-1/head", ResultsTree.Lexer, 1000, ResultsTree.Around(1000))
            .Add("round-2/head", ResultsTree.Lexer, 1040, ResultsTree.Around(1000));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 1000));

        GateFinding finding = Assert.Single(BenchmarkGate.Run(baseline, tree.Root).Failures);

        Assert.Equal(FindingKind.AllocationChanged, finding.Kind);
        Assert.Contains("varies between processes", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recording_writes_one_exact_figure_per_case()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("head", 2, ResultsTree.Lexer, 1000, ResultsTree.Around(1000));

        AllocationBaseline recorded = BenchmarkGate.Record(tree.Root, Path.Combine(tree.Root, "recorded.json"));

        Assert.Equal(1000, recorded.Allocations[ResultsTree.Lexer]);
        Assert.Contains("\"Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \\\"empty-page.pdf\\\")\": 1000", File.ReadAllText(Path.Combine(tree.Root, "recorded.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_base_commit_that_already_differs_from_the_baseline_is_a_stale_baseline_notice()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 32, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.True(report.Passed);
        Assert.Equal(FindingKind.BaselineStale, Assert.Single(report.Findings).Kind);
    }
}
