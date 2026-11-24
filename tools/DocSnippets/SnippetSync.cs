using System.Text;

namespace Broadside.Tools.DocSnippets;

/// <summary>One problem with a snippet marker: where it is and what is wrong.</summary>
/// <param name="File">The page, relative to the repository root.</param>
/// <param name="Line">The 1-based line of the marker.</param>
/// <param name="Message">What is wrong.</param>
public sealed record SnippetProblem(string File, int Line, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"{File}:{Line}: {Message}";
}

/// <summary>A page after its snippets were regenerated.</summary>
/// <param name="Text">The page with every snippet block equal to its region.</param>
/// <param name="Problems">The markers that could not be resolved; their blocks are left as they were.</param>
/// <param name="SnippetCount">How many markers the page has.</param>
public sealed record SyncResult(string Text, IReadOnlyList<SnippetProblem> Problems, int SnippetCount);

/// <summary>
/// Includes sample code in Markdown pages. A page names a region of a sample file with
/// <c>&lt;!-- snippet: samples/guides/Reading/Program.cs#open-path --&gt;</c>, followed by a fenced code block and
/// <c>&lt;!-- endsnippet --&gt;</c>. The block must equal the lines between <c>#region open-path</c> and its <c>#endregion</c>,
/// with the markers of nested regions removed and the common indentation stripped.
/// </summary>
public static class SnippetSync
{
    private const string StartPrefix = "<!-- snippet:";
    private const string MarkerSuffix = "-->";
    private const string EndMarker = "<!-- endsnippet -->";

    /// <summary>The folder every included file must be in, so that the docs build compiles it.</summary>
    public const string SamplesFolder = "samples/guides/";

    /// <summary>Checks, or with <paramref name="write"/> regenerates, every Markdown page under <c>site/</c>.</summary>
    /// <param name="root">The repository root.</param>
    /// <param name="write">Rewrite stale blocks instead of reporting them.</param>
    /// <param name="snippetCount">How many snippet markers the pages have.</param>
    /// <returns>The problems: unresolved markers, and in check mode every stale block.</returns>
    public static IReadOnlyList<SnippetProblem> CheckSite(string root, bool write, out int snippetCount)
    {
        string site = Path.Combine(root, "site");
        List<SnippetProblem> problems = [];
        snippetCount = 0;
        IEnumerable<string> pages = Directory.EnumerateFiles(site, "*.md", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(site, path).StartsWith(".lunet", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        foreach (string page in pages)
        {
            string relative = Path.GetRelativePath(root, page).Replace('\\', '/');
            string text = File.ReadAllText(page).ReplaceLineEndings("\n");
            SyncResult result = Sync(text, relative, path => ReadSource(root, path), SamplesFolder);
            snippetCount += result.SnippetCount;
            problems.AddRange(result.Problems);
            if (result.Text == text)
            {
                continue;
            }

            if (write)
            {
                File.WriteAllText(page, result.Text);
            }
            else
            {
                problems.Add(new SnippetProblem(relative, 1, "sample code differs from the samples; regenerate it with: dotnet run --project tools/DocSnippets -- --write"));
            }
        }

        return problems;
    }

    /// <summary>Regenerates the snippet blocks of one page.</summary>
    /// <param name="markdown">The page, with <c>\n</c> line endings.</param>
    /// <param name="pagePath">The page's path, for problems.</param>
    /// <param name="readSource">Reads a file by its repository-relative path; <see langword="null"/> when there is none.</param>
    /// <param name="allowedFolder">The repository-relative folder every included file must be in.</param>
    /// <returns>The page with current blocks, and the markers that could not be resolved.</returns>
    public static SyncResult Sync(string markdown, string pagePath, Func<string, string?> readSource, string allowedFolder)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(readSource);
        string[] lines = markdown.Split('\n');
        var output = new List<string>(lines.Length);
        var problems = new List<SnippetProblem>();
        int count = 0;
        bool inFence = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            output.Add(line);
            string trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence; // a marker shown inside a code block is an example, not a snippet
                continue;
            }

            if (inFence || !trimmed.StartsWith(StartPrefix, StringComparison.Ordinal) || !trimmed.EndsWith(MarkerSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            count++;
            int end = Array.FindIndex(lines, i + 1, l => l.Trim() == EndMarker || l.Trim().StartsWith(StartPrefix, StringComparison.Ordinal));
            if (end < 0 || lines[end].Trim() != EndMarker)
            {
                problems.Add(new SnippetProblem(pagePath, i + 1, $"no {EndMarker} after this snippet marker"));
                continue;
            }

            string reference = trimmed[StartPrefix.Length..^MarkerSuffix.Length].Trim();
            string? code = Resolve(reference, readSource, allowedFolder, out string? problem);
            if (code is null)
            {
                problems.Add(new SnippetProblem(pagePath, i + 1, problem!));
                continue;
            }

            output.Add("```" + Language(reference));
            output.Add(code);
            output.Add("```");
            output.Add(lines[end]);
            i = end;
        }

        return new SyncResult(string.Join('\n', output), problems, count);
    }

    /// <summary>Gets the lines of a region, without the markers of nested regions and without their common indentation.</summary>
    /// <param name="source">The file's text.</param>
    /// <param name="region">The region's name.</param>
    /// <returns>The region's code, or <see langword="null"/> when the file has no such region.</returns>
    public static string? ExtractRegion(string source, string region)
    {
        ArgumentNullException.ThrowIfNull(source);
        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        int start = Array.FindIndex(lines, l => IsRegionStart(l, out string? name) && name == region);
        if (start < 0)
        {
            return null;
        }

        var body = new List<string>();
        int depth = 1;
        for (int i = start + 1; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (IsRegionStart(lines[i], out _))
            {
                depth++;
                continue;
            }

            if (trimmed.StartsWith("#endregion", StringComparison.Ordinal))
            {
                if (--depth == 0)
                {
                    return Dedent(body);
                }

                continue;
            }

            body.Add(lines[i].TrimEnd());
        }

        return null;
    }

    private static bool IsRegionStart(string line, out string? name)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith("#region ", StringComparison.Ordinal))
        {
            name = trimmed["#region ".Length..].Trim();
            return true;
        }

        name = null;
        return false;
    }

    private static string Dedent(List<string> lines)
    {
        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        int indent = lines.Where(l => l.Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        var builder = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            builder.Append(lines[i].Length == 0 ? string.Empty : lines[i][indent..]);
        }

        return builder.ToString();
    }

    private static string? Resolve(string reference, Func<string, string?> readSource, string allowedFolder, out string? problem)
    {
        int hash = reference.LastIndexOf('#');
        if (hash <= 0 || hash == reference.Length - 1)
        {
            problem = $"snippet '{reference}' is not <path>#<region>";
            return null;
        }

        string path = reference[..hash];
        string region = reference[(hash + 1)..];
        if (!path.StartsWith(allowedFolder, StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
        {
            problem = $"snippet file {path} is not in {allowedFolder}, whose samples the docs build compiles";
            return null;
        }

        string? source = readSource(path);
        if (source is null)
        {
            problem = $"snippet file {path} not found";
            return null;
        }

        string? code = ExtractRegion(source, region);
        problem = code is null ? $"{path} has no region '{region}' (#region {region} ... #endregion)" : null;
        return code;
    }

    private static string? ReadSource(string root, string path)
    {
        string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    private static string Language(string reference)
    {
        string extension = Path.GetExtension(reference[..reference.LastIndexOf('#')]);
        return extension switch
        {
            ".cs" => "csharp",
            ".csproj" or ".props" or ".xml" => "xml",
            ".json" => "json",
            _ => string.Empty,
        };
    }
}
