using System.Diagnostics;

namespace Broadside.Tests.Writing;

/// <summary>
/// Runs a command-line PDF tool (qpdf, pdfinfo) on a file as an independent check of written output. A test that needs a tool which
/// is not on the PATH is skipped, not failed; CI installs both (qpdf and poppler-utils). <c>openssl</c> resolves to an OpenSSL 3
/// binary (see <see cref="ResolveOpenSsl"/>), whichever comes first on the PATH.
/// </summary>
internal static class ExternalTool
{
    /// <summary>Writes <paramref name="file"/> to a temporary path and runs <paramref name="tool"/> with the arguments and that path last.</summary>
    /// <returns>The exit code and the standard output and error, joined.</returns>
    public static (int ExitCode, string Output) Run(string tool, byte[] file, params string[] arguments)
    {
        string? executable = Find(tool);
        if (executable is null)
        {
            Assert.Skip(NotFound(tool));
        }

        string path = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, file);
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            start.ArgumentList.Add(path);
            using Process process = Process.Start(start)!;
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Like <see cref="Run"/>, for a tool whose standard output is binary, such as <c>pdftoppm</c> writing a PPM.</summary>
    /// <returns>The exit code, the standard output's bytes and the standard error.</returns>
    public static (int ExitCode, byte[] Output, string Error) RunBinary(string tool, byte[] file, params string[] arguments)
    {
        string? executable = Find(tool);
        if (executable is null)
        {
            Assert.Skip(NotFound(tool));
        }

        string path = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, file);
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            start.ArgumentList.Add(path);
            using Process process = Process.Start(start)!;
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var output = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(output);
            process.WaitForExit();
            return (process.ExitCode, output.ToArray(), error.Result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Runs <paramref name="tool"/> with <c>--version</c> alone.</summary>
    /// <returns>The standard output and error joined, or null when the tool is not on the PATH.</returns>
    public static string? Version(string tool)
    {
        string? executable = Find(tool);
        if (executable is null)
        {
            return null;
        }

        var start = new ProcessStartInfo(executable, "--version")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private static readonly Lazy<string?> OpenSslExecutable = new(ResolveOpenSsl);

    private static string NotFound(string tool) =>
        tool == "openssl"
            ? "OpenSSL 3 or later was not found (set BROADSIDE_OPENSSL to its path; the system LibreSSL is not enough)."
            : $"{tool} is not on the PATH.";

    private static string? Find(string tool) => tool == "openssl" ? OpenSslExecutable.Value : FindOnPath(tool);

    private static string? FindOnPath(string tool)
    {
        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        string[] names = OperatingSystem.IsWindows() ? [tool + ".exe", tool] : [tool];
        return directories.SelectMany(directory => names.Select(name => Path.Combine(directory, name))).FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// The first OpenSSL 3 binary: <c>BROADSIDE_OPENSSL</c>, then the PATH entries in order, then the well-known Homebrew and Linux
    /// locations. A binary whose <c>openssl version</c> does not start with <c>OpenSSL 3</c> (macOS ships LibreSSL, whose <c>cms</c>
    /// and <c>ts</c> differ) is passed over. Null when there is none.
    /// </summary>
    private static string? ResolveOpenSsl()
    {
        var candidates = new List<string>();
        string? configured = Environment.GetEnvironmentVariable("BROADSIDE_OPENSSL");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(configured);
        }

        string name = OperatingSystem.IsWindows() ? "openssl.exe" : "openssl";
        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        candidates.AddRange(directories.Select(directory => Path.Combine(directory, name)));
        candidates.AddRange(
            new[]
            {
                "/opt/homebrew/opt/openssl@3/bin",
                "/opt/homebrew/bin",
                "/usr/local/opt/openssl@3/bin",
                "/usr/local/bin",
                "/usr/bin",
                "/usr/local/ssl/bin",
            }.Select(directory => Path.Combine(directory, name)));

        foreach (string candidate in candidates.Distinct(StringComparer.Ordinal).Where(File.Exists))
        {
            try
            {
                var start = new ProcessStartInfo(candidate, "version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using Process process = Process.Start(start)!;
                string output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0 && output.StartsWith("OpenSSL 3", StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            {
                // Not runnable here; try the next candidate.
            }
        }

        return null;
    }
}
