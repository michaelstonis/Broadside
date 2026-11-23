using System.Text.Json;

namespace Broadside.Tools.PerfCompare;

/// <summary>One timed case: an engine's median and minimum for one operation on one page.</summary>
internal sealed record Timing(string File, int Page, string Op, int? Dpi, double? MedianMs, double? MinMs, int Runs, long? AllocBytes, string? Error)
{
    public string Key => $"{File}#{Page}|{Op}|{Dpi}";
}

/// <summary>The peak resident set size of one engine processing one file in a fresh process (no file: the engine loaded and idle).</summary>
internal sealed record MemorySample(string Engine, string? File, long PeakRssBytes);

/// <summary>
/// One run of <c>bench/perf-report.sh</c> on one machine: a folder with <c>environment.json</c>, <c>broadside.jsonl</c>,
/// <c>pdfium.jsonl</c>, <c>pdfjs.jsonl</c>, <c>memory.jsonl</c> and, when the browser comparison ran, <c>browser.jsonl</c>.
/// </summary>
internal sealed class ResultSet
{
    public required string Directory { get; init; }

    public required IReadOnlyList<KeyValuePair<string, string>> Environment { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyList<Timing>> Engines { get; init; }

    public required IReadOnlyList<MemorySample> Memory { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyList<Timing>> Browser { get; init; }

    public string Label => Environment.FirstOrDefault(static pair => pair.Key == "label").Value ?? Path.GetFileName(Directory);

    public static ResultSet Load(string directory)
    {
        var environment = new List<KeyValuePair<string, string>>();
        string environmentPath = Path.Combine(directory, "environment.json");
        if (File.Exists(environmentPath))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(environmentPath));
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                environment.Add(new(property.Name, property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText()));
            }
        }

        var engines = new Dictionary<string, IReadOnlyList<Timing>>(StringComparer.Ordinal);
        foreach (string engine in new[] { "broadside", "pdfium", "pdfjs" })
        {
            engines[engine] = ReadTimings(Path.Combine(directory, engine + ".jsonl"), null);
        }

        var browser = new Dictionary<string, IReadOnlyList<Timing>>(StringComparer.Ordinal);
        foreach (string engine in new[] { "broadside", "pdfjs" })
        {
            browser[engine] = ReadTimings(Path.Combine(directory, "browser.jsonl"), engine);
        }

        var memory = new List<MemorySample>();
        string memoryPath = Path.Combine(directory, "memory.jsonl");
        if (File.Exists(memoryPath))
        {
            foreach (string line in File.ReadLines(memoryPath).Where(static l => l.Length > 0))
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("peak_rss_bytes", out JsonElement peak) && peak.ValueKind == JsonValueKind.Number)
                {
                    memory.Add(new MemorySample(
                        root.GetProperty("engine").GetString()!,
                        root.TryGetProperty("file", out JsonElement file) && file.ValueKind == JsonValueKind.String ? file.GetString() : null,
                        peak.GetInt64()));
                }
            }
        }

        return new ResultSet { Directory = directory, Environment = environment, Engines = engines, Memory = memory, Browser = browser };
    }

    private static List<Timing> ReadTimings(string path, string? engine)
    {
        var timings = new List<Timing>();
        if (!File.Exists(path))
        {
            return timings;
        }

        foreach (string line in File.ReadLines(path).Where(static l => l.Length > 0 && l[0] == '{'))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (engine is not null && (!root.TryGetProperty("engine", out JsonElement name) || name.GetString() != engine))
            {
                continue;
            }

            timings.Add(new Timing(
                root.GetProperty("file").GetString()!,
                root.GetProperty("page").GetInt32(),
                root.GetProperty("op").GetString()!,
                Number(root, "dpi") is { } dpi ? (int)dpi : null,
                Number(root, "median_ms"),
                Number(root, "min_ms"),
                Number(root, "runs") is { } runs ? (int)runs : 0,
                Number(root, "alloc_bytes") is { } bytes ? (long)bytes : null,
                root.TryGetProperty("error", out JsonElement error) ? error.GetString() : null));
        }

        return timings;
    }

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
