using System.Globalization;
using System.Text.Json.Nodes;

namespace Broadside.Tools.BenchGate.Tests;

/// <summary>
/// A results directory in the layout the CI job produces (<c>round-&lt;n&gt;/base</c>, <c>round-&lt;n&gt;/head</c>,
/// <c>confirm/base</c>, <c>confirm/head</c>), filled with synthetic BenchmarkDotNet full JSON reports.
/// </summary>
internal sealed class ResultsTree : IDisposable
{
    public const string Lexer = "Broadside.Benchmarks.LexerBenchmarks.Tokenize(File: \"empty-page.pdf\")";

    public const string Flate = "Broadside.Benchmarks.FlateBenchmarks.Decode";

    public ResultsTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "benchgate-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Writes one benchmark of one side of one round: <paramref name="nanoseconds"/> are the measured iterations, per operation.</summary>
    public ResultsTree Add(string folder, string fullName, long allocated, params double[] nanoseconds)
    {
        string directory = Path.Combine(Root, folder);
        Directory.CreateDirectory(directory);
        string type = TypeOf(fullName);
        string path = Path.Combine(directory, $"Broadside.Benchmarks.{type}-report-full-compressed.json");

        JsonObject report = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path))!.AsObject()
            : new JsonObject { ["Title"] = type, ["Benchmarks"] = new JsonArray() };

        const long operations = 1000;
        var measurements = new JsonArray
        {
            // Stages the gate must ignore: only Workload/Result iterations are the measured ones.
            Measurement("Overhead", "Actual", 1, operations, 5.0 * operations),
            Measurement("Workload", "Warmup", 1, operations, nanoseconds[0] * 10 * operations),
        };
        for (int i = 0; i < nanoseconds.Length; i++)
        {
            measurements.Add(Measurement("Workload", "Result", i + 1, operations, nanoseconds[i] * operations));
        }

        int parenthesis = fullName.IndexOf('(', StringComparison.Ordinal);
        string withoutParameters = parenthesis < 0 ? fullName : fullName[..parenthesis];
        report["Benchmarks"]!.AsArray().Add(new JsonObject
        {
            ["Namespace"] = "Broadside.Benchmarks",
            ["Type"] = type,
            ["Method"] = withoutParameters[(withoutParameters.LastIndexOf('.') + 1)..],
            ["FullName"] = fullName,
            ["Memory"] = new JsonObject
            {
                ["Gen0Collections"] = 0,
                ["TotalOperations"] = operations,
                ["BytesAllocatedPerOperation"] = allocated,
            },
            ["Measurements"] = measurements,
        });

        File.WriteAllText(path, report.ToJsonString());

        // The allocation figure goes where `--gate-allocations` writes it: <runner>/allocations/<side>/<file>.json, one file per
        // measuring process (here, per round). The confirmation pass measures time only.
        string[] parts = folder.Split('/');
        if (parts[^2] != "confirm")
        {
            string runner = string.Join('/', parts[..^2]);
            AddAllocation(Path.Combine(runner, "allocations", parts[^1]), parts[^2], fullName, allocated);
        }

        return this;
    }

    /// <summary>Writes one allocation figure into <c>&lt;directory&gt;/&lt;file&gt;.json</c>.</summary>
    public ResultsTree AddAllocation(string directory, string file, string fullName, long bytes)
    {
        string folder = Path.Combine(Root, directory);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, file + ".json");
        JsonObject document = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path))!.AsObject()
            : new JsonObject { ["Unit"] = "bytes per call", ["Benchmarks"] = new JsonObject() };
        document["Benchmarks"]![fullName] = bytes;
        File.WriteAllText(path, document.ToJsonString());
        return this;
    }

    /// <summary>Writes the same benchmark to <c>round-1</c> .. <c>round-n</c> of one side.</summary>
    public ResultsTree AddRounds(string side, int rounds, string fullName, long allocated, params double[] nanoseconds)
    {
        for (int round = 1; round <= rounds; round++)
        {
            Add(string.Create(CultureInfo.InvariantCulture, $"round-{round}/{side}"), fullName, allocated, nanoseconds);
        }

        return this;
    }

    public string WriteBaseline(params (string FullName, long Allocated)[] entries)
    {
        var benchmarks = new JsonObject();
        foreach ((string fullName, long allocated) in entries)
        {
            benchmarks[fullName] = allocated;
        }

        string path = Path.Combine(Root, "baseline.json");
        File.WriteAllText(path, new JsonObject { ["Benchmarks"] = benchmarks }.ToJsonString());
        return path;
    }

    /// <summary>Writes a baseline file with the given text.</summary>
    public string WriteBaselineText(string json)
    {
        string path = Path.Combine(Root, "baseline.json");
        File.WriteAllText(path, json);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Iterations around <paramref name="center"/> with about 2 % spread, the shape of a quiet run.</summary>
    public static double[] Around(double center) =>
        [center * 0.99, center * 1.01, center, center * 0.98, center * 1.02, center * 1.005, center * 0.995, center, center * 1.01, center * 0.99];

    private static string TypeOf(string fullName)
    {
        int parenthesis = fullName.IndexOf('(', StringComparison.Ordinal);
        string withoutParameters = parenthesis < 0 ? fullName : fullName[..parenthesis];
        string[] parts = withoutParameters.Split('.');
        return parts[^2];
    }

    private static JsonObject Measurement(string mode, string stage, int index, long operations, double nanoseconds) => new()
    {
        ["IterationMode"] = mode,
        ["IterationStage"] = stage,
        ["LaunchIndex"] = 1,
        ["IterationIndex"] = index,
        ["Operations"] = operations,
        ["Nanoseconds"] = nanoseconds,
    };
}
