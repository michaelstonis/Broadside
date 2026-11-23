using System.Globalization;

namespace Broadside.Tools.PerfCompare;

/// <summary>
/// <c>PerfCompare bench  --pages &lt;file&gt; --corpus &lt;dir&gt; [--dpis 72,150] [--document-dpi 150] [--iterations 10] [--out &lt;jsonl&gt;]</c><br/>
/// <c>PerfCompare memory [--file &lt;pdf&gt;] [--dpi 150]</c><br/>
/// <c>PerfCompare report --results &lt;dir&gt; [--results &lt;dir&gt;]... --out &lt;md&gt;</c>
/// </summary>
internal static class Program
{
    /// <summary>Gets the timed iterations per case.</summary>
    public static int Iterations { get; private set; } = 10;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return Usage();
        }

        var options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                return Usage();
            }

            if (!options.TryGetValue(args[i], out List<string>? values))
            {
                options[args[i]] = values = [];
            }

            values.Add(args[++i]);
        }

        string? One(string name) => options.TryGetValue(name, out List<string>? values) ? values[^1] : null;
        int Int(string name, int fallback) => One(name) is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

        switch (args[0])
        {
            case "bench":
                if (One("--pages") is not { } pages || One("--corpus") is not { } corpus)
                {
                    return Usage();
                }

                Iterations = Int("--iterations", 10);
                int[] dpis = [.. (One("--dpis") ?? "72,150").Split(',').Select(static d => int.Parse(d, CultureInfo.InvariantCulture))];
                using (TextWriter output = One("--out") is { } path ? new StreamWriter(path) : Console.Out)
                {
                    Harness.Bench(pages, corpus, dpis, Int("--document-dpi", 150), Iterations, output);
                }

                return 0;

            case "memory":
                Harness.Memory(One("--file"), Int("--dpi", 150), Console.Out);
                return 0;

            case "report":
                if (!options.TryGetValue("--results", out List<string>? results) || One("--out") is not { } report)
                {
                    return Usage();
                }

                File.WriteAllText(report, Report.Write([.. results.Select(ResultSet.Load)]));
                Console.WriteLine($"wrote {report}");
                return 0;

            default:
                return Usage();
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: PerfCompare bench --pages <file> --corpus <dir> [--dpis 72,150] [--document-dpi 150] [--iterations 10] [--out <jsonl>]");
        Console.Error.WriteLine("       PerfCompare memory [--file <pdf>] [--dpi 150]");
        Console.Error.WriteLine("       PerfCompare report --results <dir> [--results <dir>]... --out <md>");
        return 2;
    }
}

/// <summary>Reads <c>tests/Broadside.Rendering.Tests/CorpusGate/pinned-pages.txt</c>: path, tab, zero-based pages, tab, reason.</summary>
internal static class PinnedPages
{
    public static IEnumerable<(string File, int Page)> Read(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split('\t');
            foreach (string page in fields[1].Split(','))
            {
                yield return (fields[0], int.Parse(page, NumberStyles.None, CultureInfo.InvariantCulture));
            }
        }
    }
}
