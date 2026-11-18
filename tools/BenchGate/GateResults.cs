using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Broadside.Tools.BenchGate;

/// <summary>
/// One benchmark case of one side of one pass: its key and its measured iterations, from one measuring process or (a rerun,
/// issue #554) several.
/// </summary>
/// <param name="FullName">BenchmarkDotNet's full name, parameters included: the key the gate and the baseline use.</param>
/// <param name="NanosecondsPerOperation">The measured (Workload, Result) iterations, in nanoseconds per operation, of every process.</param>
public sealed record BenchmarkCase(string FullName, IReadOnlyList<double> NanosecondsPerOperation)
{
    /// <summary>The same iterations by measuring process, in the order the reports were read: one list unless several processes measured the case.</summary>
    public IReadOnlyList<IReadOnlyList<double>> Processes { get; init; } = [NanosecondsPerOperation];

    /// <summary>This case with the iterations of another process that measured it in the same pass.</summary>
    public BenchmarkCase Merge(BenchmarkCase other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new BenchmarkCase(FullName, [.. NanosecondsPerOperation, .. other.NanosecondsPerOperation])
        {
            Processes = [.. Processes, .. other.Processes],
        };
    }
}

/// <summary>
/// The measurements of one gate run, read from a results directory with this layout (any number of shard folders may sit above
/// each <c>round-&lt;n&gt;</c>, <c>confirm</c> or <c>allocations</c> folder; their files are merged):
/// <code>
/// machine.json              the runner's processor and AVX-512 state (BenchGate machine); decides the timing pass
/// round-1/base/**/*.json    round-1/head/**/*.json    first interleaved pass, base commit and pull request (BenchmarkDotNet)
/// round-2/base/**/*.json    round-2/head/**/*.json    second pass (and so on)
/// confirm/base/**/*.json    confirm/head/**/*.json    rerun of the cases the rounds flagged as slower (one report per process)
/// confirm-2/base/**/*.json  confirm-2/head/**/*.json  second rerun, when the job runs more than one (and so on)
/// allocations/base/*.json   allocations/head/*.json   bytes per call (Broadside.Benchmarks --gate-allocations)
/// </code>
/// Under the round and confirm folders every <c>*.json</c> file is read as a BenchmarkDotNet full JSON report
/// (<c>--exporters json</c>); a JSON file of another shape is ignored. The rounds and reruns of a folder whose
/// <c>machine.json</c> says AVX-512 was disabled form the <see cref="TimePass.Avx512Off"/> pass, compared on their own; all
/// others form the <see cref="TimePass.Default"/> pass.
/// </summary>
public sealed class GateResults
{
    private GateResults(SortedDictionary<TimePass, TimingResults> timings, AllocationPass allocations, SortedDictionary<string, MachineInfo> machines)
    {
        Timings = timings;
        Allocations = allocations;
        Machines = machines;
    }

    /// <summary>The timing passes that have at least one round or rerun, by pass.</summary>
    public IReadOnlyDictionary<TimePass, TimingResults> Timings { get; }

    /// <summary>The interleaved rounds of the default pass by round number.</summary>
    public IReadOnlyDictionary<int, GatePass> Rounds => Timings.TryGetValue(TimePass.Default, out TimingResults? timing) ? timing.Rounds : new SortedDictionary<int, GatePass>();

    /// <summary>The allocation measurements of both sides.</summary>
    public AllocationPass Allocations { get; }

    /// <summary>The machines the jobs measured on, by results folder (relative to the results directory, <c>.</c> for the root).</summary>
    public IReadOnlyDictionary<string, MachineInfo> Machines { get; }

    /// <summary>
    /// The full names of every case the pull request side ran, in the default pass's rounds or in the allocation measurement. The
    /// AVX-512-off pass times the same cases and measures no allocations, so it adds none.
    /// </summary>
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

        var timings = new SortedDictionary<TimePass, TimingResults>();
        var allocations = new AllocationPass();
        var machines = new SortedDictionary<string, MachineInfo>(StringComparer.Ordinal);
        string[] files = [.. Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        foreach (string file in files.Where(static file => Path.GetFileName(file) == MachineInfo.FileName))
        {
            string folder = Path.GetRelativePath(root, Path.GetDirectoryName(file)!).Replace('\\', '/');
            machines[folder] = MachineInfo.Load(file);
        }

        // Read per runner (the folders above round-<n> or confirm) first: a base case counts only where the same runner also
        // measured the pull request's case, so a time ratio never mixes two machines.
        var perRunner = new Dictionary<(string Runner, GatePass Pass), GatePass>();
        foreach (string file in files)
        {
            string[] parts = Path.GetRelativePath(root, file).Replace('\\', '/').Split('/');
            if (parts[^1] == MachineInfo.FileName)
            {
                continue;
            }

            if (TryLocateAllocations(parts, out bool isHeadAllocation))
            {
                foreach (string name in ReadNotGated(file))
                {
                    allocations.NotGated.Add(name);
                }

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

            if (!TryLocate(parts, out string? folder, out int number, out bool isHead, out string? runner))
            {
                continue;
            }

            TimePass timePass = machines.TryGetValue(runner.Length == 0 ? "." : runner, out MachineInfo? machine) ? machine.Pass : TimePass.Default;
            if (!timings.TryGetValue(timePass, out TimingResults? timing))
            {
                timings[timePass] = timing = new TimingResults();
            }

            SortedDictionary<int, GatePass> passes = folder == "confirm" ? timing.Confirmations : timing.Rounds;
            if (!passes.TryGetValue(number, out GatePass? pass))
            {
                passes[number] = pass = new GatePass();
            }

            if (!perRunner.TryGetValue((runner, pass), out GatePass? staged))
            {
                perRunner[(runner, pass)] = staged = new GatePass();
            }

            // A case read twice for the same side of the same pass was measured by several processes (the reruns, issue #554):
            // keep every process's iterations.
            Dictionary<string, BenchmarkCase> cases = isHead ? staged.Head : staged.Base;
            foreach (BenchmarkCase benchmark in ReadReport(file))
            {
                cases[benchmark.FullName] = cases.TryGetValue(benchmark.FullName, out BenchmarkCase? earlier) ? earlier.Merge(benchmark) : benchmark;
            }
        }

        foreach (((string runner, GatePass pass), GatePass staged) in perRunner)
        {
            foreach ((string name, BenchmarkCase head) in staged.Head)
            {
                pass.Head[name] = head;
                pass.Runners[name] = runner.Length == 0 ? "." : runner;
                if (staged.Base.TryGetValue(name, out BenchmarkCase? @base))
                {
                    pass.Base[name] = @base;
                }
            }
        }

        return new GateResults(timings, allocations, machines);
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

    /// <summary>
    /// Reads the cases a <c>--gate-allocations</c> file lists under <c>NotGated</c>: the ones carrying the
    /// <c>NoAllocationGate</c> benchmark category, which the exact allocation comparison leaves out.
    /// </summary>
    public static IReadOnlyList<string> ReadNotGated(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("NotGated", out JsonElement names)
            || names.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. names.EnumerateArray().Where(static name => name.ValueKind == JsonValueKind.String).Select(static name => name.GetString()!)];
    }

    private static bool TryLocateAllocations(string[] parts, out bool isHead)
    {
        isHead = parts.Length >= 3 && parts[^2] == "head";
        return parts.Length >= 3 && parts[^3] == "allocations" && (isHead || parts[^2] == "base");
    }

    private static bool TryLocate(
        string[] parts,
        [NotNullWhen(true)] out string? folder,
        out int number,
        out bool isHead,
        [NotNullWhen(true)] out string? runner)
    {
        folder = null;
        number = 0;
        isHead = false;
        runner = null;
        for (int i = parts.Length - 2; i >= 1; i--)
        {
            bool head = parts[i] == "head";
            if (!head && parts[i] != "base")
            {
                continue;
            }

            string name = parts[i - 1];
            if (name == "confirm")
            {
                (folder, number) = ("confirm", 1);
            }
            else if (TryNumber(name, "confirm-", out int rerun))
            {
                (folder, number) = ("confirm", rerun);
            }
            else if (TryNumber(name, "round-", out int round))
            {
                (folder, number) = ("round", round);
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

    private static bool TryNumber(string folder, string prefix, out int number)
    {
        number = 0;
        return folder.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(folder.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>The rounds and reruns of one <see cref="TimePass"/>, over every runner that measured it.</summary>
public sealed class TimingResults
{
    /// <summary>The interleaved rounds by round number.</summary>
    public SortedDictionary<int, GatePass> Rounds { get; } = [];

    /// <summary>The reruns of the cases the rounds flagged as slower, by number (<c>confirm</c> is 1, <c>confirm-2</c> is 2); empty when nothing was rerun.</summary>
    public SortedDictionary<int, GatePass> Confirmations { get; } = [];
}

/// <summary>One timing pass: the base commit's and the pull request's cases, keyed by full name.</summary>
public sealed class GatePass
{
    /// <summary>The base commit's cases.</summary>
    public Dictionary<string, BenchmarkCase> Base { get; } = new(StringComparer.Ordinal);

    /// <summary>The pull request's cases.</summary>
    public Dictionary<string, BenchmarkCase> Head { get; } = new(StringComparer.Ordinal);

    /// <summary>The results folder (the runner) each of the pull request's cases was read from, <c>.</c> for the root.</summary>
    public Dictionary<string, string> Runners { get; } = new(StringComparer.Ordinal);
}

/// <summary>The allocation measurements: bytes per call by full name, one value per measuring process.</summary>
public sealed class AllocationPass
{
    /// <summary>The base commit's figures.</summary>
    public Dictionary<string, List<long>> Base { get; } = new(StringComparer.Ordinal);

    /// <summary>The pull request's figures.</summary>
    public Dictionary<string, List<long>> Head { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The cases that opted out of the exact allocation comparison (<c>[BenchmarkCategory("no-allocation-gate")]</c>), from either
    /// side. They have no figure and no baseline entry, and their time is still compared.
    /// </summary>
    public HashSet<string> NotGated { get; } = new(StringComparer.Ordinal);
}
