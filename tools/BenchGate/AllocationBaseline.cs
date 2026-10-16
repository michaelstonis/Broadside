using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broadside.Tools.BenchGate;

/// <summary>
/// The stored allocation baseline (<c>bench/baselines/gate/allocations.json</c>): every gated case's full name and the bytes one
/// call allocates, measured on the CI runner by <c>Broadside.Benchmarks --gate-allocations</c>. The gate requires an exact match;
/// the file changes only through <see cref="BenchmarkGate.Record"/> (or by hand from the gate's summary), in the pull request that
/// changes the allocation, so the change is reviewed.
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

    /// <summary>The bytes per call by full name. The gate accepts exactly this figure and no other.</summary>
    public IReadOnlyDictionary<string, long> Allocations { get; }

    /// <summary>
    /// Reads a baseline file: <c>{ "Benchmarks": { "&lt;full name&gt;": &lt;bytes&gt;, ... } }</c>.
    /// </summary>
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
            allocations[name] = value is JsonValue number && number.TryGetValue(out long bytes) && bytes >= 0
                ? bytes
                : throw new InvalidDataException($"{path}: \"{name}\" must be a whole number of bytes (ranges are not accepted: allocations are deterministic since #288).");
        }

        return new AllocationBaseline(allocations);
    }

    /// <summary>
    /// Builds a baseline from measured figures. A case measured more than once with different figures gets the largest; the gate
    /// still fails it (<see cref="FindingKind.AllocationChanged"/>), because a case whose allocations vary between processes is a
    /// determinism bug to fix in the library, not to accept in the baseline.
    /// </summary>
    public static AllocationBaseline From(IReadOnlyDictionary<string, List<long>> measured)
    {
        var allocations = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach ((string name, List<long> values) in measured)
        {
            if (values.Count > 0)
            {
                allocations[name] = values.Max();
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
