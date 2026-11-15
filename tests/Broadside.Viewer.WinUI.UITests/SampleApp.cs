using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Broadside.Viewer.WinUI.UITests;

/// <summary>The sample app running one document, driven through UI Automation (FlaUI, UIA3).</summary>
internal sealed partial class SampleApp : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private readonly FlaUI.Core.Application _app;

    private SampleApp(string document)
    {
        Document = document;
        string exe = FindSample();
        var start = new ProcessStartInfo(exe, $"\"{document}\"") { WorkingDirectory = Path.GetDirectoryName(exe)! };
        _app = FlaUI.Core.Application.Launch(start);
        Window = _app.GetMainWindow(Automation, Timeout) ?? throw new InvalidOperationException("The sample's window did not open.");
        Viewer = WaitFor(() => Window.FindFirstDescendant(cf => cf.ByAutomationId("Viewer")), "the viewer");
    }

    public string Document { get; }

    public UIA3Automation Automation { get; } = new();

    public Window Window { get; }

    public AutomationElement Viewer { get; }

    /// <summary>The documents the sample copies next to itself (its output's documents folder).</summary>
    public static string DocumentPath(string name) => Path.Combine(Path.GetDirectoryName(FindSample())!, "documents", name);

    /// <summary>Launches the sample on a document and waits until page 1 is drawn.</summary>
    public static SampleApp Open(string name)
    {
        var app = new SampleApp(DocumentPath(name));
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
        Assert.True(ok, $"Timed out waiting for {what}; status: '{last.Text}'.");
        return last;
    }

    public string WaitForText(string automationId, Func<string, bool> done, string what)
    {
        string last = string.Empty;
        bool ok = Poll(() => done(last = Find(automationId).Name));
        Assert.True(ok, $"Timed out waiting for {what}; {automationId}: '{last}'.");
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
        try
        {
            _app.Close();
            if (!_app.HasExited)
            {
                _app.Kill();
            }
        }
        finally
        {
            _app.Dispose();
            Automation.Dispose();
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
