namespace Broadside.Tools.BenchGate.Tests;

/// <summary>The CI job splits the benchmark methods over parallel runners, balanced by the baseline's case count per method.</summary>
public sealed class ShardTests
{
    private static readonly string[] Methods =
    [
        "Broadside.Benchmarks.OpenAndWalkBenchmarks.OpenAndWalk",
        "Broadside.Benchmarks.LexerBenchmarks.Tokenize",
        "Broadside.Benchmarks.FlateBenchmarks.Decode",
        "Broadside.Benchmarks.Ascii85Benchmarks.Decode",
        "Broadside.Benchmarks.NewBenchmarks.NotRecordedYet",
    ];

    [Fact]
    public void Every_method_lands_in_exactly_one_shard_balanced_by_case_count()
    {
        using var tree = new ResultsTree();
        var entries = new List<(string, long)>();
        for (int i = 0; i < 6; i++)
        {
            entries.Add(($"Broadside.Benchmarks.OpenAndWalkBenchmarks.OpenAndWalk(File: \"f{i}.pdf\")", 1000));
        }

        entries.Add(("Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \"a.pdf\")", 0));
        entries.Add(("Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \"b.pdf\")", 0));
        entries.Add(("Broadside.Benchmarks.FlateBenchmarks.Decode", 0));
        entries.Add(("Broadside.Benchmarks.Ascii85Benchmarks.Decode", 0));
        AllocationBaseline baseline = AllocationBaseline.Load(tree.WriteBaseline([.. entries]));

        IReadOnlyList<string> first = BenchmarkGate.Shard(Methods, baseline, count: 2, index: 0);
        IReadOnlyList<string> second = BenchmarkGate.Shard(Methods, baseline, count: 2, index: 1);

        Assert.Equal(["Broadside.Benchmarks.OpenAndWalkBenchmarks.OpenAndWalk"], first);
        Assert.Equal(
            [
                "Broadside.Benchmarks.Ascii85Benchmarks.Decode",
                "Broadside.Benchmarks.FlateBenchmarks.Decode",
                "Broadside.Benchmarks.LexerBenchmarks.Tokenize",
                "Broadside.Benchmarks.NewBenchmarks.NotRecordedYet",
            ],
            second);
    }

    [Fact]
    public void A_split_method_runs_on_every_shard_and_counts_a_share_of_its_cases_on_each()
    {
        using var tree = new ResultsTree();
        var entries = new List<(string, long)>();
        for (int i = 0; i < 6; i++)
        {
            entries.Add(($"Broadside.Benchmarks.OpenAndWalkBenchmarks.OpenAndWalk(File: \"f{i}.pdf\")", 1000));
        }

        entries.Add(("Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \"a.pdf\")", 0));
        entries.Add(("Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \"b.pdf\")", 0));
        entries.Add(("Broadside.Benchmarks.FlateBenchmarks.Decode", 0));
        entries.Add(("Broadside.Benchmarks.Ascii85Benchmarks.Decode", 0));
        AllocationBaseline baseline = AllocationBaseline.Load(tree.WriteBaseline([.. entries]));
        string[] split = ["Broadside.Benchmarks.OpenAndWalkBenchmarks.OpenAndWalk"];

        IReadOnlyList<string> first = BenchmarkGate.Shard(Methods, baseline, count: 2, index: 0, split);
        IReadOnlyList<string> second = BenchmarkGate.Shard(Methods, baseline, count: 2, index: 1, split);

        // Each shard starts at 3 (half of OpenAndWalk's 6 cases); Lexer (2) goes to shard 0, Ascii85 and Flate (1 each) to shard 1,
        // and the unrecorded method (weight 1) to the lower-numbered of two equal shards.
        Assert.Equal(
            [
                "Broadside.Benchmarks.LexerBenchmarks.Tokenize",
                "Broadside.Benchmarks.NewBenchmarks.NotRecordedYet",
                "Broadside.Benchmarks.OpenAndWalkBenchmarks.OpenAndWalk",
            ],
            first);
        Assert.Equal(
            [
                "Broadside.Benchmarks.Ascii85Benchmarks.Decode",
                "Broadside.Benchmarks.FlateBenchmarks.Decode",
                "Broadside.Benchmarks.OpenAndWalkBenchmarks.OpenAndWalk",
            ],
            second);
    }
}
