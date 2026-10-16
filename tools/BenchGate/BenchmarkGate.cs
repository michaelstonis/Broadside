using System.Globalization;

namespace Broadside.Tools.BenchGate;

/// <summary>
/// The benchmark regression gate (spec story 52, issue #139). Allocations of the pull request are compared with the stored
/// baseline and must match exactly. Times are compared with the base commit measured on the same runner in the same job,
/// never with stored times: shared CI runners differ in CPU model and load from job to job by more than the regressions the
/// gate looks for, while two builds measured side by side on one runner differ by little. See <see cref="GateOptions"/>.
/// </summary>
public static class BenchmarkGate
{
    /// <summary>Compares the results under <paramref name="resultsRoot"/> with the baseline file at <paramref name="baselinePath"/>.</summary>
    public static GateReport Run(string baselinePath, string resultsRoot, GateOptions? options = null)
    {
        options ??= GateOptions.Default;
        AllocationBaseline baseline = AllocationBaseline.Load(baselinePath);
        GateResults results = GateResults.Load(resultsRoot);
        IReadOnlyCollection<string> head = results.HeadCaseNames();

        var findings = new List<GateFinding>();
        CompareAllocations(baseline, results, head, options, findings);
        List<TimeComparison> times = CompareTimes(results, options, findings);

        GateFinding[] ordered = [.. findings
            .OrderBy(static finding => finding.IsFailure ? 0 : 1)
            .ThenBy(static finding => finding.Kind)
            .ThenBy(static finding => finding.Benchmark, StringComparer.Ordinal)];
        return new GateReport(ordered, times, head.Count, results.Rounds.Count, baselinePath.Replace('\\', '/'));
    }

    /// <summary>
    /// The full names of the cases the rounds alone find slower, for the confirmation pass (BenchmarkDotNet's <c>--filter</c>
    /// matches a full name with its parameters). Empty when nothing is slower.
    /// </summary>
    public static IReadOnlyList<string> Suspects(string resultsRoot, GateOptions? options = null)
    {
        options ??= GateOptions.Default;
        GateResults results = GateResults.Load(resultsRoot);
        var filters = new SortedSet<string>(StringComparer.Ordinal);
        foreach ((string name, Sample sample) in Samples(results))
        {
            if (sample.IsSlower(options))
            {
                filters.Add(name);
            }
        }

        return [.. filters];
    }

    /// <summary>
    /// The methods (<c>Namespace.Type.Method</c>, as <c>--list flat</c> prints them) that shard <paramref name="index"/> of
    /// <paramref name="count"/> runs: each method weighs its number of cases in <paramref name="baseline"/> (at least 1), and the
    /// heaviest methods are dealt first, each to the lightest shard, so the shards take about the same time. A method in
    /// <paramref name="split"/> splits its own cases over the shards (the benchmark reads <c>BROADSIDE_BENCH_PARTITION</c>), so it
    /// goes to every shard with an equal share of its weight. Sorted by name.
    /// </summary>
    public static IReadOnlyList<string> Shard(IEnumerable<string> methods, AllocationBaseline baseline, int count, int index, IReadOnlyCollection<string>? split = null)
    {
        split ??= [];
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);

        var weights = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string name in baseline.Allocations.Keys)
        {
            int parenthesis = name.IndexOf('(', StringComparison.Ordinal);
            string method = parenthesis < 0 ? name : name[..parenthesis];
            weights[method] = weights.GetValueOrDefault(method) + 1;
        }

        string[] listed = [.. methods.Select(static line => line.Trim()).Where(static line => line.Length > 0).Distinct(StringComparer.Ordinal)];
        var loads = new double[count];
        var assigned = new List<string>();
        foreach (string method in listed.Where(split.Contains))
        {
            for (int shard = 0; shard < count; shard++)
            {
                loads[shard] += Math.Max(1, weights.GetValueOrDefault(method)) / (double)count;
            }

            assigned.Add(method);
        }

        foreach (string method in listed
            .Where(method => !split.Contains(method))
            .OrderByDescending(method => Math.Max(1, weights.GetValueOrDefault(method)))
            .ThenBy(static method => method, StringComparer.Ordinal))
        {
            int lightest = Array.IndexOf(loads, loads.Min());
            loads[lightest] += Math.Max(1, weights.GetValueOrDefault(method));
            if (lightest == index)
            {
                assigned.Add(method);
            }
        }

        return [.. assigned.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Writes the pull request side's allocations under <paramref name="resultsRoot"/> as a baseline file
    /// (<see cref="AllocationBaseline.From"/>).
    /// </summary>
    public static AllocationBaseline Record(string resultsRoot, string outputPath)
    {
        GateResults results = GateResults.Load(resultsRoot);
        AllocationBaseline baseline = AllocationBaseline.From(results.Allocations.Head);
        baseline.Save(outputPath);
        return baseline;
    }

    private static void CompareAllocations(
        AllocationBaseline baseline,
        GateResults results,
        IReadOnlyCollection<string> head,
        GateOptions options,
        List<GateFinding> findings)
    {
        foreach (string name in head)
        {
            if (!results.Allocations.Head.TryGetValue(name, out List<long>? values) || values.Count == 0)
            {
                findings.Add(new GateFinding(name, FindingKind.NoAllocationFigure, "ran, but the allocation measurement (--gate-allocations) has no figure for it; see the measure job's log"));
                continue;
            }

            long[] measured = [.. values.Distinct().Order()];
            if (!baseline.Allocations.TryGetValue(name, out long expected))
            {
                findings.Add(new GateFinding(name, FindingKind.NotInBaseline, Invariant($"allocates {Bytes(measured)} per call and has no baseline entry"), Measured: measured[^1]));
                continue;
            }

            if (measured.Length != 1 || measured[0] != expected)
            {
                string varies = measured.Length > 1 ? " (it varies between processes: make it deterministic, see #288)" : string.Empty;
                findings.Add(new GateFinding(name, FindingKind.AllocationChanged, Invariant($"allocates {Bytes(measured)} per call{varies}; the baseline is {expected} B"), expected, measured[^1]));
                continue;
            }

            if (results.Allocations.Base.TryGetValue(name, out List<long>? onBase) && onBase.Any(bytes => bytes != expected))
            {
                long[] stale = [.. onBase.Distinct().Order()];
                findings.Add(new GateFinding(name, FindingKind.BaselineStale, Invariant($"the base commit allocates {Bytes(stale)} per call, not the baseline's {expected} B")));
            }
        }

        foreach ((string name, long expected) in baseline.Allocations)
        {
            if (!options.Subset && !head.Contains(name))
            {
                findings.Add(new GateFinding(name, FindingKind.NotRun, "is in the baseline but did not run", expected));
            }
        }
    }

    private static List<TimeComparison> CompareTimes(GateResults results, GateOptions options, List<GateFinding> findings)
    {
        var times = new List<TimeComparison>();
        foreach ((string name, Sample sample) in Samples(results))
        {
            double? confirmation = null;
            bool confirmed = true;
            if (results.Confirmation.Base.TryGetValue(name, out BenchmarkCase? confirmBase)
                && results.Confirmation.Head.TryGetValue(name, out BenchmarkCase? confirmHead)
                && confirmBase.NanosecondsPerOperation.Count > 0
                && confirmHead.NanosecondsPerOperation.Count > 0)
            {
                var rerun = new Sample([(confirmBase.NanosecondsPerOperation, confirmHead.NanosecondsPerOperation)]);
                confirmation = rerun.Ratio;
                confirmed = rerun.Ratio >= options.RoundRatio && rerun.Difference >= options.MinimumDifferenceNanoseconds;
            }

            times.Add(new TimeComparison(name, sample.BaseMedian, sample.HeadMedian, sample.Ratio, sample.RoundRatios, confirmation));
            string detail = Invariant(
                $"{sample.Ratio:0.00}x: {GateReport.FormatTime(sample.BaseMedian)} on base, {GateReport.FormatTime(sample.HeadMedian)} here (rounds {string.Join(", ", sample.RoundRatios.Select(static ratio => ratio.ToString("0.00", CultureInfo.InvariantCulture)))}{(confirmation is double c ? Invariant($"; rerun {c:0.00}") : "")})");

            if (sample.IsSlower(options) && confirmed)
            {
                findings.Add(new GateFinding(name, FindingKind.Slower, detail));
            }
            else if (sample.Ratio >= options.NoticeRatio)
            {
                findings.Add(new GateFinding(name, FindingKind.SlowerWithinTolerance, detail));
            }
            else if (sample.Ratio <= 1 / options.FailRatio)
            {
                findings.Add(new GateFinding(name, FindingKind.Faster, detail));
            }
        }

        return times;
    }

    /// <summary>Every case that has measured iterations on both sides of at least one round.</summary>
    private static SortedDictionary<string, Sample> Samples(GateResults results)
    {
        var rounds = new SortedDictionary<string, List<(IReadOnlyList<double>, IReadOnlyList<double>)>>(StringComparer.Ordinal);
        foreach (GatePass pass in results.Rounds.Values)
        {
            foreach ((string name, BenchmarkCase head) in pass.Head)
            {
                if (!pass.Base.TryGetValue(name, out BenchmarkCase? @base)
                    || head.NanosecondsPerOperation.Count == 0
                    || @base.NanosecondsPerOperation.Count == 0)
                {
                    continue;
                }

                if (!rounds.TryGetValue(name, out List<(IReadOnlyList<double>, IReadOnlyList<double>)>? list))
                {
                    rounds[name] = list = [];
                }

                list.Add((@base.NanosecondsPerOperation, head.NanosecondsPerOperation));
            }
        }

        var samples = new SortedDictionary<string, Sample>(StringComparer.Ordinal);
        foreach ((string name, List<(IReadOnlyList<double>, IReadOnlyList<double>)> list) in rounds)
        {
            samples[name] = new Sample(list);
        }

        return samples;
    }

    private static string Bytes(long[] values) => values.Length == 1
        ? Invariant($"{values[0]} B")
        : Invariant($"{values[0]} B to {values[^1]} B (it varies between runs)");

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>One case's iterations over the rounds, base and pull request.</summary>
    private sealed class Sample
    {
        private readonly double[] _base;
        private readonly double[] _head;

        public Sample(IReadOnlyList<(IReadOnlyList<double> Base, IReadOnlyList<double> Head)> rounds)
        {
            _base = [.. rounds.SelectMany(static round => round.Base).Order()];
            _head = [.. rounds.SelectMany(static round => round.Head).Order()];
            BaseMedian = Quantile(_base, 0.5);
            HeadMedian = Quantile(_head, 0.5);
            RoundRatios = [.. rounds.Select(static round => Quantile([.. round.Head.Order()], 0.5) / Quantile([.. round.Base.Order()], 0.5))];
        }

        public double BaseMedian { get; }

        public double HeadMedian { get; }

        public double Ratio => HeadMedian / BaseMedian;

        public double Difference => HeadMedian - BaseMedian;

        public IReadOnlyList<double> RoundRatios { get; }

        public bool IsSlower(GateOptions options) =>
            Ratio >= options.FailRatio
            && Difference >= options.MinimumDifferenceNanoseconds
            && RoundRatios.All(ratio => ratio >= options.RoundRatio)
            && Quantile(_head, 0.25) > Quantile(_base, 0.75);

        /// <summary>Linear interpolation between closest ranks of a sorted sample.</summary>
        private static double Quantile(double[] sorted, double p)
        {
            if (sorted.Length == 1)
            {
                return sorted[0];
            }

            double position = p * (sorted.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = Math.Min(lower + 1, sorted.Length - 1);
            return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
        }
    }
}
