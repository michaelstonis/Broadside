using System.Diagnostics;

namespace Broadside.Tools.ReleaseNotes;

/// <summary>Runs git in one repository and returns its output.</summary>
internal sealed class GitRepository(string root)
{
    public string Root { get; } = root;

    public string Run(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed with exit code {process.ExitCode}: {error.Result.Trim()}");
        }

        return output;
    }

    /// <summary>The full object name of the commit <paramref name="reference"/> names, or <see langword="null"/> if it names none.</summary>
    public string? TryResolve(string reference)
    {
        try
        {
            return Run("rev-parse", "--verify", "--quiet", reference + "^{commit}").Trim();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The commits <c>git log --first-parent</c> lists from <paramref name="tip"/>, newest first, leaving out what the exclusions reach.</summary>
    public IReadOnlyList<CommitRecord> FirstParentLog(string tip, IEnumerable<string> exclude)
    {
        var args = new List<string> { "log", "--first-parent", "--format=%H%x1f%P%x1f%B%x1e", tip };
        foreach (string excluded in exclude)
        {
            args.Add("^" + excluded);
        }

        args.Add("--");
        return Parse(Run([.. args]));
    }

    /// <summary>Every commit reachable from <paramref name="tip"/> and not from the exclusions (merges left out).</summary>
    public IReadOnlyList<CommitRecord> NonMergeLog(string tip, IEnumerable<string> exclude)
    {
        var args = new List<string> { "log", "--no-merges", "--format=%H%x1f%P%x1f%B%x1e", tip };
        foreach (string excluded in exclude)
        {
            args.Add("^" + excluded);
        }

        args.Add("--");
        return Parse(Run([.. args]));
    }

    private static List<CommitRecord> Parse(string output)
    {
        var commits = new List<CommitRecord>();
        foreach (string record in output.Split('\u001e'))
        {
            string trimmed = record.TrimStart('\n', '\r');
            if (trimmed.Length == 0)
            {
                continue;
            }

            string[] fields = trimmed.Split('\u001f');
            if (fields.Length < 3)
            {
                continue;
            }

            string message = fields[2].Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
            int newline = message.IndexOf('\n', StringComparison.Ordinal);
            string subject = newline < 0 ? message : message[..newline].Trim();
            string body = newline < 0 ? string.Empty : message[(newline + 1)..].Trim();
            string[] parents = fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            commits.Add(new CommitRecord(fields[0], parents, subject, body));
        }

        return commits;
    }
}

/// <summary>One commit: its object name, its parents, and its message split into subject and body.</summary>
internal sealed record CommitRecord(string Sha, IReadOnlyList<string> Parents, string Subject, string Body)
{
    public bool IsMerge => Parents.Count > 1;
}
