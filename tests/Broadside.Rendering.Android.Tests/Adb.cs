using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Broadside.Rendering.Android.Tests;

/// <summary>
/// Runs adb against one device and keeps the transport usable: before each phase it waits until the device is attached and booted,
/// and when a transport-only step fails it reconnects, waits again and retries. Every command and its outcome goes to a log file
/// (<see cref="LogPath"/>) that the CI job uploads with the test results.
/// </summary>
/// <remarks>
/// An emulator on a hosted runner drops to "device offline" now and then (issue #344); the device comes back within seconds, but
/// a command issued meanwhile fails. Steps that only move data (install, clear, pull) are retried; whether the instrumentation was
/// cut off by the transport or failed on the device is decided by its output (see <see cref="AndroidDevice"/>).
/// </remarks>
internal sealed class Adb
{
    /// <summary>The text every <see cref="AdbTransportLostException"/> message starts with; the CI job retries the test step only on it.</summary>
    public const string TransportLostMarker = "adb transport lost";

    private const int Attempts = 5;

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    private readonly string _adb;
    private readonly string _serial;
    private readonly Lock _logLock = new();

    public Adb(string adb, string serial, string logPath)
    {
        _adb = adb;
        _serial = serial;
        LogPath = logPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
    }

    /// <summary>Gets the file the adb commands and their outcomes are written to.</summary>
    public string LogPath { get; }

    /// <summary>Runs an adb command for the device (the serial is added) and returns what it printed and its exit code.</summary>
    public (byte[] Output, int ExitCode) Device(IReadOnlyList<string> arguments, TimeSpan? timeout = null) =>
        Run(["-s", _serial, .. arguments], timeout ?? CommandTimeout);

    /// <summary>
    /// Runs a step that only moves data and can safely run again, recovering the transport and retrying when it fails because of the
    /// transport; throws <see cref="AdbTransportLostException"/> when every attempt failed that way, and
    /// <see cref="InvalidOperationException"/> at once when the device answered with an error of its own (a full disk, a bad APK).
    /// </summary>
    public (byte[] Output, int ExitCode) Retry(string step, IReadOnlyList<string> arguments)
    {
        (byte[] Output, int ExitCode) result = ([], 0);
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            if (attempt > 1)
            {
                Recover(step, attempt);
            }

            result = Device(arguments);
            if (result.ExitCode == 0)
            {
                return result;
            }

            string text = Encoding.UTF8.GetString(result.Output);
            bool transport = result.ExitCode == -1 || LooksLikeTransport(text) || !IsOnline();
            Log($"{step}: attempt {attempt} failed ({result.ExitCode}, {(transport ? "transport" : "device error")}): {Tail(result.Output)}");
            if (!transport)
            {
                throw new InvalidOperationException($"adb {step} failed ({result.ExitCode}): {text}");
            }
        }

        throw new AdbTransportLostException($"{TransportLostMarker}: adb {step} failed {Attempts} times ({result.ExitCode}): {Tail(result.Output)} (log: {LogPath})");
    }

    /// <summary>
    /// Returns whether a failed command's output points at the transport: a message adb prints when the device or the connection went
    /// away, or no error text at all (a transfer cut off mid-progress prints only its progress).
    /// </summary>
    internal static bool LooksLikeTransport(string output)
    {
        foreach (string sign in (string[])["offline", "not found", "no devices", "closed", "protocol fault", "connection reset", "broken pipe", "failed to read", "failed to write", "unexpected eof"])
        {
            if (output.Contains(sign, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (string sign in (string[])["failure", "exception", "error", "denied", "no such file", "no space"])
        {
            if (output.Contains(sign, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsOnline()
    {
        (byte[] output, int exitCode) = Device(["get-state"], ProbeTimeout);
        return exitCode == 0 && Encoding.UTF8.GetString(output).Trim() == "device";
    }

    /// <summary>Waits until the device is attached, answers a shell command and has finished booting; recovers it when it does not.</summary>
    public void EnsureReady(string phase)
    {
        Log($"{phase}: checking the device");
        if (IsReady())
        {
            return;
        }

        for (int attempt = 2; attempt <= Attempts; attempt++)
        {
            Recover(phase, attempt);
            if (IsReady())
            {
                return;
            }
        }

        throw new AdbTransportLostException($"{TransportLostMarker}: {_serial} was not ready before {phase} (log: {LogPath})");
    }

    /// <summary>Reconnects an offline device (restarting the adb server on later attempts), backs off, and waits for it to be ready.</summary>
    public void Recover(string phase, int attempt)
    {
        Log($"{phase}: recovering before attempt {attempt}; state {State()}");
        Run(["reconnect", "offline"], ProbeTimeout);
        if (attempt >= 3)
        {
            Run(["kill-server"], ProbeTimeout);
            Run(["start-server"], ProbeTimeout);
        }

        Thread.Sleep(TimeSpan.FromSeconds(Math.Min(2 << (attempt - 2), 16)));
        WaitReady();
    }

    /// <summary>Appends a timestamped line to the log.</summary>
    public void Log(string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:HH:mm:ss.fff} {message}{Environment.NewLine}");
        lock (_logLock)
        {
            File.AppendAllText(LogPath, line);
        }
    }

    private bool IsReady()
    {
        // Twice, a second apart: a transport that is flapping often answers once and then drops again.
        for (int check = 0; check < 2; check++)
        {
            if (check > 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }

            (byte[] output, int exitCode) = Device(["shell", "getprop", "sys.boot_completed"], ProbeTimeout);
            if (exitCode != 0 || Encoding.UTF8.GetString(output).Trim() != "1")
            {
                Log($"not ready ({exitCode}): {Tail(output)}");
                return false;
            }
        }

        return true;
    }

    private void WaitReady()
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < ReadyTimeout)
        {
            if (Device(["wait-for-device"], ProbeTimeout).ExitCode == 0 && IsReady())
            {
                Log($"ready after {clock.Elapsed.TotalSeconds:F1} s");
                return;
            }

            Thread.Sleep(TimeSpan.FromSeconds(2));
        }

        Log($"still not ready after {clock.Elapsed.TotalSeconds:F1} s; state {State()}");
    }

    private string State()
    {
        (byte[] output, int exitCode) = Run(["devices", "-l"], ProbeTimeout);
        return $"({exitCode}) {Encoding.UTF8.GetString(output).ReplaceLineEndings(" | ").Trim()}";
    }

    private (byte[] Output, int ExitCode) Run(IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        (byte[] Output, int ExitCode) result = AndroidDevice.RunAdb(_adb, arguments, timeout);
        string command = string.Join(' ', arguments.Count > 12 ? [.. arguments.Take(12), $"... ({arguments.Count} arguments)"] : arguments);
        Log($"adb {command} -> {result.ExitCode} in {clock.Elapsed.TotalSeconds:F1} s");
        return result;
    }

    private static string Tail(byte[] output)
    {
        string text = Encoding.UTF8.GetString(output).ReplaceLineEndings(" ").Trim();
        return text.Length <= 400 ? text : "..." + text[^400..];
    }
}

/// <summary>Thrown when adb could not reach the device after reconnecting and retrying: the emulator's transport, not the code under test, failed.</summary>
internal sealed class AdbTransportLostException : InvalidOperationException
{
    public AdbTransportLostException()
    {
    }

    public AdbTransportLostException(string message)
        : base(message)
    {
    }

    public AdbTransportLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
