using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Broadside.Viewer.WinUI.UITests;

/// <summary>
/// The sample app running one document, driven through UI Automation (FlaUI, UIA3). The sample is started here with its stdout and
/// stderr captured, so when it exits early (or its window never opens) the failure carries its exit code, its output, its startup
/// log and the Application event log entries written since the launch; a timed-out wait carries all of it too.
/// </summary>
internal sealed partial class SampleApp : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private readonly Process _process;
    private readonly FlaUI.Core.Application _app;
    private readonly StringBuilder _output = new();
    private readonly Stopwatch _sinceStart = Stopwatch.StartNew();
    private readonly string _exe;
    private readonly int _processId;
    private bool _closing;

    private SampleApp(string document)
    {
        Document = document;
        _exe = FindSample();
        var start = new ProcessStartInfo(_exe)
        {
            WorkingDirectory = Path.GetDirectoryName(_exe)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(document);
        start.Environment["BROADSIDE_WINUI_TRACE"] = "1"; // the sample logs the pointer events it gets: a timed-out wait prints them

        _process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {_exe}.");
        _processId = _process.Id;
        _process.OutputDataReceived += (_, e) => Capture("stdout", e.Data);
        _process.ErrorDataReceived += (_, e) => Capture("stderr", e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        try
        {
            // Wait for the window here rather than in FlaUI, which only reports "Could not find process" when the app has exited.
            bool shown = Poll(() =>
            {
                _process.Refresh();
                return _process.HasExited || _process.MainWindowHandle != IntPtr.Zero;
            });
            if (_process.HasExited || !shown)
            {
                throw new InvalidOperationException(_process.HasExited ? "The sample exited before opening its window." : "The sample's window did not open.");
            }

            _app = FlaUI.Core.Application.Attach(_process);
            Window = _app.GetMainWindow(Automation, Timeout) ?? throw new InvalidOperationException("The sample's window did not open.");
            Viewer = WaitFor(() => Window.FindFirstDescendant(cf => cf.ByAutomationId("Viewer")), "the viewer");
        }
        catch (Exception e)
        {
            string diagnostics = Diagnostics();
            Stop();
            _process.Dispose();
            Automation.Dispose();
            throw new InvalidOperationException($"{e.Message}{Environment.NewLine}{diagnostics}", e);
        }
    }

    public string Document { get; }

    public UIA3Automation Automation { get; } = new();

    public Window Window { get; }

    public AutomationElement Viewer { get; }

    /// <summary>A file of the repository's hand-written corpus (tests/Corpus), found above the test's output folder.</summary>
    public static string CorpusFile(string name)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "Corpus", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"tests/Corpus/{name} is not above {AppContext.BaseDirectory}.");
    }

    /// <summary>The documents the sample copies next to itself (its output's documents folder).</summary>
    public static string DocumentPath(string name) => Path.Combine(Path.GetDirectoryName(FindSample())!, "documents", name);

    /// <summary>Launches the sample on a document and waits until page 1 is drawn.</summary>
    /// <param name="name">A document the sample carries, or a full path.</param>
    public static SampleApp Open(string name)
    {
        var app = new SampleApp(Path.IsPathRooted(name) ? name : DocumentPath(name));
        try
        {
            app.WaitForState(state => state.PageCount > 0 && state.Activity == "Ready", "page 1 drawn");
            return app;
        }
        catch
        {
            app.Dispose();
            throw;
        }
    }

    public AutomationElement Find(string automationId) =>
        WaitFor(() => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), automationId);

    public void Click(string automationId) => Find(automationId).AsButton().Invoke();

    /// <summary>Flips a toggle button (UIA's Toggle pattern: a ToggleButton has no Invoke pattern).</summary>
    public void Toggle(string automationId) => Find(automationId).AsToggleButton().Toggle();

    /// <summary>The status line ("Page 3 of 8 | Zoom 125% | Ready").</summary>
    public Status ReadStatus()
    {
        string text = Find("Status").Name;
        Match match = StatusPattern().Match(text);
        return match.Success
            ? new Status(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                match.Groups[4].Value,
                text)
            : new Status(0, 0, 0, string.Empty, text);
    }

    public Status WaitForState(Func<Status, bool> done, string what)
    {
        Status last = default;
        bool ok = Poll(() =>
        {
            last = ReadStatus();
            Assert.False(last.Text.StartsWith("Error", StringComparison.Ordinal), last.Text);
            return done(last);
        });
        if (!ok)
        {
            // Built only on a timeout: the diagnostics wait for the event log and read the process.
            Assert.Fail($"Timed out waiting for {what}; status: '{last.Text}'.{Environment.NewLine}{Diagnostics()}");
        }

        return last;
    }

    public string WaitForText(string automationId, Func<string, bool> done, string what)
    {
        string last = string.Empty;
        bool ok = Poll(() => done(last = Find(automationId).Name));
        if (!ok)
        {
            Assert.Fail($"Timed out waiting for {what}; {automationId}: '{last}'.{Environment.NewLine}{Diagnostics()}");
        }

        return last;
    }

    public static T WaitFor<T>(Func<T?> probe, string what)
        where T : class
    {
        T? found = null;
        bool ok = Poll(() => (found = probe()) is not null);
        Assert.True(ok, $"Timed out waiting for {what}.");
        return found!;
    }

    public static bool Poll(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < Timeout)
        {
            try
            {
                if (done())
                {
                    return true;
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // The element went away while it was read (the tree changed): read again.
            }

            Thread.Sleep(100);
        }

        return false;
    }

    public void Dispose()
    {
        // Cleanup never throws: an exception here would replace the test's own failure in the report.
        bool exited = Exited();
        if (exited && !_closing)
        {
            // An app that went away during the test (rather than being closed here) says why in the test's output.
            TestContext.Current.TestOutputHelper?.WriteLine("The sample exited during the test." + Environment.NewLine + Diagnostics());
        }

        try
        {
            _closing = true;
            if (!exited)
            {
                try
                {
                    _app.Close();
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
                {
                    // The window or the process went away first; Stop ends what is left.
                }
            }

            Stop();
        }
        finally
        {
            _app.Dispose();
            _process.Dispose();
            Automation.Dispose();
        }
    }

    // Process.HasExited throws once the Process object no longer tracks the process (FlaUI shares it); ask by id then.
    private bool Exited()
    {
        try
        {
            return _process.HasExited;
        }
        catch (InvalidOperationException)
        {
            try
            {
                using Process current = Process.GetProcessById(_processId);
                return current.HasExited;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                return true;
            }
        }
    }

    private void Capture(string stream, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.Append(stream).Append(": ").AppendLine(line);
        }
    }

    private void Stop()
    {
        _closing = true;
        if (Exited())
        {
            return;
        }

        try
        {
            using Process current = Process.GetProcessById(_processId);
            if (!current.WaitForExit(TimeSpan.FromSeconds(5)))
            {
                current.Kill(entireProcessTree: true);
                current.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    /// <summary>
    /// What the sample left behind: exit code, stdout and stderr, its startup log lines (Broadside.Viewer.WinUI.Sample.log next to the
    /// exe, this process's lines) and the crash entries (.NET Runtime, Application Error, Windows Error Reporting, SideBySide) the
    /// Application event log recorded since the launch.
    /// </summary>
    private string Diagnostics()
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"--- sample: {_exe} (pid {_processId}), {_sinceStart.Elapsed.TotalSeconds:0.0} s since launch");
        if (Exited())
        {
            try
            {
                _process.WaitForExit(); // Drains the redirected output.
                int code = _process.ExitCode;
                report.AppendLine(CultureInfo.InvariantCulture, $"--- exit code: {code} (0x{code:X8}){(_closing ? ", after the test closed it" : string.Empty)}");
            }
            catch (InvalidOperationException)
            {
                report.AppendLine("--- exited (exit code unavailable)");
            }
        }
        else
        {
            report.AppendLine("--- still running");
        }

        lock (_output)
        {
            report.AppendLine("--- stdout/stderr:").Append(_output.Length == 0 ? "(none)" + Environment.NewLine : _output.ToString());
        }

        string log = Path.Combine(Path.GetDirectoryName(_exe)!, "Broadside.Viewer.WinUI.Sample.log");
        string marker = string.Create(CultureInfo.InvariantCulture, $"[{_processId}]");
        report.AppendLine(CultureInfo.InvariantCulture, $"--- {log}:");
        try
        {
            string[] lines = File.Exists(log) ? [.. File.ReadAllLines(log).Where(l => l.Contains(marker, StringComparison.Ordinal))] : [];
            report.AppendLine(lines.Length == 0 ? "(no lines for this process)" : string.Join(Environment.NewLine, lines));
        }
        catch (IOException e)
        {
            report.AppendLine("(unreadable: " + e.Message + ")");
        }

        report.AppendLine("--- Application event log since the launch:").AppendLine(ApplicationEvents(_sinceStart.Elapsed + TimeSpan.FromSeconds(10)));
        return report.ToString();
    }

    // Windows Error Reporting writes its entries a moment after the crash: wait briefly, then ask PowerShell (always on Windows).
    private static string ApplicationEvents(TimeSpan since)
    {
        Thread.Sleep(TimeSpan.FromSeconds(3));
        string script = string.Create(
            CultureInfo.InvariantCulture,
            $"Get-WinEvent -FilterHashtable @{{LogName='Application'; StartTime=(Get-Date).AddSeconds(-{Math.Ceiling(since.TotalSeconds + 3)})}} -ErrorAction SilentlyContinue | Where-Object {{ $_.ProviderName -in '.NET Runtime','Application Error','Windows Error Reporting','Application Hang','SideBySide' }} | Format-List TimeCreated,ProviderName,Id,LevelDisplayName,Message | Out-String -Width 400");
        try
        {
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add(script);
            using Process shell = Process.Start(start)!;
            Task<string> output = shell.StandardOutput.ReadToEndAsync();
            Task<string> errors = shell.StandardError.ReadToEndAsync();
            if (!shell.WaitForExit(TimeSpan.FromSeconds(60)))
            {
                shell.Kill();
                return "(Get-WinEvent timed out)";
            }

            string text = (output.Result + errors.Result).Trim();
            return text.Length == 0 ? "(no matching entries)" : text;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return "(could not run PowerShell: " + e.Message + ")";
        }
    }

    /// <summary>The sample's exe: BROADSIDE_WINUI_SAMPLE, else the newest build under the repository's artifacts folder.</summary>
    private static string FindSample()
    {
        if (Environment.GetEnvironmentVariable("BROADSIDE_WINUI_SAMPLE") is { Length: > 0 } configured)
        {
            return Path.GetFullPath(configured);
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
        {
            directory = directory.Parent;
        }

        string artifacts = Path.Combine(directory?.FullName ?? ".", "artifacts", "bin", "Broadside.Viewer.WinUI.Sample");
        FileInfo? exe = Directory.Exists(artifacts)
            ? new DirectoryInfo(artifacts).EnumerateFiles("Broadside.Viewer.WinUI.Sample.exe", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;
        return exe?.FullName ?? throw new InvalidOperationException(
            "Build samples/Broadside.Viewer.WinUI.Sample first, or set BROADSIDE_WINUI_SAMPLE to its exe.");
    }

    [GeneratedRegex(@"Page (\d+) of (\d+) \| Zoom (\d+)% \| (\w+)")]
    private static partial Regex StatusPattern();

    /// <summary>The sample's status line, parsed.</summary>
    internal readonly record struct Status(int Page, int PageCount, int Zoom, string Activity, string Text);
}
