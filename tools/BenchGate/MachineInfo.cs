using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broadside.Tools.BenchGate;

/// <summary>Which timing pass a runner's rounds belong to.</summary>
public enum TimePass
{
    /// <summary>The runtime's default instruction sets: AVX-512 when the runner's processor has it.</summary>
    Default,

    /// <summary>
    /// AVX-512 disabled in the runtime (<c>DOTNET_EnableAVX512F=0</c>, <c>DOTNET_EnableAVX512=0</c>), so the JIT has the 16 vector
    /// registers of a processor without it instead of 32 (issue #539).
    /// </summary>
    Avx512Off,
}

/// <summary>
/// The processor a gate job measured on and what the measuring processes saw of AVX-512 (issue #539). <c>bench/run-gate.sh</c>
/// writes it as <c>machine.json</c> next to its <c>round-&lt;n&gt;</c>, <c>confirm</c> and <c>allocations</c> folders, through
/// <c>BenchGate machine</c> run under the same runtime settings as the benchmarks.
/// </summary>
/// <param name="Cpu">The processor's model name.</param>
/// <param name="Avx512Hardware">True when the processor reports AVX-512 Foundation (<c>avx512f</c> in <c>/proc/cpuinfo</c> on
/// Linux); null when unknown. A hypervisor can hide it, in which case nothing on that runner uses it either.</param>
/// <param name="Avx512Runtime">True when .NET used AVX-512 in the measuring processes (<c>Avx512F.IsSupported</c> under the
/// gate's runtime settings).</param>
/// <param name="Avx512Disabled">True when the gate disabled AVX-512 for this job's timings (the <see cref="TimePass.Avx512Off"/> pass).</param>
/// <param name="Os">The operating system.</param>
/// <param name="Architecture">The process architecture.</param>
public sealed record MachineInfo(string Cpu, bool? Avx512Hardware, bool Avx512Runtime, bool Avx512Disabled, string Os, string Architecture)
{
    /// <summary>The file name <c>bench/run-gate.sh</c> writes.</summary>
    public const string FileName = "machine.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, IndentSize = 2 };

    /// <summary>The timing pass this machine's rounds belong to.</summary>
    public TimePass Pass => Avx512Disabled ? TimePass.Avx512Off : TimePass.Default;

    /// <summary>One phrase for people: the processor and what the timings used, e.g. "INTEL XEON PLATINUM 8573C, AVX-512 disabled".</summary>
    public string Describe() => $"{Cpu}, {Avx512State()}";

    /// <summary>What the measuring processes used of AVX-512, in words.</summary>
    public string Avx512State()
    {
        if (Avx512Disabled)
        {
            return Avx512Runtime ? "AVX-512 still on (disabling it failed)"
                : Avx512Hardware == true ? "AVX-512 disabled"
                : "no AVX-512 (nothing to disable)";
        }

        return Avx512Runtime ? "AVX-512 on"
            : Avx512Hardware == true ? "AVX-512 not used by .NET"
            : "no AVX-512";
    }

    /// <summary>The machine this process runs on, with AVX-512 as this process (and so the benchmarks run under the same settings) sees it.</summary>
    public static MachineInfo Detect()
    {
        string? cpu = null;
        bool? hardware = null;
        if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
        {
            (cpu, hardware) = ParseCpuInfo(File.ReadAllText("/proc/cpuinfo"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            cpu = Sysctl("machdep.cpu.brand_string");
            hardware = RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64
                && Sysctl("hw.optional.avx512f") == "1";
        }
        else if (OperatingSystem.IsWindows())
        {
            cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        }

        bool disabled = IsOff("DOTNET_EnableAVX512F") || IsOff("DOTNET_EnableAVX512");
        bool runtime = Avx512F.IsSupported;
        if (hardware is null && (runtime || !disabled))
        {
            hardware = runtime;
        }

        return new MachineInfo(
            string.IsNullOrWhiteSpace(cpu) ? "unknown processor" : cpu.Trim(),
            hardware,
            runtime,
            disabled,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
    }

    /// <summary>The model name and the <c>avx512f</c> flag of the first processor in a <c>/proc/cpuinfo</c> text.</summary>
    public static (string? Model, bool? Avx512) ParseCpuInfo(string text)
    {
        string? model = null;
        bool? avx512 = null;
        foreach (string line in text.Split('\n'))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (model is null && key == "model name")
            {
                model = value;
            }
            else if (avx512 is null && key == "flags")
            {
                avx512 = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("avx512f", StringComparer.Ordinal);
            }
        }

        return (model, avx512);
    }

    /// <summary>Reads a <see cref="FileName"/> file.</summary>
    public static MachineInfo Load(string path)
    {
        JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
        if (root is not JsonObject machine || machine["cpu"] is not JsonValue cpu)
        {
            throw new InvalidDataException($"{path}: expected an object with a \"cpu\" string.");
        }

        return new MachineInfo(
            cpu.GetValue<string>(),
            machine["avx512Hardware"] is JsonValue hardware ? hardware.GetValue<bool>() : null,
            machine["avx512Runtime"]?.GetValue<bool>() ?? false,
            machine["avx512Disabled"]?.GetValue<bool>() ?? false,
            machine["os"]?.GetValue<string>() ?? string.Empty,
            machine["architecture"]?.GetValue<string>() ?? string.Empty);
    }

    /// <summary>Writes this machine as a <see cref="FileName"/> file.</summary>
    public void Save(string path)
    {
        var machine = new JsonObject
        {
            ["cpu"] = Cpu,
            ["avx512Hardware"] = Avx512Hardware,
            ["avx512Runtime"] = Avx512Runtime,
            ["avx512Disabled"] = Avx512Disabled,
            ["os"] = Os,
            ["architecture"] = Architecture,
        };
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, machine.ToJsonString(Options) + Environment.NewLine);
    }

    private static bool IsOff(string variable) =>
        Environment.GetEnvironmentVariable(variable) is string value
        && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int number)
        && number == 0;

    private static string? Sysctl(string name)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sysctl", ["-n", name]) { RedirectStandardOutput = true, RedirectStandardError = true });
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
