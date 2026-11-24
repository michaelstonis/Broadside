using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Broadside.Tools.ReleaseNotes;

/// <summary>What to write the notes for.</summary>
public sealed record ReleaseNotesOptions
{
    /// <summary>The last released ref; by default the highest <c>v*</c> tag (SemVer order) below the version tagged on <see cref="To"/>, reachable or not.</summary>
    public string? From { get; init; }

    /// <summary>The ref being released.</summary>
    public string To { get; init; } = "HEAD";

    /// <summary>The version in the heading; by default the <c>v*</c> tag on <see cref="To"/> without its <c>v</c>, else <c>Unreleased</c>.</summary>
    public string? Version { get; init; }

    /// <summary>The GitHub repository the links point at.</summary>
    public string Repository { get; init; } = "michaelstonis/Broadside";
}

/// <summary>One line of the notes.</summary>
public sealed record ReleaseNoteEntry(string Type, string? Scope, string Description, string? BreakingNote, string Sha)
{
    /// <summary>Whether the change breaks compatibility (a <c>!</c> after the type or a <c>BREAKING CHANGE:</c> footer).</summary>
    public bool IsBreaking => BreakingNote is not null;
}

/// <summary>The generated notes.</summary>
public sealed record ReleaseNotesResult(string Version, string? From, string To, IReadOnlyList<ReleaseNoteEntry> Entries, IReadOnlyList<PackageApiChange> ApiChanges, string Markdown);

/// <summary>
/// Release notes from the Conventional Commit titles between two refs. The first-parent history of the release is read; a merge
/// whose pull request title (GitHub's merge commit body) follows Conventional Commits is one entry, and any other merge
/// (<c>merge: #N ...</c>, a release pull request, <c>Merge main into ...</c>) is replaced by the commits it brought in, read the
/// same way. Entries are grouped by type, then scope; breaking changes are listed first; the public API diff follows.
/// </summary>
public static partial class ReleaseNotesGenerator
{
    private static readonly (string Title, string[] Types)[] Sections =
    [
        ("Features", ["feat"]),
        ("Fixes", ["fix"]),
        ("Performance", ["perf"]),
        ("Refactoring", ["refactor"]),
        ("Documentation", ["docs"]),
        ("Tests", ["test"]),
        ("Benchmarks", ["bench"]),
        ("Build and CI", ["build", "ci"]),
        ("Reverts", ["revert"]),
        ("Chores", ["chore", "style"]),
    ];

    /// <summary>Types whose merges are expanded into the commits they brought in, and whose own commits are left out.</summary>
    private static readonly HashSet<string> ContainerTypes = new(StringComparer.Ordinal) { "merge", "release" };

    public static ReleaseNotesResult Generate(string repositoryRoot, ReleaseNotesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var git = new GitRepository(repositoryRoot);
        string toSha = git.TryResolve(options.To) ?? throw new ArgumentException($"'{options.To}' names no commit.", nameof(options));
        string? from = options.From ?? FindLastTag(git, toSha);
        string? fromSha = from is null ? null : git.TryResolve(from) ?? throw new ArgumentException($"'{from}' names no commit.", nameof(options));
        string version = options.Version ?? TagOn(git, toSha)?[1..] ?? "Unreleased";

        var entries = new List<ReleaseNoteEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Walk(git, toSha, fromSha is null ? [] : [fromSha], entries, seen);
        entries.Reverse();

        IReadOnlyList<PackageApiChange> api = ApiDiff.Compare(git, fromSha, toSha);
        string date = git.Run("log", "-1", "--format=%cs", toSha).Trim();
        string toRef = version == "Unreleased" ? toSha[..12] : TagOn(git, toSha) ?? "v" + version;
        string markdown = Render(options.Repository, version, date, from, toRef, entries, api);
        return new ReleaseNotesResult(version, from, toRef, entries, api, markdown);
    }

    private static void Walk(GitRepository git, string tip, IReadOnlyList<string> exclude, List<ReleaseNoteEntry> entries, HashSet<string> seen)
    {
        foreach (CommitRecord commit in git.FirstParentLog(tip, exclude))
        {
            if (!seen.Add(commit.Sha))
            {
                continue;
            }

            if (commit.IsMerge)
            {
                (string title, string? pullRequest) = MergeTitle(commit);
                ConventionalTitle? parsed = ConventionalTitle.TryParse(title);
                string[] branchExclude = [commit.Parents[0], .. exclude];
                if (parsed is null || ContainerTypes.Contains(parsed.Type))
                {
                    foreach (string parent in commit.Parents.Skip(1))
                    {
                        Walk(git, parent, branchExclude, entries, seen);
                    }

                    continue;
                }

                string description = parsed.Description;
                if (pullRequest is not null && !description.Contains('#' + pullRequest, StringComparison.Ordinal))
                {
                    description += $" (#{pullRequest})";
                }

                string? breaking = parsed.Bang ? description : null;
                foreach (CommitRecord branchCommit in commit.Parents.Skip(1).SelectMany(p => git.NonMergeLog(p, branchExclude)))
                {
                    seen.Add(branchCommit.Sha);
                    ConventionalTitle? inner = ConventionalTitle.TryParse(branchCommit.Subject);
                    string? footer = ConventionalTitle.BreakingFooter(branchCommit.Body);
                    if (footer is not null || inner?.Bang == true)
                    {
                        breaking = breaking is null ? footer ?? inner!.Description : breaking;
                    }
                }

                entries.Add(new ReleaseNoteEntry(parsed.Type, parsed.Scope, description, breaking, commit.Sha));
                continue;
            }

            ConventionalTitle? conventional = ConventionalTitle.TryParse(commit.Subject);
            if (conventional is null)
            {
                entries.Add(new ReleaseNoteEntry(string.Empty, null, commit.Subject, ConventionalTitle.BreakingFooter(commit.Body), commit.Sha));
                continue;
            }

            if (ContainerTypes.Contains(conventional.Type) || (conventional.Type == "chore" && conventional.Scope == "release"))
            {
                continue;
            }

            string? note = ConventionalTitle.BreakingFooter(commit.Body) ?? (conventional.Bang ? conventional.Description : null);
            entries.Add(new ReleaseNoteEntry(conventional.Type, conventional.Scope, conventional.Description, note, commit.Sha));
        }
    }

    /// <summary>The title a merge commit carries: GitHub's pull request title (the body's first line) or the subject.</summary>
    private static (string Title, string? PullRequest) MergeTitle(CommitRecord commit)
    {
        Match github = GitHubMergePattern().Match(commit.Subject);
        if (github.Success)
        {
            string firstLine = commit.Body.Split('\n', 2)[0].Trim();
            return (firstLine.Length == 0 ? commit.Subject : firstLine, github.Groups["number"].Value);
        }

        return (commit.Subject, null);
    }

    /// <summary>
    /// The latest release before <paramref name="toSha"/>: the highest <c>v*</c> tag, reachable or not (releases are tagged on
    /// main's merge commit, which the integration branch does not contain), below the version tagged on <paramref name="toSha"/>.
    /// </summary>
    private static string? FindLastTag(GitRepository git, string toSha)
    {
        SemanticVersion? released = TagOn(git, toSha) is { } tag ? SemanticVersion.TryParse(tag[1..]) : null;
        return git.Run("tag", "--list", "v*")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => (Tag: name, Version: SemanticVersion.TryParse(name[1..])))
            .Where(t => t.Version is not null && (released is null || t.Version.CompareTo(released) < 0))
            .OrderByDescending(t => t.Version)
            .Select(t => t.Tag)
            .FirstOrDefault();
    }

    private static string? TagOn(GitRepository git, string sha) =>
        git.Run("tag", "--points-at", sha, "--list", "v*")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => SemanticVersion.TryParse(tag[1..]) is not null)
            .OrderByDescending(tag => SemanticVersion.TryParse(tag[1..]))
            .FirstOrDefault();

    private static string Render(string repository, string version, string date, string? from, string toRef, List<ReleaseNoteEntry> entries, IReadOnlyList<PackageApiChange> api)
    {
        string url = "https://github.com/" + repository;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"## {version} ({date})\n\n");
        text.Append(from is null
            ? "Every change up to this release.\n\n"
            : string.Create(CultureInfo.InvariantCulture, $"Changes since [`{from}`]({url}/releases/tag/{from}). Full diff: [`{from}...{toRef}`]({url}/compare/{from}...{toRef}).\n\n"));

        if (entries.Count == 0)
        {
            text.Append("No changes.\n\n");
        }

        List<ReleaseNoteEntry> breaking = [.. entries.Where(e => e.IsBreaking)];
        bool apiRemovals = api.Any(p => p.Removed.Count > 0);
        if (breaking.Count > 0 || apiRemovals)
        {
            text.Append("### Breaking changes\n\n");
            foreach (ReleaseNoteEntry entry in breaking)
            {
                text.Append(Line(url, entry.Scope, entry.BreakingNote!));
            }

            if (apiRemovals)
            {
                text.Append("- Public API removed or changed in ")
                    .AppendJoin(", ", api.Where(p => p.Removed.Count > 0).Select(p => $"`{p.Package}`"))
                    .Append(": see Public API below.\n");
            }

            text.Append('\n');
        }

        var known = new HashSet<string>(Sections.SelectMany(s => s.Types), StringComparer.Ordinal);
        IEnumerable<(string Title, List<ReleaseNoteEntry> Items)> groups = Sections
            .Select(s => (s.Title, Items: entries.Where(e => s.Types.Contains(e.Type)).ToList()))
            .Append(("Other changes", entries.Where(e => !known.Contains(e.Type)).ToList()));
        foreach ((string title, List<ReleaseNoteEntry> items) in groups)
        {
            if (items.Count == 0)
            {
                continue;
            }

            text.Append(CultureInfo.InvariantCulture, $"### {title}\n\n");
            foreach (ReleaseNoteEntry entry in items.OrderBy(e => e.Scope ?? string.Empty, StringComparer.Ordinal))
            {
                string description = title == "Other changes" && entry.Type.Length > 0 ? $"{entry.Type}: {entry.Description}" : entry.Description;
                text.Append(Line(url, entry.Scope, description));
            }

            text.Append('\n');
        }

        text.Append(ApiDiff.Render(api, url, from, toRef));
        return text.ToString().TrimEnd() + "\n";
    }

    private static string Line(string url, string? scope, string description)
    {
        string linked = IssueReference().Replace(description, m => $"[#{m.Groups["n"].Value}]({url}/issues/{m.Groups["n"].Value})");
        return scope is null ? $"- {linked}\n" : $"- **{scope}:** {linked}\n";
    }

    [GeneratedRegex(@"^Merge pull request #(?<number>\d+) from \S+", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubMergePattern();

    [GeneratedRegex(@"(?<![\w&\[/])#(?<n>\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex IssueReference();
}
