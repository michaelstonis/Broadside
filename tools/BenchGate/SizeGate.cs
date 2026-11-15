using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broadside.Tools.BenchGate;

/// <summary>
/// The Native AOT size probes' gate (issue #361): compares the sizes <c>bench/size-probes.sh</c> measured with
/// <c>bench/baselines/gate/sizes.json</c>, per runtime identifier, the way the allocation gate compares allocations.
/// </summary>
/// <remarks>
/// A probe fails when it grows by more than the tolerance, when it is new (no baseline for it) or when a baselined probe of the
/// measured runtime is missing. A probe that shrinks by more than the tolerance passes with a notice: re-record the baseline so the
/// saving is kept. The tolerance absorbs the few bytes a linker or runtime patch moves; a feature's real growth is far larger.
/// </remarks>
public static class SizeGate
{
    /// <summary>The default tolerance: a probe may differ from its baseline by this fraction before the gate reacts.</summary>
    public const double DefaultTolerance = 0.01;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Reads a sizes file: <c>{ "Probes": { "&lt;rid&gt;": { "&lt;probe&gt;": &lt;bytes&gt;, ... } } }</c>.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Bytes by probe, by runtime identifier.</returns>
    public static SortedDictionary<string, SortedDictionary<string, long>> Load(string path)
    {
        JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
        if (root?["Probes"] is not JsonObject runtimes)
        {
            throw new InvalidDataException($"{path}: expected an object with a \"Probes\" object.");
        }

        var sizes = new SortedDictionary<string, SortedDictionary<string, long>>(StringComparer.Ordinal);
        foreach ((string rid, JsonNode? probes) in runtimes)
        {
            if (probes is not JsonObject entries)
            {
                throw new InvalidDataException($"{path}: \"{rid}\" must be an object of probe sizes.");
            }

            var byProbe = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach ((string probe, JsonNode? value) in entries)
            {
                byProbe[probe] = value is JsonValue number && number.TryGetValue(out long bytes) && bytes > 0
                    ? bytes
                    : throw new InvalidDataException($"{path}: \"{rid}\".\"{probe}\" must be a positive whole number of bytes.");
            }

            sizes[rid] = byProbe;
        }

        return sizes;
    }

    /// <summary>Writes a sizes file sorted by runtime and probe, two-space indented, with a final newline.</summary>
    /// <param name="sizes">The sizes.</param>
    /// <param name="path">The file.</param>
    public static void Save(IReadOnlyDictionary<string, SortedDictionary<string, long>> sizes, string path)
    {
        var runtimes = new JsonObject();
        foreach ((string rid, SortedDictionary<string, long> probes) in sizes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var entries = new JsonObject();
            foreach ((string probe, long bytes) in probes)
            {
                entries[probe] = bytes;
            }

            runtimes[rid] = entries;
        }

        string json = new JsonObject { ["Probes"] = runtimes }.ToJsonString(Options).ReplaceLineEndings("\n") + "\n";
        File.WriteAllText(path, json);
    }

    /// <summary>Compares measured sizes with the baseline.</summary>
    /// <param name="baselinePath">The baseline file.</param>
    /// <param name="resultsPath">The measured sizes, in the same format.</param>
    /// <param name="tolerance">How far, as a fraction of the baseline, a probe may move before it fails (growth) or is noted (shrinkage).</param>
    /// <returns>The report.</returns>
    public static SizeReport Run(string baselinePath, string resultsPath, double tolerance = DefaultTolerance)
    {
        SortedDictionary<string, SortedDictionary<string, long>> baseline = Load(baselinePath);
        SortedDictionary<string, SortedDictionary<string, long>> results = Load(resultsPath);
        var rows = new List<SizeRow>();
        foreach ((string rid, SortedDictionary<string, long> measured) in results)
        {
            SortedDictionary<string, long>? expected = baseline.GetValueOrDefault(rid);
            foreach ((string probe, long bytes) in measured)
            {
                if (expected is null || !expected.TryGetValue(probe, out long recorded))
                {
                    rows.Add(new SizeRow(rid, probe, null, bytes, SizeVerdict.New));
                    continue;
                }

                double change = (double)(bytes - recorded) / recorded;
                SizeVerdict verdict = change > tolerance ? SizeVerdict.Grew : change < -tolerance ? SizeVerdict.Shrank : SizeVerdict.Unchanged;
                rows.Add(new SizeRow(rid, probe, recorded, bytes, verdict));
            }

            if (expected is not null)
            {
                foreach ((string probe, long recorded) in expected)
                {
                    if (!measured.ContainsKey(probe))
                    {
                        rows.Add(new SizeRow(rid, probe, recorded, null, SizeVerdict.Missing));
                    }
                }
            }
        }

        return new SizeReport(rows, tolerance);
    }

    /// <summary>Writes the baseline with the measured runtimes' sizes replaced by the measured ones; other runtimes are kept.</summary>
    /// <param name="baselinePath">The baseline file; created when absent.</param>
    /// <param name="resultsPath">The measured sizes.</param>
    public static void Record(string baselinePath, string resultsPath)
    {
        SortedDictionary<string, SortedDictionary<string, long>> baseline = File.Exists(baselinePath)
            ? Load(baselinePath)
            : new(StringComparer.Ordinal);
        foreach ((string rid, SortedDictionary<string, long> measured) in Load(resultsPath))
        {
            baseline[rid] = measured;
        }

        Save(baseline, baselinePath);
    }
}

/// <summary>What the gate concluded about one probe.</summary>
public enum SizeVerdict
{
    /// <summary>Within the tolerance of its baseline.</summary>
    Unchanged,

    /// <summary>Larger than its baseline by more than the tolerance: a failure.</summary>
    Grew,

    /// <summary>Smaller than its baseline by more than the tolerance: passes, with a request to re-record.</summary>
    Shrank,

    /// <summary>Measured but not in the baseline: a failure until it is recorded.</summary>
    New,

    /// <summary>In the baseline of a measured runtime but not measured: a failure.</summary>
    Missing,
}

/// <summary>One probe's comparison.</summary>
/// <param name="Rid">The runtime identifier.</param>
/// <param name="Probe">The probe's name.</param>
/// <param name="Baseline">The recorded bytes, or <see langword="null"/> for a new probe.</param>
/// <param name="Measured">The measured bytes, or <see langword="null"/> for a missing probe.</param>
/// <param name="Verdict">The verdict.</param>
public sealed record SizeRow(string Rid, string Probe, long? Baseline, long? Measured, SizeVerdict Verdict)
{
    /// <summary>Gets whether this row fails the gate.</summary>
    public bool Fails => Verdict is SizeVerdict.Grew or SizeVerdict.New or SizeVerdict.Missing;
}

/// <summary>The gate's conclusion over every probe.</summary>
/// <param name="Rows">One row per probe, in runtime and probe order.</param>
/// <param name="Tolerance">The tolerance used.</param>
public sealed record SizeReport(IReadOnlyList<SizeRow> Rows, double Tolerance)
{
    /// <summary>Gets whether no probe failed.</summary>
    public bool Passed => !Rows.Any(row => row.Fails);

    /// <summary>Gets the failing rows.</summary>
    public IEnumerable<SizeRow> Failures => Rows.Where(row => row.Fails);

    /// <summary>Formats the report as a Markdown table for the job summary.</summary>
    /// <returns>The Markdown.</returns>
    public string ToMarkdown()
    {
        var text = new StringBuilder();
        CultureInfo culture = CultureInfo.InvariantCulture;
        text.AppendLine(Passed ? "### Size probes: passed" : "### Size probes: failed");
        text.AppendLine();
        text.AppendLine("| Runtime | Probe | Baseline | Measured | Change | Verdict |");
        text.AppendLine("|---|---|---:|---:|---:|---|");
        foreach (SizeRow row in Rows)
        {
            string change = row is { Baseline: { } before, Measured: { } after }
                ? string.Create(culture, $"{after - before:+#,0;-#,0;0} B ({(double)(after - before) / before:+0.00%;-0.00%;0.00%})")
                : string.Empty;
            text.AppendLine(culture, $"| {row.Rid} | {row.Probe} | {Bytes(row.Baseline)} | {Bytes(row.Measured)} | {change} | {Describe(row.Verdict)} |");
        }

        text.AppendLine();
        text.AppendLine(culture, $"Tolerance: {Tolerance:0.##%} of the baseline. Record intended changes with `BenchGate sizes --record bench/baselines/gate/sizes.json` (bench/baselines/gate/README.md, \"Size probes\").");
        return text.ToString();
    }

    private static string Bytes(long? bytes) => bytes is { } value ? value.ToString("#,0", CultureInfo.InvariantCulture) : "-";

    private static string Describe(SizeVerdict verdict) => verdict switch
    {
        SizeVerdict.Unchanged => "ok",
        SizeVerdict.Grew => "**grew**",
        SizeVerdict.Shrank => "shrank: re-record the baseline",
        SizeVerdict.New => "**new: not in the baseline**",
        _ => "**missing: not measured**",
    };
}
