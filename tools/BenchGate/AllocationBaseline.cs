using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broadside.Tools.BenchGate;

/// <summary>
/// One baseline entry: the bytes one call of a benchmark allocates. <see cref="Min"/> equals <see cref="Max"/> for every case whose
/// figure is the same in every process, which is the rule; a range is recorded only for a case measured to vary between processes
/// (<c>bench/baselines/gate/README.md</c>, "Cases that vary").
/// </summary>
/// <param name="Min">The smallest accepted figure.</param>
/// <param name="Max">The largest accepted figure.</param>
public readonly record struct AllocationRange(long Min, long Max)
{
    /// <summary>True for an exact figure.</summary>
    public bool IsExact => Min == Max;

    /// <summary>True when <paramref name="bytes"/> is accepted.</summary>
    public bool Contains(long bytes) => bytes >= Min && bytes <= Max;

    /// <summary>The figure, or <c>min..max</c>.</summary>
    public override string ToString() => IsExact
        ? FormattableString.Invariant($"{Min} B")
        : FormattableString.Invariant($"{Min}..{Max} B");
}

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

    private AllocationBaseline(SortedDictionary<string, AllocationRange> allocations) => Allocations = allocations;

    /// <summary>The accepted figure or range by full name.</summary>
    public IReadOnlyDictionary<string, AllocationRange> Allocations { get; }

    /// <summary>
    /// Reads a baseline file: <c>{ "Benchmarks": { "&lt;full name&gt;": &lt;bytes&gt; or { "Min": &lt;bytes&gt;, "Max": &lt;bytes&gt; }, ... } }</c>.
    /// </summary>
    public static AllocationBaseline Load(string path)
    {
        JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
        if (root?["Benchmarks"] is not JsonObject benchmarks)
        {
            throw new InvalidDataException($"{path}: expected an object with a \"Benchmarks\" object.");
        }

        var allocations = new SortedDictionary<string, AllocationRange>(StringComparer.Ordinal);
        foreach ((string name, JsonNode? value) in benchmarks)
        {
            allocations[name] = value switch
            {
                JsonValue number => new AllocationRange(number.GetValue<long>(), number.GetValue<long>()),
                JsonObject range when range["Min"] is JsonValue min && range["Max"] is JsonValue max && min.GetValue<long>() <= max.GetValue<long>() =>
                    new AllocationRange(min.GetValue<long>(), max.GetValue<long>()),
                _ => throw new InvalidDataException($"{path}: \"{name}\" must be a number or {{ \"Min\": <bytes>, \"Max\": <bytes> }}."),
            };
        }

        return new AllocationBaseline(allocations);
    }

    /// <summary>
    /// Builds a baseline from measured figures. A case measured the same everywhere gets that figure. A case whose figures differ,
    /// or that <paramref name="previous"/> records as a range, gets a range: the previous one when it holds every figure, otherwise
    /// the figures' span widened on each side by half the previous range's width (so a deliberate change moves the range without
    /// narrowing it).
    /// </summary>
    public static AllocationBaseline From(IReadOnlyDictionary<string, List<long>> measured, AllocationBaseline? previous = null)
    {
        var allocations = new SortedDictionary<string, AllocationRange>(StringComparer.Ordinal);
        foreach ((string name, List<long> values) in measured)
        {
            if (values.Count == 0)
            {
                continue;
            }

            long min = values.Min();
            long max = values.Max();
            if (previous is not null && previous.Allocations.TryGetValue(name, out AllocationRange before) && !before.IsExact)
            {
                if (values.All(before.Contains))
                {
                    allocations[name] = before;
                    continue;
                }

                long padding = (before.Max - before.Min) / 2;
                allocations[name] = new AllocationRange(Math.Max(0, min - padding), max + padding);
                continue;
            }

            allocations[name] = new AllocationRange(min, max);
        }

        return new AllocationBaseline(allocations);
    }

    /// <summary>Writes the file sorted by full name, two-space indented, with a final newline, so a re-record diffs line by line.</summary>
    public void Save(string path)
    {
        var benchmarks = new JsonObject();
        foreach ((string name, AllocationRange range) in Allocations)
        {
            benchmarks[name] = range.IsExact ? JsonValue.Create(range.Min) : new JsonObject { ["Min"] = range.Min, ["Max"] = range.Max };
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
