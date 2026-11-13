using BenchmarkDotNet.Running;

namespace Broadside.Benchmarks;

/// <summary>
/// Entry point of the realistic tier. <c>dotnet run -c Release --project bench/Broadside.Benchmarks.Realistic -- --filter '*'</c>
/// runs everything; add <c>--job short</c> for a quick local run or <c>--job dry</c> for the CI smoke run. Per-page timings of every
/// page, JIT and Native AOT, come from <c>bench/Broadside.SampleTimings</c> instead.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        (BenchmarkConfig config, string[] arguments) = BenchmarkConfig.Parse(args);
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(arguments, config);

        bool failed = summaries.Any(static summary => summary.HasCriticalValidationErrors || summary.Reports.Any(static report => !report.Success));
        return failed ? 1 : 0;
    }
}
