using System.Globalization;

namespace Broadside.Tools.BenchGate;

/// <summary>
/// Entry point. See README.md.
/// <code>
/// BenchGate compare  --baseline &lt;file&gt; --results &lt;dir&gt; [--summary &lt;file&gt;] [--record &lt;file&gt;] [--base-name &lt;text&gt;] [--fail-ratio &lt;x&gt;] [--subset true]
/// BenchGate suspects --results &lt;dir&gt; --out &lt;file&gt; [--fail-ratio &lt;x&gt;]
/// BenchGate record   --results &lt;dir&gt; --out &lt;file&gt;
/// BenchGate shard    --list &lt;file&gt; --count &lt;n&gt; --index &lt;i&gt; --out &lt;file&gt; [--baseline &lt;file&gt;] [--split &lt;method,...&gt;]
/// </code>
/// Exit codes: 0 passed, 1 a regression (compare only), 2 a bad command line or unreadable input.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage(args.Length == 0 ? Console.Error : Console.Out);
            return args.Length == 0 ? 2 : 0;
        }

        Dictionary<string, string>? options = ParseOptions(args.AsSpan(1));
        if (options is null)
        {
            PrintUsage(Console.Error);
            return 2;
        }

        try
        {
            return args[0] switch
            {
                "compare" => Compare(options),
                "suspects" => Suspects(options),
                "record" => Record(options),
                "shard" => Shard(options),
                _ => Usage($"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or FormatException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static int Compare(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("--baseline", out string? baseline) || !options.TryGetValue("--results", out string? results))
        {
            return Usage("compare needs --baseline and --results.");
        }

        GateReport report = BenchmarkGate.Run(baseline, results, ReadOptions(options));
        string markdown = report.ToMarkdown(options.GetValueOrDefault("--base-name"));
        if (options.TryGetValue("--summary", out string? summary))
        {
            File.AppendAllText(summary, markdown + Environment.NewLine);
        }

        if (options.TryGetValue("--record", out string? record))
        {
            BenchmarkGate.Record(results, record);
        }

        foreach (GateFinding finding in report.Failures)
        {
            // A GitHub annotation per regression, so it shows on the run page as well as in the summary.
            Console.Out.WriteLine($"::error title=Benchmark regression::{finding.Benchmark}: {finding.Message}");
        }

        Console.Out.WriteLine(markdown);
        return report.Passed ? 0 : 1;
    }

    private static int Suspects(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("--results", out string? results) || !options.TryGetValue("--out", out string? output))
        {
            return Usage("suspects needs --results and --out.");
        }

        IReadOnlyList<string> filters = BenchmarkGate.Suspects(results, ReadOptions(options));
        File.WriteAllLines(output, filters);
        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{filters.Count} benchmark case(s) to confirm."));
        foreach (string filter in filters)
        {
            Console.Out.WriteLine(filter);
        }

        return 0;
    }

    private static int Record(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("--results", out string? results) || !options.TryGetValue("--out", out string? output))
        {
            return Usage("record needs --results and --out.");
        }

        AllocationBaseline baseline = BenchmarkGate.Record(results, output);
        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Recorded {baseline.Allocations.Count} benchmark case(s) to {output}."));
        return 0;
    }

    private static int Shard(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("--list", out string? list)
            || !options.TryGetValue("--count", out string? count)
            || !options.TryGetValue("--index", out string? index)
            || !options.TryGetValue("--out", out string? output))
        {
            return Usage("shard needs --list, --count, --index and --out.");
        }

        AllocationBaseline baseline = options.TryGetValue("--baseline", out string? path) && File.Exists(path)
            ? AllocationBaseline.Load(path)
            : AllocationBaseline.From(new Dictionary<string, List<long>>());
        IReadOnlyList<string> methods = BenchmarkGate.Shard(
            File.ReadAllLines(list),
            baseline,
            int.Parse(count, NumberStyles.None, CultureInfo.InvariantCulture),
            int.Parse(index, NumberStyles.None, CultureInfo.InvariantCulture),
            options.TryGetValue("--split", out string? split) ? split.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : []);
        File.WriteAllLines(output, methods);
        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Shard {index} of {count}: {methods.Count} benchmark method(s)."));
        return 0;
    }

    private static GateOptions ReadOptions(Dictionary<string, string> options) => GateOptions.Default with
    {
        FailRatio = options.TryGetValue("--fail-ratio", out string? ratio)
            ? double.Parse(ratio, NumberStyles.Float, CultureInfo.InvariantCulture)
            : GateOptions.Default.FailRatio,
        Subset = options.TryGetValue("--subset", out string? subset) && bool.Parse(subset),
    };

    private static Dictionary<string, string>? ParseOptions(ReadOnlySpan<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Expected '--option value' at '{args[i]}'.");
                return null;
            }

            options[args[i]] = args[i + 1];
        }

        return options;
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage(Console.Error);
        return 2;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  BenchGate compare  --baseline <file> --results <dir> [--summary <file>] [--record <file>] [--base-name <text>] [--fail-ratio <x>] [--subset true]");
        writer.WriteLine("  BenchGate suspects --results <dir> --out <file> [--fail-ratio <x>]");
        writer.WriteLine("  BenchGate record   --results <dir> --out <file>");
        writer.WriteLine("  BenchGate shard    --list <file> --count <n> --index <i> --out <file> [--baseline <file>] [--split <method,...>]");
        writer.WriteLine("Results layout: <dir>/[<shard>/]{round-<n>,confirm,allocations}/{base,head}/**/*.json.");
    }
}
