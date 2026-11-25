using System.Globalization;
using System.Text;

namespace Broadside.Tools.ReleaseNotes;

/// <summary>How one package's public API (its <c>PublicAPI.Shipped.txt</c> and <c>PublicAPI.Unshipped.txt</c>) changed between two refs.</summary>
public sealed record PackageApiChange(string Package, IReadOnlyList<string> Added, IReadOnlyList<string> Removed);

/// <summary>The public API diff: the entries of every package's API files at each ref, compared as sets.</summary>
internal static class ApiDiff
{
    /// <summary>The most entries listed per package and direction; the compare link has the rest.</summary>
    private const int ListLimit = 200;

    public static IReadOnlyList<PackageApiChange> Compare(GitRepository git, string? fromSha, string toSha)
    {
        Dictionary<string, HashSet<string>> before = fromSha is null ? [] : Read(git, fromSha);
        Dictionary<string, HashSet<string>> after = Read(git, toSha);
        var changes = new List<PackageApiChange>();
        foreach (string package in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            HashSet<string> old = before.GetValueOrDefault(package) ?? [];
            HashSet<string> current = after.GetValueOrDefault(package) ?? [];
            List<string> added = [.. current.Except(old).Order(StringComparer.Ordinal)];
            List<string> removed = [.. old.Except(current).Order(StringComparer.Ordinal)];
            if (added.Count > 0 || removed.Count > 0)
            {
                changes.Add(new PackageApiChange(package, added, removed));
            }
        }

        return changes;
    }

    public static string Render(IReadOnlyList<PackageApiChange> changes, string url, string? from, string toRef)
    {
        var text = new StringBuilder("### Public API\n\n");
        if (changes.Count == 0)
        {
            text.Append("No change to any package's public API.\n");
            return text.ToString();
        }

        text.Append("Entries added to and removed from each package's `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`");
        text.Append(from is null ? ".\n\n" : string.Create(CultureInfo.InvariantCulture, $" ([diff]({url}/compare/{from}...{toRef})).\n\n"));
        text.Append("| Package | Added | Removed |\n|---|---:|---:|\n");
        foreach (PackageApiChange change in changes)
        {
            text.Append(CultureInfo.InvariantCulture, $"| `{change.Package}` | {change.Added.Count} | {change.Removed.Count} |\n");
        }

        text.Append('\n');
        foreach (PackageApiChange change in changes)
        {
            text.Append(CultureInfo.InvariantCulture, $"<details><summary><code>{change.Package}</code>: {change.Added.Count} added, {change.Removed.Count} removed</summary>\n\n```diff\n");
            foreach (string entry in change.Removed.Take(ListLimit))
            {
                text.Append("- ").Append(entry).Append('\n');
            }

            foreach (string entry in change.Added.Take(ListLimit))
            {
                text.Append("+ ").Append(entry).Append('\n');
            }

            int omitted = Math.Max(0, change.Removed.Count - ListLimit) + Math.Max(0, change.Added.Count - ListLimit);
            if (omitted > 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"# {omitted} more in the full diff\n");
            }

            text.Append("```\n\n</details>\n\n");
        }

        return text.ToString();
    }

    /// <summary>Each package's API entries at <paramref name="sha"/>: shipped plus unshipped, less the unshipped <c>*REMOVED*</c> entries.</summary>
    private static Dictionary<string, HashSet<string>> Read(GitRepository git, string sha)
    {
        var packages = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var removedByPackage = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        string[] files = git.Run("ls-tree", "-r", "--name-only", sha, "--", "src")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            if (name is not ("PublicAPI.Shipped.txt" or "PublicAPI.Unshipped.txt"))
            {
                continue;
            }

            string[] segments = file.Split('/');
            if (segments.Length < 3)
            {
                continue;
            }

            string package = segments[1];
            HashSet<string> entries = packages.TryGetValue(package, out HashSet<string>? existing) ? existing : packages[package] = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> removed = removedByPackage.TryGetValue(package, out HashSet<string>? r) ? r : removedByPackage[package] = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in git.Run("show", $"{sha}:{file}").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                if (line.StartsWith("*REMOVED*", StringComparison.Ordinal))
                {
                    removed.Add(line["*REMOVED*".Length..]);
                }
                else
                {
                    entries.Add(line);
                }
            }
        }

        foreach ((string package, HashSet<string> removed) in removedByPackage)
        {
            packages[package].ExceptWith(removed);
        }

        return packages;
    }
}
