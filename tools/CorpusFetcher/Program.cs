using System.Globalization;
using System.Text.Json;

namespace Broadside.Tools.CorpusFetcher;

/// <summary>Entry point: <c>dotnet run --project tools/CorpusFetcher -- [--only id,id] [--include-linked] [--list]</c>.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
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

        string? repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory()) ?? FindRepositoryRoot(AppContext.BaseDirectory);
        string manifestPath = options.ManifestPath
            ?? (repositoryRoot is null ? Path.Combine(AppContext.BaseDirectory, "corpora.json") : Path.Combine(repositoryRoot, "tools", "CorpusFetcher", "corpora.json"));
        string? corpusDirectory = options.CorpusDirectory ?? (repositoryRoot is null ? null : Path.Combine(repositoryRoot, "corpus"));
        if (corpusDirectory is null)
        {
            await Console.Error.WriteLineAsync("Could not find Broadside.slnx above the current directory; pass --corpus-dir <path>.");
            return 2;
        }

        CorpusManifest manifest;
        try
        {
            await using FileStream manifestStream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync(manifestStream, CorpusJsonContext.Default.CorpusManifest)
                ?? throw new InvalidDataException("Manifest is empty.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync($"Cannot read manifest {manifestPath}: {ex.Message}");
            return 2;
        }

        string? validationError = Validate(manifest);
        if (validationError is not null)
        {
            await Console.Error.WriteLineAsync($"Invalid manifest {manifestPath}: {validationError}");
            return 2;
        }

        if (options.List)
        {
            PrintList(manifest, Console.Out);
            return 0;
        }

        List<CorpusEntry> selected = manifest.Corpora;
        if (options.Only.Count > 0)
        {
            var unknown = options.Only.Where(id => !manifest.Corpora.Exists(c => string.Equals(c.Id, id, StringComparison.Ordinal))).ToList();
            if (unknown.Count > 0)
            {
                await Console.Error.WriteLineAsync($"Unknown corpus id(s): {string.Join(", ", unknown)}. Use --list to see the ids.");
                return 2;
            }

            selected = manifest.Corpora.Where(c => options.Only.Contains(c.Id, StringComparer.Ordinal)).ToList();
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        using SocketsHttpHandler handler = CorpusFetcher.CreateHandler();
        using HttpClient http = CorpusFetcher.CreateHttpClient(handler);
        var fetcher = new CorpusFetcher(corpusDirectory, http, Console.Out);
        LockFile lockFile = await LoadLockAsync(fetcher.LockFilePath, cancellation.Token);
        await Console.Out.WriteLineAsync($"Corpus directory: {corpusDirectory}");

        var results = new List<CorpusResult>(selected.Count);
        try
        {
            foreach (CorpusEntry entry in selected)
            {
                CorpusResult result = await fetcher.FetchAsync(entry, lockFile, options.IncludeLinked, cancellation.Token);
                results.Add(result);
                if (!result.Manual)
                {
                    await SaveLockAsync(fetcher.LockFilePath, lockFile, cancellation.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Cancelled.");
            return 130;
        }

        PrintSummary(results, Console.Out);
        return results.Exists(static r => r.Error is not null) ? 1 : 0;
    }

    private static string? FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? Validate(CorpusManifest manifest)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (CorpusEntry entry in manifest.Corpora)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || SafePath.NormalizeEntryName(entry.Id) != entry.Id || entry.Id.Contains('/', StringComparison.Ordinal) || entry.Id.StartsWith('.'))
            {
                return $"corpus id '{entry.Id}' is not a plain folder name";
            }

            if (!ids.Add(entry.Id))
            {
                return $"duplicate corpus id '{entry.Id}'";
            }

            if (entry.Urls.Count == 0)
            {
                return $"corpus '{entry.Id}' has no urls";
            }

            foreach (Uri url in entry.Urls)
            {
                if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && !entry.Manual))
                {
                    return $"corpus '{entry.Id}' url '{url}' must be absolute https";
                }
            }

            if (entry.Kind == CorpusKind.GitSparse && !entry.Manual && string.IsNullOrEmpty(entry.Ref))
            {
                return $"git-sparse corpus '{entry.Id}' must pin a ref";
            }

            if (entry.StripPrefix < 0)
            {
                return $"corpus '{entry.Id}' has a negative stripPrefix";
            }
        }

        return null;
    }

    private static async Task<LockFile> LoadLockAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new LockFile();
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, CorpusJsonContext.Default.LockFile, cancellationToken) ?? new LockFile();
        }
        catch (JsonException ex)
        {
            await Console.Error.WriteLineAsync($"Ignoring unreadable lock file {path}: {ex.Message}");
            return new LockFile();
        }
    }

    private static async Task SaveLockAsync(string path, LockFile lockFile, CancellationToken cancellationToken)
    {
        string partial = path + ".partial";
        await using (FileStream stream = File.Create(partial))
        {
            await JsonSerializer.SerializeAsync(stream, lockFile, CorpusJsonContext.Default.LockFile, cancellationToken);
        }

        File.Move(partial, path, overwrite: true);
    }

    private static void PrintList(CorpusManifest manifest, TextWriter output)
    {
        foreach (CorpusEntry entry in manifest.Corpora)
        {
            string flag = entry.Manual ? " (manual)" : string.Empty;
            output.WriteLine($"{entry.Id}{flag}");
            output.WriteLine($"  kind:    {KindName(entry.Kind)}{(entry.Ref is null ? string.Empty : " @ " + entry.Ref)}");
            output.WriteLine($"  size:    {entry.ApproximateSize}");
            output.WriteLine($"  license: {entry.License}");
            output.WriteLine($"  {entry.Description}");
            foreach (Uri url in entry.Urls)
            {
                output.WriteLine($"  {url}");
            }

            output.WriteLine();
        }
    }

    private static string KindName(CorpusKind kind) => kind switch
    {
        CorpusKind.GitSparse => "git-sparse",
        CorpusKind.Zip => "zip",
        CorpusKind.Files => "files",
        _ => kind.ToString(),
    };

    private static void PrintSummary(List<CorpusResult> results, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("Summary");
        foreach (CorpusResult result in results)
        {
            if (result.Manual)
            {
                output.WriteLine($"  {result.Id}: manual, not downloaded");
                continue;
            }

            if (result.Error is not null)
            {
                output.WriteLine($"  {result.Id}: FAILED: {result.Error}");
                continue;
            }

            string links = result.LinksFound > 0
                ? string.Create(CultureInfo.InvariantCulture, $", {result.LinksFound} links{(result.LinksFailed > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({result.LinksFailed} failed)") : string.Empty)}")
                : string.Empty;
            string filtered = result.EntriesFiltered > 0 ? string.Create(CultureInfo.InvariantCulture, $", {result.EntriesFiltered} filtered out") : string.Empty;
            string rejected = result.EntriesRejected > 0 ? string.Create(CultureInfo.InvariantCulture, $", {result.EntriesRejected} UNSAFE entries rejected") : string.Empty;
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {result.Id}: {result.FilesWritten} files written ({CorpusFetcher.FormatBytes(result.BytesWritten)}), {result.FilesSkipped} skipped{links}{filtered}{rejected}"));
        }
    }
}

/// <summary>Parsed command line.</summary>
internal sealed class Options
{
    public List<string> Only { get; } = [];

    public bool IncludeLinked { get; private set; }

    public bool List { get; private set; }

    public bool ShowHelp { get; private set; }

    public string? CorpusDirectory { get; private set; }

    public string? ManifestPath { get; private set; }

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
                case "--only":
                    if (i + 1 >= args.Length)
                    {
                        errors.WriteLine("--only needs a comma-separated list of ids.");
                        return null;
                    }

                    options.Only.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--include-linked":
                    options.IncludeLinked = true;
                    break;
                case "--list":
                    options.List = true;
                    break;
                case "--corpus-dir":
                    if (i + 1 >= args.Length)
                    {
                        errors.WriteLine("--corpus-dir needs a path.");
                        return null;
                    }

                    options.CorpusDirectory = args[++i];
                    break;
                case "--manifest":
                    if (i + 1 >= args.Length)
                    {
                        errors.WriteLine("--manifest needs a path.");
                        return null;
                    }

                    options.ManifestPath = args[++i];
                    break;
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                default:
                    if (arg.StartsWith("--only=", StringComparison.Ordinal))
                    {
                        options.Only.AddRange(arg["--only=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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

        output.WriteLine("Usage: dotnet run --project tools/CorpusFetcher -- [options]");
        output.WriteLine();
        output.WriteLine("Downloads the PDF corpora listed in tools/CorpusFetcher/corpora.json into corpus/ (gitignored).");
        output.WriteLine("Re-running verifies corpus/manifest.lock.json and skips files that are already present with a matching SHA-256.");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --only <id,id>      Fetch only these corpora (see --list for ids).");
        output.WriteLine("  --include-linked    Also download the targets of pdf.js-style .link files (external, often dead URLs).");
        output.WriteLine("  --list              Print the manifest and exit.");
        output.WriteLine("  --corpus-dir <dir>  Write to this directory instead of <repo>/corpus.");
        output.WriteLine("  --manifest <file>   Read this manifest instead of tools/CorpusFetcher/corpora.json.");
        output.WriteLine("  -h, --help          Show this help.");
    }
}
