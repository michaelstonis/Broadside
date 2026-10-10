using System.Diagnostics;

namespace Broadside.Tests.Writing;

/// <summary>
/// Runs a command-line PDF tool (qpdf, pdfinfo) on a file as an independent check of written output. A test that needs a tool which
/// is not on the PATH is skipped, not failed; CI installs both (qpdf and poppler-utils).
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
            Assert.Skip($"{tool} is not on the PATH.");
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

    private static string? Find(string tool)
    {
        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        string[] names = OperatingSystem.IsWindows() ? [tool + ".exe", tool] : [tool];
        return directories.SelectMany(directory => names.Select(name => Path.Combine(directory, name))).FirstOrDefault(File.Exists);
    }
}
