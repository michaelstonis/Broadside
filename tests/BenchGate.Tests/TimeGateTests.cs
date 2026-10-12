namespace Broadside.Tools.BenchGate.Tests;

/// <summary>Times are compared with the base commit measured in the same job, within the tolerance of <see cref="GateOptions"/>.</summary>
public sealed class TimeGateTests
{
    [Fact]
    public void A_thirty_percent_slowdown_in_every_round_and_the_rerun_fails()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1300))
            .Add("confirm/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("confirm/head", ResultsTree.Lexer, 0, ResultsTree.Around(1300));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.False(report.Passed);
        GateFinding finding = Assert.Single(report.Failures);
        Assert.Equal(FindingKind.Slower, finding.Kind);
        Assert.Equal("1.30x: 1.000 us on base, 1.300 us here (rounds 1.30, 1.30; rerun 1.30)", finding.Message);
    }

    [Fact]
    public void Run_to_run_noise_passes()
    {
        using var tree = new ResultsTree();
        double[] quiet = ResultsTree.Around(1000);
        double[] noisy = [1080, 950, 1120, 990, 1040, 1010, 1150, 970, 1000, 1060];
        tree.Add("round-1/base", ResultsTree.Lexer, 0, quiet)
            .Add("round-1/head", ResultsTree.Lexer, 0, noisy)
            .Add("round-2/base", ResultsTree.Lexer, 0, noisy)
            .Add("round-2/head", ResultsTree.Lexer, 0, quiet);
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.True(report.Passed);
        Assert.Empty(report.Findings);
        Assert.Empty(BenchmarkGate.Suspects(tree.Root));
    }

    [Fact]
    public void A_slowdown_in_one_round_only_is_runner_drift_and_a_notice()
    {
        using var tree = new ResultsTree();
        tree.Add("round-1/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("round-1/head", ResultsTree.Lexer, 0, ResultsTree.Around(1600))
            .Add("round-2/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("round-2/head", ResultsTree.Lexer, 0, ResultsTree.Around(1020));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.True(report.Passed);
        Assert.Equal(FindingKind.SlowerWithinTolerance, Assert.Single(report.Findings).Kind);
        Assert.Empty(BenchmarkGate.Suspects(tree.Root));
    }

    [Fact]
    public void A_slowdown_the_rerun_does_not_reproduce_is_a_notice()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1300))
            .Add("confirm/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("confirm/head", ResultsTree.Lexer, 0, ResultsTree.Around(1030));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.True(report.Passed);
        GateFinding finding = Assert.Single(report.Findings);
        Assert.Equal(FindingKind.SlowerWithinTolerance, finding.Kind);
        Assert.Contains("rerun 1.03", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Slower_cases_are_the_suspects_to_rerun_by_full_name()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1300))
            .AddRounds("base", 2, ResultsTree.Flate, 0, ResultsTree.Around(500))
            .AddRounds("head", 2, ResultsTree.Flate, 0, ResultsTree.Around(505));

        IReadOnlyList<string> suspects = BenchmarkGate.Suspects(tree.Root);

        Assert.Equal([ResultsTree.Lexer], suspects);
    }

    [Fact]
    public void A_nanosecond_difference_is_never_a_regression()
    {
        using var tree = new ResultsTree();
        tree.AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(2.0))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(2.8))
            .Add("confirm/base", ResultsTree.Lexer, 0, ResultsTree.Around(2.0))
            .Add("confirm/head", ResultsTree.Lexer, 0, ResultsTree.Around(2.8));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.True(report.Passed);
    }

    [Fact]
    public void Base_iterations_pair_only_with_the_pull_request_iterations_of_the_same_runner()
    {
        // A base commit older than BROADSIDE_BENCH_PARTITION runs every case on every shard; only the shard that also ran the
        // pull request's case measured both on one machine.
        using var tree = new ResultsTree();
        tree.Add("shard-0/round-1/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("shard-0/round-1/head", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("shard-1/round-1/base", ResultsTree.Lexer, 0, ResultsTree.Around(2000))
            .Add("shard-9/round-1/base", ResultsTree.Lexer, 0, ResultsTree.Around(400));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.Empty(report.Findings);
        Assert.InRange(Assert.Single(report.Times).Ratio, 0.99, 1.01);
    }

    [Fact]
    public void Shards_are_merged_by_round()
    {
        using var tree = new ResultsTree();
        tree.Add("shard-0/round-1/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("shard-0/round-1/head", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("shard-1/round-1/base", ResultsTree.Flate, 0, ResultsTree.Around(500))
            .Add("shard-1/round-1/head", ResultsTree.Flate, 0, ResultsTree.Around(500));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0), (ResultsTree.Flate, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.True(report.Passed);
        Assert.Equal(2, report.CaseCount);
        Assert.Equal(1, report.RoundCount);
        Assert.Equal(2, report.Times.Count);
    }
}
