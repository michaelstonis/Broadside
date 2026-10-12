using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broadside.Tools.BenchGate;

/// <summary>
/// The stored allocation baseline (<c>bench/baselines/gate/allocations.json</c>): every gated benchmark's full name and its
/// bytes allocated per operation, recorded on the CI runner. The gate requires an exact match; the file changes only through
/// <see cref="BenchmarkGate.Record"/>, in the pull request that changes the allocation, so the change is reviewed.
/// </summary>
public sealed class AllocationBaseline
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private AllocationBaseline(SortedDictionary<string, long> allocations) => Allocations = allocations;

    /// <summary>Bytes allocated per operation by full name.</summary>
    public IReadOnlyDictionary<string, long> Allocations { get; }

    /// <summary>Reads a baseline file: <c>{ "Benchmarks": { "&lt;full name&gt;": &lt;bytes&gt;, ... } }</c>.</summary>
    public static AllocationBaseline Load(string path)
    {
        JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
        if (root?["Benchmarks"] is not JsonObject benchmarks)
        {
            throw new InvalidDataException($"{path}: expected an object with a \"Benchmarks\" object.");
        }

        var allocations = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach ((string name, JsonNode? value) in benchmarks)
        {
            allocations[name] = value?.GetValue<long>() ?? throw new InvalidDataException($"{path}: \"{name}\" has no value.");
        }

        return new AllocationBaseline(allocations);
    }

    /// <summary>Builds a baseline from measured cases; a case measured more than once keeps its largest figure.</summary>
    public static AllocationBaseline From(IEnumerable<BenchmarkCase> cases)
    {
        var allocations = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (BenchmarkCase benchmark in cases)
        {
            if (benchmark.AllocatedBytesPerOperation is long bytes)
            {
                allocations[benchmark.FullName] = allocations.TryGetValue(benchmark.FullName, out long existing) ? Math.Max(existing, bytes) : bytes;
            }
        }

        return new AllocationBaseline(allocations);
    }

    /// <summary>Writes the file sorted by full name, two-space indented, with a final newline, so a re-record diffs line by line.</summary>
    public void Save(string path)
    {
        var benchmarks = new JsonObject();
        foreach ((string name, long bytes) in Allocations)
        {
            benchmarks[name] = bytes;
        }

        string json = new JsonObject { ["Benchmarks"] = benchmarks }.ToJsonString(Options).ReplaceLineEndings("\n") + "\n";
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json);
    }
}
