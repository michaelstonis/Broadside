using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Broadside.Tools.RenderDiff;

/// <summary>One file's pages for a reference renderer to draw.</summary>
/// <param name="PdfPath">The PDF file.</param>
/// <param name="RelativeStem">The file's path relative to its input, without <c>.pdf</c>, with forward slashes.</param>
/// <param name="Pages">The zero-based pages.</param>
/// <param name="Dpi">The resolution.</param>
/// <param name="OutputDirectory">Where a renderer that draws writes <c>page&lt;N&gt;.png</c>.</param>
/// <param name="Timeout">How long the renderer may take for the whole file.</param>
public sealed record ReferenceRequest(string PdfPath, string RelativeStem, IReadOnlyList<int> Pages, double Dpi, string OutputDirectory, TimeSpan Timeout);

/// <summary>Where a reference renderer left a file's page images, and what went wrong if it did not draw them all.</summary>
/// <param name="Directory">The directory holding <c>page&lt;N&gt;.png</c>.</param>
/// <param name="Error">The renderer's complaint (a timeout, an exit code, its error output), or null.</param>
public sealed record ReferenceResult(string Directory, string? Error);

/// <summary>
/// A renderer RenderDiff compares Broadside with. PDFium, pdf.js and poppler run as separate processes from what
/// <c>acquire-references.sh</c> installed (or <c>pdftoppm</c> on the path), so nothing they need is referenced by any Broadside
/// project; an engine that is not installed reports why and is skipped.
/// </summary>
public abstract class ReferenceEngine
{
    /// <summary>The engine names <see cref="Create"/> knows.</summary>
    public static readonly IReadOnlyList<string> KnownNames = ["pdfium", "pdfjs", "poppler"];

    /// <summary>Initializes a new instance of the <see cref="ReferenceEngine"/> class.</summary>
    /// <param name="name">The engine's name in the report.</param>
    protected ReferenceEngine(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Gets the engine's name in the report.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the engine draws pages (so its images belong in the output directory), rather than reading them from elsewhere.</summary>
    public virtual bool Draws => true;

    /// <summary>Creates a known engine.</summary>
    /// <param name="name"><c>pdfium</c>, <c>pdfjs</c> or <c>poppler</c>.</param>
    /// <param name="referenceDirectory">Where <c>acquire-references.sh</c> installed the reference renderers.</param>
    /// <param name="scriptDirectory">Where the engines' render scripts are (the tool's <c>reference</c> folder).</param>
    /// <returns>The engine.</returns>
    /// <exception cref="ArgumentException">The name is not known.</exception>
    public static ReferenceEngine Create(string name, string referenceDirectory, string scriptDirectory) => name switch
    {
        "pdfium" => new ScriptReferenceEngine(
            "pdfium",
            Path.Combine(referenceDirectory, "pdfium", "bin", "python"),
            ["-I", Path.Combine(scriptDirectory, "pdfium", "render.py")],
            environment: null,
            installHint: "run tools/RenderDiff/acquire-references.sh pdfium (needs python3)"),
        "pdfjs" => new ScriptReferenceEngine(
            "pdfjs",
            FindOnPath("node") ?? "node",
            [Path.Combine(scriptDirectory, "pdfjs", "render.mjs")],
            environment: new Dictionary<string, string> { ["RENDERDIFF_PDFJS_DIR"] = Path.Combine(referenceDirectory, "pdfjs") },
            installHint: "run tools/RenderDiff/acquire-references.sh pdfjs (needs node and npm)",
            requiredPath: Path.Combine(referenceDirectory, "pdfjs", "node_modules", "pdfjs-dist", "package.json")),
        "poppler" => new PopplerReferenceEngine(FindOnPath("pdftoppm")),
        _ => throw new ArgumentException($"Unknown engine '{name}'; expected one of {string.Join(", ", KnownNames)}.", nameof(name)),
    };

    /// <summary>Creates an engine that reads renders made earlier: <c>&lt;directory&gt;/&lt;relative stem&gt;/page&lt;N&gt;.png</c>, the layout every engine's renders have in a report's <c>references/&lt;engine&gt;</c> folder.</summary>
    /// <param name="name">The engine's name in the report.</param>
    /// <param name="directory">The directory.</param>
    /// <returns>The engine.</returns>
    public static ReferenceEngine FromDirectory(string name, string directory) => new DirectoryReferenceEngine(name, directory);

    /// <summary>Says why the engine cannot run here, or returns null when it can.</summary>
    /// <returns>The reason, including how to install the engine.</returns>
    public abstract string? CheckAvailable();

    /// <summary>Draws (or finds) one file's pages.</summary>
    /// <param name="request">The file and pages.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>Where the images are.</returns>
    public abstract Task<ReferenceResult> RenderAsync(ReferenceRequest request, CancellationToken cancellationToken);

    /// <summary>Finds an executable on the <c>PATH</c>.</summary>
    /// <param name="name">The executable's name.</param>
    /// <returns>Its full path, or null.</returns>
    internal static string? FindOnPath(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Runs a process to completion or until the timeout, killing it (and its children) on timeout or cancellation.</summary>
    /// <param name="executable">The executable.</param>
    /// <param name="arguments">Its arguments, passed as a list (no shell).</param>
    /// <param name="environment">Extra environment variables, or null.</param>
    /// <param name="timeout">How long it may run.</param>
    /// <param name="cancellationToken">Cancels it.</param>
    /// <returns>Null on exit code 0 with no error output, else a description of what went wrong.</returns>
    protected static async Task<string?> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                start.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = start };
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (errors)
                {
                    if (errors.Length < 4000)
                    {
                        errors.AppendLine(e.Data);
                    }
                }
            }
        };
        process.OutputDataReceived += static (_, _) => { };
        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited meanwhile.
            }

            cancellationToken.ThrowIfCancellationRequested();
            return string.Create(CultureInfo.InvariantCulture, $"timed out after {timeout.TotalSeconds:0} s");
        }

        string text;
        lock (errors)
        {
            text = errors.ToString().Trim();
        }

        if (process.ExitCode != 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"exit code {process.ExitCode}{(text.Length > 0 ? ": " + text : string.Empty)}");
        }

        return text.Length > 0 ? text : null;
    }

    /// <summary>Writes the zero-based pages as a comma-separated list.</summary>
    /// <param name="pages">The pages.</param>
    /// <returns>The list.</returns>
    protected static string PageList(IEnumerable<int> pages) => string.Join(',', pages.Select(static page => page.ToString(CultureInfo.InvariantCulture)));
}

/// <summary>Reads renders made earlier from a directory.</summary>
internal sealed class DirectoryReferenceEngine(string name, string directory) : ReferenceEngine(name)
{
    public override bool Draws => false;

    public override string? CheckAvailable() => System.IO.Directory.Exists(directory) ? null : $"the directory {directory} does not exist";

    public override Task<ReferenceResult> RenderAsync(ReferenceRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ReferenceResult(Path.Combine(directory, request.RelativeStem), null));
}

/// <summary>Runs one of the tool's render scripts (<c>reference/&lt;engine&gt;/render.*</c>) under an interpreter, once per file.</summary>
internal sealed class ScriptReferenceEngine(
    string name,
    string interpreter,
    IReadOnlyList<string> scriptArguments,
    IReadOnlyDictionary<string, string>? environment,
    string installHint,
    string? requiredPath = null) : ReferenceEngine(name)
{
    public override string? CheckAvailable()
    {
        if (!File.Exists(interpreter))
        {
            return $"{interpreter} not found; {installHint}";
        }

        if (requiredPath is not null && !File.Exists(requiredPath))
        {
            return $"{requiredPath} not found; {installHint}";
        }

        string script = scriptArguments[^1];
        return File.Exists(script) ? null : $"the render script {script} is missing";
    }

    public override async Task<ReferenceResult> RenderAsync(ReferenceRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        string[] arguments =
        [
            .. scriptArguments,
            request.PdfPath,
            request.OutputDirectory,
            request.Dpi.ToString(CultureInfo.InvariantCulture),
            PageList(request.Pages),
        ];
        string? error = await RunProcessAsync(interpreter, arguments, environment, request.Timeout, cancellationToken).ConfigureAwait(false);
        return new ReferenceResult(request.OutputDirectory, error);
    }
}

/// <summary>Runs poppler's <c>pdftoppm</c>, once per page.</summary>
internal sealed class PopplerReferenceEngine(string? executable) : ReferenceEngine("poppler")
{
    public override string? CheckAvailable() =>
        executable is null ? "pdftoppm is not on the PATH; install poppler (apt install poppler-utils, brew install poppler)" : null;

    public override async Task<ReferenceResult> RenderAsync(ReferenceRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        string resolution = request.Dpi.ToString(CultureInfo.InvariantCulture);
        var errors = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        foreach (int page in request.Pages)
        {
            TimeSpan left = request.Timeout - stopwatch.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                errors.Add($"page {page}: timed out");
                break;
            }

            string number = (page + 1).ToString(CultureInfo.InvariantCulture);
            string prefix = Path.Combine(request.OutputDirectory, $"page{page}");
            string[] arguments = ["-r", resolution, "-f", number, "-l", number, "-singlefile", "-hide-annotations", "-png", request.PdfPath, prefix];
            string? error = await RunProcessAsync(executable!, arguments, null, left, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                errors.Add($"page {page}: {error}");
            }
        }

        return new ReferenceResult(request.OutputDirectory, errors.Count == 0 ? null : string.Join(Environment.NewLine, errors));
    }
}
