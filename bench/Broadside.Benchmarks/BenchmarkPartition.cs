using System.Globalization;

namespace Broadside.Benchmarks;

/// <summary>
/// Splits the cases of a benchmark with very many parameter values over the regression gate's parallel runners
/// (<c>bench/baselines/gate/README.md</c>). <c>BROADSIDE_BENCH_PARTITION=&lt;index&gt;/&lt;count&gt;</c> keeps every
/// <c>count</c>-th value starting at <c>index</c>; without it, or with a malformed value, every value is kept, so local runs and
/// <c>--job dry</c> are unaffected. A <c>[ParamsSource]</c> that can grow with the corpus (one case per file) passes its values
/// through <see cref="Select{T}"/> and its method is listed in the gate workflow's <c>--split</c> option.
/// </summary>
internal static class BenchmarkPartition
{
    /// <summary>The environment variable the gate sets per runner.</summary>
    public const string Variable = "BROADSIDE_BENCH_PARTITION";

    /// <summary>The values of this runner's partition, in their original order.</summary>
    public static IEnumerable<T> Select<T>(IEnumerable<T> values)
    {
        if (!TryRead(Environment.GetEnvironmentVariable(Variable), out int index, out int count))
        {
            return values;
        }

        return values.Where((_, position) => position % count == index);
    }

    private static bool TryRead(string? value, out int index, out int count)
    {
        index = 0;
        count = 1;
        int slash = value?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        return slash > 0
            && int.TryParse(value.AsSpan(0, slash), NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && int.TryParse(value.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out count)
            && count > 0
            && index < count;
    }
}
