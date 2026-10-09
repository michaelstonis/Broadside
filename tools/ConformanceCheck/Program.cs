using System.Globalization;

namespace Broadside.Tools.ConformanceCheck;

/// <summary>Entry point: <c>dotnet run --project tools/ConformanceCheck -- [--root &lt;dir&gt;] [--summary] [--strict]</c>.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Options? options = Options.Parse(args, Console.Error);
        if (options is null)
        {
            Options.PrintUsage(Console.Error);
            return 2;
        }

        if (options.ShowHelp)
        {
            Options.PrintUsage(Console.Out);
            return 0;
        }

        string? root = options.Root is null
            ? RepositoryLocator.FindRoot(Directory.GetCurrentDirectory()) ?? RepositoryLocator.FindRoot(AppContext.BaseDirectory)
            : Path.GetFullPath(options.Root);
        if (root is null)
        {
            Console.Error.WriteLine($"Could not find {RepositoryLocator.Marker} above the current directory; pass --root <repo root>.");
            return 2;
        }

        ConformanceReport report;
        try
        {
            report = ConformanceChecker.Check(root, options.Strict);
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        foreach (Violation violation in report.Violations)
        {
            Console.Out.WriteLine(violation.ToString());
        }

        if (options.Summary)
        {
            if (!report.IsValid)
            {
                Console.Out.WriteLine();
            }

            Console.Out.WriteLine(report.ToMarkdownSummary());
            Console.Out.WriteLine();
        }

        Console.Out.WriteLine(report.IsValid
            ? string.Create(CultureInfo.InvariantCulture, $"Conformance map OK: {report.Files.Count} files, {report.RowCount} rows, 0 violations.")
            : string.Create(CultureInfo.InvariantCulture, $"Conformance map FAILED: {report.Violations.Count} violation(s) in {report.Files.Count} files, {report.RowCount} rows."));
        return report.IsValid ? 0 : 1;
    }
}

/// <summary>Parsed command line.</summary>
internal sealed class Options
{
    public string? Root { get; private set; }

    public bool Summary { get; private set; }

    public bool Strict { get; private set; }

    public bool ShowHelp { get; private set; }

    public static Options? Parse(string[] args, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(errors);

        var options = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--root":
                    if (i + 1 >= args.Length)
                    {
                        errors.WriteLine("--root needs a path.");
                        return null;
                    }

                    options.Root = args[++i];
                    break;
                case "--summary":
                    options.Summary = true;
                    break;
                case "--strict":
                    options.Strict = true;
                    break;
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                default:
                    if (arg.StartsWith("--root=", StringComparison.Ordinal))
                    {
                        options.Root = arg["--root=".Length..];
                        break;
                    }

                    errors.WriteLine($"Unknown argument: {arg}");
                    return null;
            }
        }

        return options;
    }

    public static void PrintUsage(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine("Usage: dotnet run --project tools/ConformanceCheck -- [options]");
        output.WriteLine();
        output.WriteLine("Checks every docs/conformance/*.md (except README.md) against the rules in docs/conformance/README.md:");
        output.WriteLine("six cells per row, a known status, tests on done rows, notes on n/a rows, no duplicate clauses, and every");
        output.WriteLine("cited test and implementing type present in tests/ and src/. Prints one line per violation as");
        output.WriteLine("docs/conformance/<file>.md:<line>: <message>.");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --root <dir>   Repository root (default: the nearest parent of the current directory holding Broadside.slnx).");
        output.WriteLine("  --summary      Also print a Markdown table of row counts per status for every file, with a total.");
        output.WriteLine("  --strict       Also fail partial rows that have an empty Tests or Notes cell.");
        output.WriteLine("  -h, --help     Show this help.");
        output.WriteLine();
        output.WriteLine("Exit code: 0 when the map is valid, 1 when any rule fails, 2 for a usage or environment error.");
    }
}
