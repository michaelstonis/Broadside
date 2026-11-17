using System.Globalization;

namespace Broadside.Viewer.WinUI.Sample;

/// <summary>
/// Startup and crash diagnostics: each line goes to stderr (read by the UI tests, which redirect it) and is appended to
/// <c>Broadside.Viewer.WinUI.Sample.log</c> next to the exe (a WinExe has no console, so the file is what survives a crash). Lines
/// carry the process id, so a reader can pick out one launch.
/// </summary>
internal static class StartupLog
{
    private static readonly Lock Gate = new();

    /// <summary>The log file next to the exe.</summary>
    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "Broadside.Viewer.WinUI.Sample.log");

    public static void Write(string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:O} [{Environment.ProcessId}] {message}");
        lock (Gate)
        {
            try
            {
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Diagnostics only: a read-only output folder must not stop the app.
            }
            catch (UnauthorizedAccessException)
            {
                // As above.
            }

            Console.Error.WriteLine(line);
            Console.Error.Flush();
        }
    }
}
