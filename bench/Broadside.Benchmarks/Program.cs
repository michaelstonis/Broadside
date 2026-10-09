using BenchmarkDotNet.Running;

namespace Broadside.Benchmarks;

/// <summary>
/// Entry point. <c>dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*'</c> runs everything;
/// add <c>--job short</c> for a quick local run or <c>--job dry</c> for the CI smoke run. See <c>tests/README.md</c>.
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
