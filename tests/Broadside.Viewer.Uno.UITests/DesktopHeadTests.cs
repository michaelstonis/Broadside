using System.Diagnostics;

namespace Broadside.Viewer.Uno.UITests;

/// <summary>
/// The desktop head (Skia) in a real window: its automation script opens, scrolls, zooms, selects and searches through the view's
/// input path, switches dark mode, and reads a tagged page through the automation peers; every tile it waits for is shown through Uno.
/// </summary>
public sealed class DesktopHeadTests
{
    [Fact]
    public async Task The_desktop_head_opens_scrolls_zooms_selects_and_searches()
    {
        UnoHost.Require(UnoHost.DesktopAssembly, "desktop head");
        Assert.SkipWhen(
            OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && Environment.GetEnvironmentVariable("BROADSIDE_UNO_REQUIRED") != "1",
            "No X display (run under xvfb-run).");

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(UnoHost.DesktopAssembly!);
        start.Environment["BROADSIDE_UNO_SCRIPT"] = "1";
        using Process process = Process.Start(start)!;
        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            lock (output)
            {
                output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            lock (output)
            {
                output.AppendLine(e.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The desktop head did not finish its script: " + output);
        }

        // Let the redirected output drain (the exit is seen before the last lines are read).
        await process.WaitForExitAsync(CancellationToken.None);
        string log;
        lock (output)
        {
            log = output.ToString();
        }

        Assert.True(process.ExitCode == 0, $"exit code {process.ExitCode}: {log}");
        Assert.Contains("BROADSIDE_SCRIPT_OK", log, StringComparison.Ordinal);
        foreach (string step in new[] { "open", "scroll", "zoom", "select", "search", "dark", "accessibility" })
        {
            Assert.Contains($"BROADSIDE_STEP {step} ok", log, StringComparison.Ordinal);
        }
    }
}
