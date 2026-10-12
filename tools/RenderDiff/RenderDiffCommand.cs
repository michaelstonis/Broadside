using System.Globalization;
using Broadside.GoldenImages;

namespace Broadside.Tools.RenderDiff;

/// <summary>The command line: parses arguments into <see cref="RenderDiffOptions"/>, runs <see cref="RenderDiffRun"/> and maps the outcome to an exit code.</summary>
public static class RenderDiffCommand
{
    /// <summary>Exit code: the report was written.</summary>
    public const int Success = 0;

    /// <summary>Exit code: the arguments were wrong.</summary>
    public const int UsageError = 2;

    /// <summary>Exit code: no reference renderer is available, so nothing was compared.</summary>
    public const int NoReference = 3;

    private const string Usage = """
        Usage: dotnet run --project tools/RenderDiff -c Release -- [options] <pdf-or-directory>...

        Renders each page with Broadside and with reference renderers at the same DPI, aligns the images, ranks the pages by the
        golden-image comparison and writes index.html, report.json and triage.md. Development only; never a CI gate.

          --engines <list>        Reference engines: pdfium, pdfjs, poppler, comma-separated, or empty for none.
                                  Default: pdfium,pdfjs,poppler. Engines that are not installed are skipped.
          --reference <name>=<dir>  Also compare with renders made earlier: <dir>/<file stem>/page<N>.png. Repeatable.
          --reference-dir <dir>   Where acquire-references.sh installed PDFium and pdf.js.
                                  Default: $BROADSIDE_REFERENCE_DIR, else <repo>/artifacts/reference-renderers.
          --out <dir>             Report directory. Default: <repo>/artifacts/renderdiff.
          --dpi <dpi>             Resolution. Default: 72.
          --max-pages <n>         Pages per file, from the first. Default: 10.
          --max-shift <px>        Largest alignment offset tried. Default: 1.
          --edge <t>              Edge tolerance, 0 to 1. Default: 0.3 (the golden-image default).
          --flat <t>              Flat-area tolerance, 0 to 1. Default: 0.05.
          --report-pages <n>      Worst pages shown with images in index.html. Default: 50.
          --triage-pages <n>      Worst pages listed in triage.md. Default: 20.
          --timeout <seconds>     Per file and engine. Default: 120.
          --jobs <n>              Files processed at once. Default: half the processors.
          --reuse                 Reuse reference renders already in the report directory.
          --help                  Show this text.

        Install the reference renderers with tools/RenderDiff/acquire-references.sh (PDFium needs python3, pdf.js needs node);
        poppler is used when pdftoppm is on the PATH. Exit codes: 0 report written, 2 bad arguments, 3 no reference available.
        """;

    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="output">Receives progress.</param>
    /// <param name="error">Receives the usage text and errors.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        RenderDiffOptions? options;
        string? problem;
        try
        {
            (options, problem) = Parse(args);
        }
        catch (ArgumentException ex)
        {
            (options, problem) = (null, ex.Message);
        }

        if (options is null)
        {
            if (problem is not null)
            {
                await error.WriteLineAsync(problem).ConfigureAwait(false);
                await error.WriteLineAsync().ConfigureAwait(false);
                await error.WriteLineAsync(Usage).ConfigureAwait(false);
                return UsageError;
            }

            await output.WriteLineAsync(Usage).ConfigureAwait(false);
            return Success;
        }

        RenderDiffReport report;
        try
        {
            report = await RenderDiffRun.RunAsync(options, output, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return UsageError;
        }

        if (report.Engines.Count == 0)
        {
            await error.WriteLineAsync(
                "No reference renderer is available, so nothing was compared. Install PDFium and pdf.js with tools/RenderDiff/acquire-references.sh, "
                + "install poppler (pdftoppm), or pass --reference <name>=<dir>.").ConfigureAwait(false);
            return NoReference;
        }

        return Success;
    }

    /// <summary>Parses the arguments; returns null options and a problem for bad arguments, null options and no problem for <c>--help</c>.</summary>
    private static (RenderDiffOptions? Options, string? Problem) Parse(IReadOnlyList<string> args)
    {
        string? repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory()) ?? FindRepositoryRoot(AppContext.BaseDirectory);
        string defaultRoot = repositoryRoot ?? Directory.GetCurrentDirectory();
        string referenceDirectory = Environment.GetEnvironmentVariable("BROADSIDE_REFERENCE_DIR") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : Path.Combine(defaultRoot, "artifacts", "reference-renderers");
        string outputDirectory = Path.Combine(defaultRoot, "artifacts", "renderdiff");
        string scriptDirectory = Path.Combine(AppContext.BaseDirectory, "reference");
        List<string> engineNames = [.. ReferenceEngine.KnownNames];
        var directories = new List<(string Name, string Directory)>();
        var inputs = new List<string>();
        var options = new RenderDiffOptions { Inputs = inputs, OutputDirectory = outputDirectory };
        double edge = options.Threshold.EdgeTolerance;
        double flat = options.Threshold.FlatTolerance;

        for (int i = 0; i < args.Count; i++)
        {
            string argument = args[i];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                inputs.Add(argument);
                continue;
            }

            if (argument == "--help")
            {
                return (null, null);
            }

            if (argument == "--reuse")
            {
                options = options with { ReuseReferences = true };
                continue;
            }

            if (i + 1 >= args.Count)
            {
                return (null, $"{argument} needs a value.");
            }

            string value = args[++i];
            switch (argument)
            {
                case "--engines":
                    engineNames = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                    if (engineNames.Find(name => !ReferenceEngine.KnownNames.Contains(name)) is string unknown)
                    {
                        return (null, $"Unknown engine '{unknown}'; expected {string.Join(", ", ReferenceEngine.KnownNames)}.");
                    }

                    break;
                case "--reference":
                    int equals = value.IndexOf('=', StringComparison.Ordinal);
                    if (equals <= 0 || equals == value.Length - 1)
                    {
                        return (null, "--reference takes <name>=<directory>.");
                    }

                    directories.Add((value[..equals], value[(equals + 1)..]));
                    break;
                case "--reference-dir":
                    referenceDirectory = value;
                    break;
                case "--out":
                    options = options with { OutputDirectory = value };
                    break;
                case "--dpi":
                    options = options with { Dpi = Number(argument, value, 1, 2400) };
                    break;
                case "--max-pages":
                    options = options with { MaxPagesPerFile = (int)Number(argument, value, 1, int.MaxValue) };
                    break;
                case "--max-shift":
                    options = options with { MaxShift = (int)Number(argument, value, 0, 16) };
                    break;
                case "--edge":
                    edge = Number(argument, value, 0, 1);
                    break;
                case "--flat":
                    flat = Number(argument, value, 0, 1);
                    break;
                case "--report-pages":
                    options = options with { ReportedPages = (int)Number(argument, value, 0, int.MaxValue) };
                    break;
                case "--triage-pages":
                    options = options with { TriagePages = (int)Number(argument, value, 0, int.MaxValue) };
                    break;
                case "--timeout":
                    options = options with { Timeout = TimeSpan.FromSeconds(Number(argument, value, 1, 86_400)) };
                    break;
                case "--jobs":
                    options = options with { Jobs = (int)Number(argument, value, 1, 1024) };
                    break;
                default:
                    return (null, $"Unknown option {argument}.");
            }
        }

        if (inputs.Count == 0)
        {
            return (null, "Name at least one PDF file or directory.");
        }

        List<ReferenceEngine> engines =
        [
            .. engineNames.Distinct(StringComparer.Ordinal).Select(name => ReferenceEngine.Create(name, referenceDirectory, scriptDirectory)),
            .. directories.Select(static d => ReferenceEngine.FromDirectory(d.Name, d.Directory)),
        ];
        if (engines.Select(static e => e.Name).Distinct(StringComparer.Ordinal).Count() != engines.Count)
        {
            return (null, "Two engines have the same name.");
        }

        return (options with { Engines = engines, Threshold = new GoldenThreshold(edge, flat, 0) }, null);
    }

    private static double Number(string option, string value, double minimum, double maximum) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && number >= minimum && number <= maximum
            ? number
            : throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"{option} takes a number from {minimum} to {maximum}, not '{value}'."));

    private static string? FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
