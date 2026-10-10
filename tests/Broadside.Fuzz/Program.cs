using System.Globalization;
using SharpFuzz;

namespace Broadside.Fuzz;

/// <summary>
/// Entry point of the fuzz harness. The first argument selects the mode, the second the target (see <see cref="FuzzTargets"/>):
/// <list type="bullet">
/// <item><c>--fuzz [target]</c>: run under libFuzzer through the libfuzzer-dotnet driver. The driver passes at most one argument
/// (with <c>--target_path=dotnet</c> that is the harness DLL, and the mode is implied), so the target may also come from the
/// <c>BROADSIDE_FUZZ_TARGET</c> environment variable.</item>
/// <item><c>--afl &lt;target&gt;</c>: run under afl-fuzz (SharpFuzz's AFL fork-server mode).</item>
/// <item><c>--smoke &lt;target&gt; [seconds] [seed]</c>: no fuzzer needed; corpus files plus random mutations for the given time (default 10 s).</item>
/// <item><c>--run &lt;target&gt; &lt;file&gt;</c>: run the target once on one input, to replay a finding.</item>
/// <item><c>--seeds &lt;target&gt; &lt;directory&gt; [pdf-directory...]</c>: write the target's seed inputs for libFuzzer or AFL (see <see cref="SeedWriter"/>).</item>
/// <item><c>--list</c>: print the target names.</item>
/// </list>
/// Exit code 0 means no finding, 1 a finding, 2 a usage error. See README.md next to this file.
/// </summary>
internal static class Program
{
    private const string TargetEnvironmentVariable = "BROADSIDE_FUZZ_TARGET";
    private const string LibFuzzerEnvironmentVariable = "__LIBFUZZER_SHM_ID";
    private const int DefaultSmokeSeconds = 10;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            // libfuzzer-dotnet starts the target as "dotnet <harness.dll>" (--target_path=dotnet --target_arg=<dll>) and passes no
            // further argument, so a bare start under the driver is --fuzz with the target from the environment.
            if (Environment.GetEnvironmentVariable(LibFuzzerEnvironmentVariable) is null)
            {
                return Usage();
            }

            args = ["--fuzz"];
        }

        switch (args[0])
        {
            case "--list":
                foreach (string name in FuzzTargets.All.Keys.Order(StringComparer.Ordinal))
                {
                    Console.WriteLine(name);
                }

                return 0;

            case "--fuzz":
                {
                    string? name = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable(TargetEnvironmentVariable);
                    if (!TryGetTarget(name, out ReadOnlySpanAction? target))
                    {
                        return Usage();
                    }

                    if (Environment.GetEnvironmentVariable(LibFuzzerEnvironmentVariable) is null)
                    {
                        // Without the driver SharpFuzz would read a file named by the first argument, which is our mode switch.
                        Console.Error.WriteLine("--fuzz must be started by the libfuzzer-dotnet driver (see README.md). Use --smoke or --run to execute a target directly.");
                        return 2;
                    }

                    Fuzzer.LibFuzzer.Run(target);
                    return 0;
                }

            case "--afl":
                {
                    if (args.Length < 2 || !TryGetTarget(args[1], out ReadOnlySpanAction? target))
                    {
                        return Usage();
                    }

                    Fuzzer.Run(stream =>
                    {
                        using var buffer = new MemoryStream();
                        stream.CopyTo(buffer);
                        target(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
                    });
                    return 0;
                }

            case "--smoke":
                {
                    if (args.Length < 2 || !TryGetTarget(args[1], out ReadOnlySpanAction? target))
                    {
                        return Usage();
                    }

                    if (!TryParseInt(args, index: 2, DefaultSmokeSeconds, out int seconds) || seconds < 0
                        || !TryParseInt(args, index: 3, Random.Shared.Next(), out int seed))
                    {
                        return Usage();
                    }

                    return SmokeRunner.Run(args[1], target, TimeSpan.FromSeconds(seconds), seed);
                }

            case "--run":
                {
                    if (args.Length < 3 || !TryGetTarget(args[1], out ReadOnlySpanAction? target))
                    {
                        return Usage();
                    }

                    target(File.ReadAllBytes(args[2]));
                    Console.WriteLine("ok");
                    return 0;
                }

            case "--seeds":
                {
                    if (args.Length < 3 || !TryGetTarget(args[1], out _))
                    {
                        return Usage();
                    }

                    int count = SeedWriter.Write(args[1], args[2], args[3..]);
                    Console.WriteLine(SeedWriter.Describe(args[1], count, args[2]));
                    return 0;
                }

            default:
                return Usage();
        }
    }

    private static bool TryGetTarget(string? name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ReadOnlySpanAction? target)
    {
        if (name is not null && FuzzTargets.All.TryGetValue(name, out target))
        {
            return true;
        }

        Console.Error.WriteLine(name is null
            ? $"No target given. Pass one or set {TargetEnvironmentVariable}."
            : $"Unknown target '{name}'. Use --list to see the targets.");
        target = null;
        return false;
    }

    private static bool TryParseInt(string[] args, int index, int fallback, out int value)
    {
        if (args.Length <= index)
        {
            value = fallback;
            return true;
        }

        if (int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        Console.Error.WriteLine($"'{args[index]}' is not an integer.");
        return false;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            Broadside fuzz harness. Modes:
              --fuzz [target]                   run under libFuzzer (libfuzzer-dotnet driver); target may come from BROADSIDE_FUZZ_TARGET
              --afl <target>                    run under afl-fuzz
              --smoke <target> [seconds] [seed] corpus files plus random mutations, no fuzzer needed (default 10 s)
              --run <target> <file>             run the target once on one input
              --seeds <target> <dir> [pdf-dir...] write the target's seed inputs from tests/Corpus and the PDFs under pdf-dir
              --list                            list the targets
            """);
        return 2;
    }
}
