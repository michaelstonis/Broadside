using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Broadside.Benchmarks;

/// <summary>
/// The BenchmarkDotNet configuration for every benchmark in this assembly: the default columns, exporters and loggers plus one job
/// chosen by <c>--job</c>. <c>--job short</c> selects <see cref="Job.ShortRun"/> for a quick local signal, <c>--job dry</c> selects
/// <see cref="Job.Dry"/> (one in-process iteration, no measurement) so CI can prove every benchmark still runs. Without <c>--job</c>,
/// or with a value this class does not recognise, BenchmarkDotNet's own command-line handling applies, which means
/// <see cref="Job.Default"/> unless the value names another built-in job.
/// </summary>
internal sealed class BenchmarkConfig : ManualConfig
{
    private BenchmarkConfig(Job? job)
    {
        Add(DefaultConfig.Instance);
        if (job is not null)
        {
            AddJob(job);
        }
    }

    /// <summary>
    /// Reads the <c>--job</c> option from the command line. When it names a job this class owns, the option is removed from the
    /// returned arguments so BenchmarkDotNet does not add a second job of its own.
    /// </summary>
    public static (BenchmarkConfig Config, string[] Arguments) Parse(string[] args)
    {
        for (int i = 0; i < (args.Length - 1); i++)
        {
            if (!string.Equals(args[i], "--job", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Job? job = SelectJob(args[i + 1]);
            if (job is null)
            {
                break;
            }

            string[] remaining = [.. args[..i], .. args[(i + 2)..]];
            return (new BenchmarkConfig(job), remaining);
        }

        return (new BenchmarkConfig(job: null), args);
    }

    private static Job? SelectJob(string name) => name.ToUpperInvariant() switch
    {
        "SHORT" => Job.ShortRun,
        "DRY" => Job.Dry,
        _ => null,
    };
}
