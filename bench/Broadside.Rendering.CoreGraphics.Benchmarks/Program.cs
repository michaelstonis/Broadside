using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Entry point, with the options of <c>bench/Broadside.Benchmarks</c>: <c>--job short</c> for a quick local run, <c>--job dry</c> for a
/// smoke run, neither for <see cref="Job.Default"/>. Every job runs in process (a macOS app bundle cannot be rebuilt as a child
/// process), and results go to <c>BenchmarkDotNet.Artifacts/</c> at the repository root (gitignored), not into the app bundle, where
/// the app starts. Exits 1 when any benchmark fails validation or execution.
/// </summary>
/// <remarks>
/// In process, BenchmarkDotNet's <c>Allocated</c> counts every thread of the process, and its own host allocates a little while a
/// benchmark runs (an empty 100 ms spin reads about 130 B per operation), so a benchmark whose operations take many milliseconds
/// shows a few bytes per operation that are not its own. The replay allocation is asserted on its own thread by
/// <c>Broadside.Rendering.CoreGraphics.Tests.ReplayAllocationTests</c>.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        Job job = Job.Default;
        string[] arguments = args;
        int index = Array.FindIndex(args, static arg => string.Equals(arg, "--job", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length)
        {
            Job? named = args[index + 1].ToUpperInvariant() switch
            {
                "SHORT" => Job.ShortRun,
                "DRY" => Job.Dry,
                _ => null,
            };
            if (named is not null)
            {
                job = named;
                arguments = [.. args[..index], .. args[(index + 2)..]];
            }
        }

        ManualConfig config = ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(job.WithToolchain(InProcessNoEmitToolchain.Instance))
            .WithArtifactsPath(Path.Combine(CorpusLocator.RepositoryRoot, "BenchmarkDotNet.Artifacts"));
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(arguments, config);

        bool failed = summaries.Any(static summary => summary.HasCriticalValidationErrors || summary.Reports.Any(static report => !report.Success));
        return failed ? 1 : 0;
    }
}
