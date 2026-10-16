using System.Diagnostics;
using System.Globalization;
using Broadside.GoldenImages;
using Broadside.Rendering.Android.Device;
using Broadside.TestSupport;

namespace Broadside.Rendering.Android.Tests;

/// <summary>
/// One run of the device app's instrumentation over <see cref="AndroidBackendTests.Files"/>, shared by every test: it installs the
/// built APK on the attached device or emulator over adb, renders the files there, and reads back the results and the pixels.
/// </summary>
/// <remarks>
/// The pixels come back in one <c>adb pull</c> of the output directory, retried after reconnecting: an emulator's adb transport can
/// drop to "device offline" during a long transfer, which one <c>exec-out</c> per file turned into dozens of failed tests.
/// </remarks>
/// <remarks>
/// Where it finds things: adb in <c>ANDROID_HOME</c> or <c>ANDROID_SDK_ROOT</c> (<c>platform-tools</c>), the default SDK location on
/// macOS, or the <c>PATH</c>; the device named by <c>ANDROID_SERIAL</c>, else the first one <c>adb devices</c> lists; the APK named by
/// <c>BROADSIDE_ANDROID_APK</c>, else the newest <c>*-Signed.apk</c> under <c>artifacts/bin/Broadside.Rendering.Android.Device/</c>
/// (build it with <c>dotnet build tests/Broadside.Rendering.Android.Device -c Release</c>; a Debug build skips the allocation tests). When one is missing the tests skip, unless
/// <c>BROADSIDE_ANDROID_REQUIRED</c> is <c>1</c>.
/// </remarks>
internal sealed class AndroidDevice
{
    /// <summary>The environment variable that turns a missing device, adb or APK into a failure rather than a skip.</summary>
    public const string RequiredVariable = "BROADSIDE_ANDROID_REQUIRED";

    /// <summary>Replays per page for the allocation and time measurement.</summary>
    private const int Replays = 20;

    private const int PullAttempts = 3;

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private static readonly Lazy<AndroidDevice> Shared = new(Start);

    private readonly Dictionary<string, string> _results;
    private readonly string _pixels;

    private AndroidDevice(string? unavailable, Dictionary<string, string>? results = null, string pixels = "")
    {
        Unavailable = unavailable;
        _results = results ?? [];
        _pixels = pixels;
    }

    /// <summary>Gets why no device run could be made, or <see langword="null"/> when it was.</summary>
    public string? Unavailable { get; }

    /// <summary>Returns the shared run, starting it on first use; skips the calling test (or fails it, when required) if there is none.</summary>
    public static AndroidDevice Run()
    {
        AndroidDevice device = Shared.Value;
        if (device.Unavailable is { } reason)
        {
            if (Environment.GetEnvironmentVariable(RequiredVariable) == "1")
            {
                throw new InvalidOperationException(reason);
            }

            Assert.Skip(reason);
        }

        return device;
    }

    /// <summary>
    /// Returns the device's render of a page in a variant (<see cref="DeviceProtocol.Native"/>, <see cref="DeviceProtocol.Outlines"/>
    /// or <see cref="DeviceProtocol.Paths"/>), as pulled from the app's external files directory.
    /// </summary>
    public GoldenImage Render(string fileName, int page, string variant = DeviceProtocol.Native)
    {
        string[] size = Result(DeviceProtocol.SizeKey(fileName, page)).Split(' ');
        int width = int.Parse(size[0], CultureInfo.InvariantCulture);
        int height = int.Parse(size[1], CultureInfo.InvariantCulture);
        string path = Path.Combine(_pixels, DeviceProtocol.PixelFile(fileName, page, variant));
        Assert.True(File.Exists(path), $"The device wrote no {DeviceProtocol.PixelFile(fileName, page, variant)}.");
        byte[] pixels = File.ReadAllBytes(path);
        Assert.True(pixels.Length == width * height * 4, $"{path} holds {pixels.Length} bytes; a {width} x {height} render needs {width * height * 4}.");
        return GoldenImage.FromPixels(pixels, width, height, width * 4, GoldenPixelFormat.Bgra8);
    }

    /// <summary>Returns the diagnostic codes the backend reported for a render variant of the page.</summary>
    public string[] Diagnostics(string fileName, int page, string variant = DeviceProtocol.Native) =>
        Result(DeviceProtocol.DiagnosticsKey(fileName, page, variant)).Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Returns how many glyphs a render variant of the page drew through Android's font engine.</summary>
    public int NativeGlyphs(string fileName, int page, string variant = DeviceProtocol.Native) =>
        int.Parse(Result(DeviceProtocol.NativeGlyphsKey(fileName, page, variant)), CultureInfo.InvariantCulture);

    /// <summary>Returns how many Android fonts a fresh backend built while rendering every page of the file twice.</summary>
    public int FontsCreated(string fileName) => int.Parse(Result(DeviceProtocol.FontsKey(fileName)), CultureInfo.InvariantCulture);

    /// <summary>Returns how many Android bitmaps a fresh backend made from the file's images and masks after rendering every page <paramref name="pass"/> times.</summary>
    public int ImagesCreated(string fileName, int pass, string variant = DeviceProtocol.Native) =>
        int.Parse(Result(DeviceProtocol.ImagesKey(fileName, pass, variant)), CultureInfo.InvariantCulture);

    /// <summary>Returns how many pattern cells a fresh backend rendered into tiles after rendering every page <paramref name="pass"/> times.</summary>
    public int CellsRecorded(string fileName, int pass) => int.Parse(Result(DeviceProtocol.CellsKey(fileName, pass)), CultureInfo.InvariantCulture);

    /// <summary>Returns the bytes of converted pixels the backend kept after rendering every page of the file.</summary>
    public long CachedImageBytes(string fileName, string variant = DeviceProtocol.Native) =>
        long.Parse(Result(DeviceProtocol.CachedBytesKey(fileName, variant)), CultureInfo.InvariantCulture);

    /// <summary>Returns why the device's path reference did not draw the page, or <see langword="null"/> when it did.</summary>
    public string? PathReference(string fileName, int page) =>
        Result(DeviceProtocol.PathsKey(fileName, page)) is var answer && answer == "yes" ? null : answer;

    /// <summary>Returns the fewest bytes one warm replay of the page allocated on the device, with native glyphs or forced outlines.</summary>
    public long ReplayAllocatedBytes(string fileName, int page, string variant = DeviceProtocol.Native) =>
        long.Parse(Result(DeviceProtocol.AllocatedKey(fileName, page, variant)), CultureInfo.InvariantCulture);

    /// <summary>Returns what the device's measurement reports for one 64-byte array: above zero when the runtime counts allocations.</summary>
    /// <summary>Gets whether the device app was built optimized, the only build whose allocation measurement means anything.</summary>
    public bool IsOptimizedBuild => Result(DeviceProtocol.BuildKey) == DeviceProtocol.Optimized;

    public long AllocationProbeBytes() => long.Parse(Result(DeviceProtocol.AllocationProbeKey), CultureInfo.InvariantCulture);

    /// <summary>Returns the mean time of one warm replay of the page on the device, in microseconds.</summary>
    public double ReplayMicroseconds(string fileName, int page, string variant = DeviceProtocol.Native) =>
        double.Parse(Result(DeviceProtocol.ReplayTimeKey(fileName, page, variant)), CultureInfo.InvariantCulture);

    private string Result(string key) =>
        _results.TryGetValue(key, out string? value) ? value : throw new InvalidOperationException($"The instrumentation reported no '{key}'.");

    private static AndroidDevice Start()
    {
        string? adb = FindAdb();
        if (adb is null)
        {
            return new AndroidDevice("adb was not found (set ANDROID_HOME or put platform-tools on the PATH).");
        }

        string? serial = FindDevice(adb);
        if (serial is null)
        {
            return new AndroidDevice("No Android device or emulator is attached (adb devices lists none).");
        }

        string? apk = FindApk();
        if (apk is null)
        {
            return new AndroidDevice("The device app is not built: run 'dotnet build tests/Broadside.Rendering.Android.Device' or set BROADSIDE_ANDROID_APK.");
        }

        Check(RunAdb(adb, ["-s", serial, "install", "-r", "-t", apk]), "install");
        Check(RunAdb(adb, ["-s", serial, "shell", "rm", "-rf", $"{DeviceProtocol.DeviceFilesDirectory}/{DeviceProtocol.OutputDirectory}"]), "clear");

        (byte[] output, int exitCode) = RunAdb(adb, [
            "-s", serial, "shell", "am", "instrument", "-w",
            "-e", DeviceProtocol.FilesArgument, string.Join(',', AndroidBackendTests.Files),
            "-e", DeviceProtocol.UncachedArgument, string.Join(',', AndroidBackendTests.UncachedFiles),
            "-e", DeviceProtocol.ReplaysArgument, Replays.ToString(CultureInfo.InvariantCulture),
            $"{DeviceProtocol.Package}/{DeviceProtocol.Instrumentation}",
        ]);
        string text = System.Text.Encoding.UTF8.GetString(output);
        Dictionary<string, string> results = ParseResults(text);
        if (exitCode != 0 || !text.Contains("INSTRUMENTATION_CODE: -1", StringComparison.Ordinal) || results.ContainsKey(DeviceProtocol.ErrorKey))
        {
            throw new InvalidOperationException($"The instrumentation failed on {serial}:{Environment.NewLine}{text}");
        }

        return new AndroidDevice(null, results, Pull(adb, serial));
    }

    /// <summary>
    /// Copies the app's output directory to a new local directory and returns the copy, reconnecting and retrying when the transfer
    /// fails.
    /// </summary>
    private static string Pull(string adb, string serial)
    {
        string remote = $"{DeviceProtocol.DeviceFilesDirectory}/{DeviceProtocol.OutputDirectory}";
        (byte[] Output, int ExitCode) result = default;
        for (int attempt = 0; attempt < PullAttempts; attempt++)
        {
            if (attempt > 0)
            {
                RunAdb(adb, ["reconnect", "offline"]);
                RunAdb(adb, ["-s", serial, "wait-for-device"]);
            }

            string local = Directory.CreateTempSubdirectory("broadside-android-").FullName;
            result = RunAdb(adb, ["-s", serial, "pull", remote, local]);
            if (result.ExitCode == 0)
            {
                return Path.Combine(local, DeviceProtocol.OutputDirectory);
            }
        }

        Check(result, "pull");
        throw new UnreachableException();
    }

    /// <summary>Reads the <c>INSTRUMENTATION_RESULT: key=value</c> lines <c>am instrument</c> prints for the finishing bundle.</summary>
    private static Dictionary<string, string> ParseResults(string text)
    {
        const string prefix = "INSTRUMENTATION_RESULT: ";
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        string? key = null;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            int equals;
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal) && (equals = trimmed.IndexOf('=', prefix.Length)) > 0)
            {
                key = trimmed[prefix.Length..equals];
                results[key] = trimmed[(equals + 1)..];
            }
            else if (key == DeviceProtocol.ErrorKey && !trimmed.StartsWith("INSTRUMENTATION_", StringComparison.Ordinal))
            {
                results[key] += Environment.NewLine + trimmed;
            }
        }

        return results;
    }

    private static string? FindAdb()
    {
        string executable = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        List<string> candidates = [];
        foreach (string variable in (string[])["ANDROID_HOME", "ANDROID_SDK_ROOT"])
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } sdk)
            {
                candidates.Add(Path.Combine(sdk, "platform-tools", executable));
            }
        }

        if (OperatingSystem.IsMacOS())
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Android", "sdk", "platform-tools", executable));
        }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (directory.Length > 0)
            {
                candidates.Add(Path.Combine(directory, executable));
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindDevice(string adb)
    {
        if (Environment.GetEnvironmentVariable("ANDROID_SERIAL") is { Length: > 0 } serial)
        {
            return serial;
        }

        (byte[] output, int exitCode) = RunAdb(adb, ["devices"]);
        if (exitCode != 0)
        {
            return null;
        }

        foreach (string line in System.Text.Encoding.UTF8.GetString(output).Split('\n').Skip(1))
        {
            string[] columns = line.Trim().Split('\t');
            if (columns is [string device, "device"])
            {
                return device;
            }
        }

        return null;
    }

    private static string? FindApk()
    {
        if (Environment.GetEnvironmentVariable("BROADSIDE_ANDROID_APK") is { Length: > 0 } apk)
        {
            return File.Exists(apk) ? apk : null;
        }

        string output = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "..", "artifacts", "bin", "Broadside.Rendering.Android.Device"));
        return Directory.Exists(output)
            ? Directory.EnumerateFiles(output, "*-Signed.apk", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
    }

    private static void Check((byte[] Output, int ExitCode) result, string step)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"adb {step} failed ({result.ExitCode}): {System.Text.Encoding.UTF8.GetString(result.Output)}");
        }
    }

    private static (byte[] Output, int ExitCode) RunAdb(string adb, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(adb)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("adb did not start.");
        Stream stdout = process.StandardOutput.BaseStream;
        Task<byte[]> output = Task.Run(() =>
        {
            var buffer = new MemoryStream();
            stdout.CopyTo(buffer);
            return buffer.ToArray();
        });
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout) || !output.Wait(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"adb {string.Join(' ', arguments)} did not finish within {Timeout}.");
        }

        byte[] bytes = output.GetAwaiter().GetResult();
        string errorText = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            bytes = [.. bytes, .. System.Text.Encoding.UTF8.GetBytes(errorText)];
        }

        return (bytes, process.ExitCode);
    }
}
