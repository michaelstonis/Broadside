using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Broadside.Tools.BenchGate;

/// <summary>One benchmark case of one BenchmarkDotNet run: its key and its measured iterations.</summary>
/// <param name="FullName">BenchmarkDotNet's full name, parameters included: the key the gate and the baseline use.</param>
/// <param name="NanosecondsPerOperation">The measured (Workload, Result) iterations, in nanoseconds per operation.</param>
public sealed record BenchmarkCase(string FullName, IReadOnlyList<double> NanosecondsPerOperation);

/// <summary>
/// The measurements of one gate run, read from a results directory with this layout (any number of shard folders may sit above
/// each <c>round-&lt;n&gt;</c>, <c>confirm</c> or <c>allocations</c> folder; their files are merged):
/// <code>
/// round-1/base/**/*.json    round-1/head/**/*.json    first interleaved pass, base commit and pull request (BenchmarkDotNet)
/// round-2/base/**/*.json    round-2/head/**/*.json    second pass (and so on)
/// confirm/base/**/*.json    confirm/head/**/*.json    rerun of the cases the rounds flagged as slower
/// allocations/base/*.json   allocations/head/*.json   bytes per call (Broadside.Benchmarks --gate-allocations)
/// </code>
/// Under the round and confirm folders every <c>*.json</c> file is read as a BenchmarkDotNet full JSON report
/// (<c>--exporters json</c>); a JSON file of another shape is ignored.
/// </summary>
public sealed class GateResults
{
    private GateResults(SortedDictionary<int, GatePass> rounds, GatePass confirmation, AllocationPass allocations)
    {
        Rounds = rounds;
        Confirmation = confirmation;
        Allocations = allocations;
    }

    /// <summary>The interleaved passes by round number.</summary>
    public IReadOnlyDictionary<int, GatePass> Rounds { get; }

    /// <summary>The confirmation pass; empty when nothing was rerun.</summary>
    public GatePass Confirmation { get; }

    /// <summary>The allocation measurements of both sides.</summary>
    public AllocationPass Allocations { get; }

    /// <summary>The full names of every case the pull request side ran, in the rounds or in the allocation measurement.</summary>
    public IReadOnlyCollection<string> HeadCaseNames()
    {
        var names = new SortedSet<string>(Allocations.Head.Keys, StringComparer.Ordinal);
        foreach (GatePass pass in Rounds.Values)
        {
            names.UnionWith(pass.Head.Keys);
        }

        return names;
    }

    /// <summary>Reads every measurement under <paramref name="root"/>.</summary>
    public static GateResults Load(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Results directory '{root}' does not exist.");
        }

        var rounds = new SortedDictionary<int, GatePass>();
        var confirmation = new GatePass();
        var allocations = new AllocationPass();

        // Read per runner (the folders above round-<n> or confirm) first: a base case counts only where the same runner also
        // measured the pull request's case, so a time ratio never mixes two machines.
        var perRunner = new Dictionary<(string Runner, GatePass Pass), GatePass>();
        foreach (string file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string[] parts = Path.GetRelativePath(root, file).Replace('\\', '/').Split('/');
            if (TryLocateAllocations(parts, out bool isHeadAllocation))
            {
                foreach ((string name, long bytes) in ReadAllocations(file))
                {
                    Dictionary<string, List<long>> side = isHeadAllocation ? allocations.Head : allocations.Base;
                    if (!side.TryGetValue(name, out List<long>? values))
                    {
                        side[name] = values = [];
                    }

                    values.Add(bytes);
                }

                continue;
            }

            if (!TryLocate(parts, out GatePass? pass, out bool isHead, out string? runner, rounds, confirmation))
            {
                continue;
            }

            if (!perRunner.TryGetValue((runner, pass), out GatePass? staged))
            {
                perRunner[(runner, pass)] = staged = new GatePass();
            }

            foreach (BenchmarkCase benchmark in ReadReport(file))
            {
                (isHead ? staged.Head : staged.Base)[benchmark.FullName] = benchmark;
            }
        }

        foreach (((_, GatePass pass), GatePass staged) in perRunner)
        {
            foreach ((string name, BenchmarkCase head) in staged.Head)
            {
                pass.Head[name] = head;
                if (staged.Base.TryGetValue(name, out BenchmarkCase? @base))
                {
                    pass.Base[name] = @base;
                }
            }
        }

        return new GateResults(rounds, confirmation, allocations);
    }

    /// <summary>Reads one BenchmarkDotNet full JSON report; a JSON file of another shape yields nothing.</summary>
    public static IReadOnlyList<BenchmarkCase> ReadReport(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("Benchmarks", out JsonElement benchmarks)
            || benchmarks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var cases = new List<BenchmarkCase>();
        foreach (JsonElement benchmark in benchmarks.EnumerateArray())
        {
            string? fullName = String(benchmark, "FullName");
            if (fullName is null)
            {
                continue;
            }

            var nanoseconds = new List<double>();
            if (benchmark.TryGetProperty("Measurements", out JsonElement measurements) && measurements.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement measurement in measurements.EnumerateArray())
                {
                    if (String(measurement, "IterationMode") == "Workload"
                        && String(measurement, "IterationStage") == "Result"
                        && measurement.TryGetProperty("Operations", out JsonElement operations)
                        && measurement.TryGetProperty("Nanoseconds", out JsonElement elapsed)
                        && operations.GetDouble() > 0)
                    {
                        nanoseconds.Add(elapsed.GetDouble() / operations.GetDouble());
                    }
                }
            }

            cases.Add(new BenchmarkCase(fullName, nanoseconds));
        }

        return cases;
    }

    /// <summary>Reads one <c>--gate-allocations</c> file: <c>{ "Benchmarks": { "&lt;full name&gt;": &lt;bytes per call&gt;, ... } }</c>.</summary>
    public static IReadOnlyList<(string FullName, long Bytes)> ReadAllocations(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("Benchmarks", out JsonElement benchmarks)
            || benchmarks.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return [.. benchmarks.EnumerateObject()
            .Where(static property => property.Value.ValueKind == JsonValueKind.Number)
            .Select(static property => (property.Name, property.Value.GetInt64()))];
    }

    private static bool TryLocateAllocations(string[] parts, out bool isHead)
    {
        isHead = parts.Length >= 3 && parts[^2] == "head";
        return parts.Length >= 3 && parts[^3] == "allocations" && (isHead || parts[^2] == "base");
    }

    private static bool TryLocate(
        string[] parts,
        [NotNullWhen(true)] out GatePass? pass,
        out bool isHead,
        [NotNullWhen(true)] out string? runner,
        SortedDictionary<int, GatePass> rounds,
        GatePass confirmation)
    {
        pass = null;
        isHead = false;
        runner = null;
        for (int i = parts.Length - 2; i >= 1; i--)
        {
            bool head = parts[i] == "head";
            if (!head && parts[i] != "base")
            {
                continue;
            }

            string folder = parts[i - 1];
            if (folder == "confirm")
            {
                pass = confirmation;
            }
            else if (folder.StartsWith("round-", StringComparison.Ordinal)
                && int.TryParse(folder.AsSpan("round-".Length), NumberStyles.None, CultureInfo.InvariantCulture, out int round))
            {
                if (!rounds.TryGetValue(round, out pass))
                {
                    rounds[round] = pass = new GatePass();
                }
            }
            else
            {
                continue;
            }

            isHead = head;
            runner = string.Join('/', parts[..(i - 1)]);
            return true;
        }

        return false;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>One timing pass: the base commit's and the pull request's cases, keyed by full name.</summary>
public sealed class GatePass
{
    /// <summary>The base commit's cases.</summary>
    public Dictionary<string, BenchmarkCase> Base { get; } = new(StringComparer.Ordinal);

    /// <summary>The pull request's cases.</summary>
    public Dictionary<string, BenchmarkCase> Head { get; } = new(StringComparer.Ordinal);
}

/// <summary>The allocation measurements: bytes per call by full name, one value per measuring process.</summary>
public sealed class AllocationPass
{
    /// <summary>The base commit's figures.</summary>
    public Dictionary<string, List<long>> Base { get; } = new(StringComparer.Ordinal);

    /// <summary>The pull request's figures.</summary>
    public Dictionary<string, List<long>> Head { get; } = new(StringComparer.Ordinal);
}
