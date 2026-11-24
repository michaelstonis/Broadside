using System.Diagnostics;

namespace Broadside.Tools.ReleaseNotes.Tests;

/// <summary>A throwaway git repository in a temporary folder, with helpers that commit, branch, merge and tag.</summary>
internal sealed class TempRepository : IDisposable
{
    private int _fileCounter;

    public TempRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), "broadside-release-notes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Git("init", "-q", "-b", "main");
        Git("config", "user.name", "Test Author");
        Git("config", "user.email", "author@example.com");
        Git("config", "commit.gpgsign", "false");
        Git("config", "tag.gpgsign", "false");
        Commit("chore: initial commit");
    }

    public string Root { get; }

    /// <summary>Commits a new file with the given message (subject, blank line and body when it has several paragraphs).</summary>
    public TempRepository Commit(string message)
    {
        File.WriteAllText(Path.Combine(Root, $"file{_fileCounter++}.txt"), message);
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
        return this;
    }

    /// <summary>Writes a file (creating folders) and commits it.</summary>
    public TempRepository CommitFile(string path, string content, string message)
    {
        string full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
        return this;
    }

    public TempRepository Tag(string name)
    {
        Git("tag", "-a", name, "-m", name);
        return this;
    }

    public TempRepository Checkout(string branch, bool create = false)
    {
        if (create)
        {
            Git("checkout", "-q", "-b", branch);
        }
        else
        {
            Git("checkout", "-q", branch);
        }

        return this;
    }

    /// <summary>Merges <paramref name="branch"/> into the current branch with a merge commit and the given message.</summary>
    public TempRepository Merge(string branch, string message)
    {
        Git("merge", "-q", "--no-ff", branch, "-m", message);
        return this;
    }

    public string Git(params string[] args)
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

        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {error}");
        }

        return output.Trim();
    }

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
