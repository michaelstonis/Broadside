namespace Broadside.Tools.BenchGate.Tests;

/// <summary>The Native AOT size probes' gate (issue #361): growth past the tolerance, new and missing probes fail; shrinkage is noted.</summary>
public sealed class SizeGateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sizegate-" + Guid.NewGuid().ToString("N"));

    public SizeGateTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Sizes_within_the_tolerance_pass()
    {
        string baseline = Write("baseline.json", ("osx-arm64", "render", 1_000_000), ("osx-arm64", "save", 2_000_000));
        string results = Write("results.json", ("osx-arm64", "render", 1_009_000), ("osx-arm64", "save", 1_995_000));

        SizeReport report = SizeGate.Run(baseline, results);

        Assert.True(report.Passed);
        Assert.All(report.Rows, row => Assert.Equal(SizeVerdict.Unchanged, row.Verdict));
    }

    [Fact]
    public void A_probe_that_grows_past_the_tolerance_fails()
    {
        string baseline = Write("baseline.json", ("osx-arm64", "render", 1_000_000));
        string results = Write("results.json", ("osx-arm64", "render", 1_020_000));

        SizeReport report = SizeGate.Run(baseline, results);

        Assert.False(report.Passed);
        SizeRow row = Assert.Single(report.Failures);
        Assert.Equal(SizeVerdict.Grew, row.Verdict);
        Assert.Contains("| osx-arm64 | render | 1,000,000 | 1,020,000 | +20,000 B (+2.00%) | **grew** |", report.ToMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_probe_that_shrinks_past_the_tolerance_passes_and_asks_for_a_new_record()
    {
        string baseline = Write("baseline.json", ("osx-arm64", "save", 2_000_000));
        string results = Write("results.json", ("osx-arm64", "save", 1_500_000));

        SizeReport report = SizeGate.Run(baseline, results);

        Assert.True(report.Passed);
        Assert.Equal(SizeVerdict.Shrank, Assert.Single(report.Rows).Verdict);
        Assert.Contains("re-record", report.ToMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_probe_and_a_missing_probe_fail()
    {
        string baseline = Write("baseline.json", ("osx-arm64", "render", 1_000_000), ("osx-arm64", "sign", 3_000_000));
        string results = Write("results.json", ("osx-arm64", "render", 1_000_000), ("osx-arm64", "extract-text", 900_000));

        SizeReport report = SizeGate.Run(baseline, results);

        Assert.False(report.Passed);
        Assert.Equal(
            [("extract-text", SizeVerdict.New), ("sign", SizeVerdict.Missing)],
            report.Failures.Select(row => (row.Probe, row.Verdict)).OrderBy(pair => pair.Probe, StringComparer.Ordinal));
    }

    [Fact]
    public void A_runtime_without_a_baseline_fails_and_other_runtimes_are_not_required()
    {
        string baseline = Write("baseline.json", ("osx-arm64", "render", 1_000_000));
        string results = Write("results.json", ("linux-x64", "render", 1_100_000));

        SizeReport report = SizeGate.Run(baseline, results);

        SizeRow row = Assert.Single(report.Rows);
        Assert.Equal(("linux-x64", SizeVerdict.New), (row.Rid, row.Verdict));
    }

    [Fact]
    public void Recording_replaces_the_measured_runtime_and_keeps_the_others()
    {
        string baseline = Write("baseline.json", ("osx-arm64", "render", 1_000_000), ("linux-x64", "render", 1_100_000));
        string results = Write("results.json", ("osx-arm64", "render", 900_000), ("osx-arm64", "save", 2_000_000));

        SizeGate.Record(baseline, results);

        SortedDictionary<string, SortedDictionary<string, long>> recorded = SizeGate.Load(baseline);
        Assert.Equal(1_100_000, recorded["linux-x64"]["render"]);
        Assert.Equal(900_000, recorded["osx-arm64"]["render"]);
        Assert.Equal(2_000_000, recorded["osx-arm64"]["save"]);
        Assert.EndsWith("}\n", File.ReadAllText(baseline), StringComparison.Ordinal);
    }

    [Fact]
    public void The_stored_baseline_reads()
    {
        string path = Path.Combine(RepositoryRoot(), "bench", "baselines", "gate", "sizes.json");

        SortedDictionary<string, SortedDictionary<string, long>> sizes = SizeGate.Load(path);

        Assert.Contains("osx-arm64", sizes.Keys);
        Assert.True(sizes["osx-arm64"]["extract-text"] < sizes["osx-arm64"]["render"]);
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

    private string Write(string name, params (string Rid, string Probe, long Bytes)[] sizes)
    {
        string path = Path.Combine(_directory, name);
        var byRuntime = new SortedDictionary<string, SortedDictionary<string, long>>(StringComparer.Ordinal);
        foreach ((string rid, string probe, long bytes) in sizes)
        {
            if (!byRuntime.TryGetValue(rid, out SortedDictionary<string, long>? probes))
            {
                byRuntime[rid] = probes = new SortedDictionary<string, long>(StringComparer.Ordinal);
            }

            probes[probe] = bytes;
        }

        SizeGate.Save(byRuntime, path);
        return path;
    }
}
