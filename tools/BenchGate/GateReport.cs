using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Broadside.Tools.BenchGate;

/// <summary>What a finding is about.</summary>
public enum FindingKind
{
    /// <summary>Bytes allocated per operation differ from the stored baseline (more or fewer). Fails the gate.</summary>
    AllocationChanged,

    /// <summary>The case ran but has no baseline entry (a new or renamed benchmark). Fails the gate.</summary>
    NotInBaseline,

    /// <summary>The baseline has the case but the run did not produce it (a removed or renamed benchmark, or a failed run). Fails the gate.</summary>
    NotRun,

    /// <summary>The run has no allocation figure for the case (no <c>[MemoryDiagnoser]</c>). Fails the gate.</summary>
    NoAllocationFigure,

    /// <summary>Measurably slower than the base commit under every condition of <see cref="GateOptions"/>. Fails the gate.</summary>
    Slower,

    /// <summary>Slower than the notice ratio but not failing (one condition of <see cref="GateOptions"/> did not hold). A notice.</summary>
    SlowerWithinTolerance,

    /// <summary>The base commit's allocation already differs from the baseline: the baseline is stale. A notice.</summary>
    BaselineStale,

    /// <summary>Faster than the base commit by the fail ratio or more. A notice.</summary>
    Faster,
}

/// <summary>One finding about one benchmark case.</summary>
/// <param name="Benchmark">The case's full name.</param>
/// <param name="Kind">What it is about.</param>
/// <param name="Message">One line for people.</param>
/// <param name="Baseline">The baseline's bytes per operation, for allocation findings that have one.</param>
/// <param name="Measured">The measured bytes per operation (the largest when runs disagree), for allocation findings that have one.</param>
public sealed record GateFinding(string Benchmark, FindingKind Kind, string Message, long? Baseline = null, long? Measured = null)
{
    /// <summary>True when the finding fails the gate.</summary>
    public bool IsFailure => Kind is FindingKind.AllocationChanged or FindingKind.NotInBaseline or FindingKind.NotRun
        or FindingKind.NoAllocationFigure or FindingKind.Slower;
}

/// <summary>One case's time comparison, for the full table in the summary.</summary>
/// <param name="Benchmark">The case's full name.</param>
/// <param name="BaseMedian">Median of the base commit's iterations, ns per operation.</param>
/// <param name="HeadMedian">Median of the pull request's iterations, ns per operation.</param>
/// <param name="Ratio">Head over base.</param>
/// <param name="RoundRatios">The same ratio per round.</param>
/// <param name="ConfirmationRatio">The confirmation pass's ratio, when the case was rerun.</param>
public sealed record TimeComparison(string Benchmark, double BaseMedian, double HeadMedian, double Ratio, IReadOnlyList<double> RoundRatios, double? ConfirmationRatio);

/// <summary>The gate's verdict and its job summary.</summary>
public sealed class GateReport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal GateReport(IReadOnlyList<GateFinding> findings, IReadOnlyList<TimeComparison> times, int caseCount, int roundCount, string baselinePath)
    {
        Findings = findings;
        Times = times;
        CaseCount = caseCount;
        RoundCount = roundCount;
        BaselinePath = baselinePath;
    }

    /// <summary>Every finding, failures first.</summary>
    public IReadOnlyList<GateFinding> Findings { get; }

    /// <summary>The findings that fail the gate.</summary>
    public IReadOnlyList<GateFinding> Failures => [.. Findings.Where(static finding => finding.IsFailure)];

    /// <summary>Every case compared in time, in name order.</summary>
    public IReadOnlyList<TimeComparison> Times { get; }

    /// <summary>Cases the pull request side ran.</summary>
    public int CaseCount { get; }

    /// <summary>Interleaved rounds read.</summary>
    public int RoundCount { get; }

    /// <summary>The baseline file compared against.</summary>
    public string BaselinePath { get; }

    /// <summary>True when nothing fails.</summary>
    public bool Passed => !Findings.Any(static finding => finding.IsFailure);

    /// <summary>The GitHub job summary: verdict, one row per failure, notices, the full time table folded, and how to update the baseline.</summary>
    public string ToMarkdown(string? baseName = null)
    {
        var text = new StringBuilder();
        IReadOnlyList<GateFinding> failures = Failures;
        string against = baseName is null ? "the base commit" : $"the base commit `{baseName}`";
        text.AppendLine("## Benchmark gate").AppendLine();
        text.AppendLine(Invariant(
            $"{(Passed ? "**Passed**" : $"**Failed**: {failures.Count} regression(s)")} over {CaseCount} benchmark case(s). Allocations (bytes per call) are compared with `{BaselinePath}`, exactly; times with {against}, run on this runner in {RoundCount} interleaved round(s), slower cases rerun once to confirm."));
        text.AppendLine();

        if (failures.Count > 0)
        {
            AppendTable(text, failures);
        }

        GateFinding[] notices = [.. Findings.Where(static finding => !finding.IsFailure)];
        if (notices.Length > 0)
        {
            text.AppendLine("### Notices").AppendLine();
            AppendTable(text, notices);
        }

        if (Times.Count > 0)
        {
            text.AppendLine(Invariant($"<details><summary>All {Times.Count} time comparisons</summary>")).AppendLine();
            text.AppendLine("| Benchmark | Base | Pull request | Ratio | Rounds | Confirmation |");
            text.AppendLine("|---|---:|---:|---:|---|---:|");
            foreach (TimeComparison time in Times)
            {
                text.AppendLine(Invariant(
                    $"| {Code(time.Benchmark)} | {FormatTime(time.BaseMedian)} | {FormatTime(time.HeadMedian)} | {time.Ratio:0.000} | {string.Join(", ", time.RoundRatios.Select(static ratio => ratio.ToString("0.000", CultureInfo.InvariantCulture)))} | {(time.ConfirmationRatio is double confirmation ? confirmation.ToString("0.000", CultureInfo.InvariantCulture) : "")} |"));
            }

            text.AppendLine().AppendLine("</details>").AppendLine();
        }

        GateFinding[] baselineChanges = [.. failures.Where(static finding => finding.Kind is FindingKind.AllocationChanged or FindingKind.NotInBaseline or FindingKind.NotRun)];
        if (baselineChanges.Length > 0)
        {
            text.AppendLine("### Baseline update").AppendLine();
            text.AppendLine(Invariant($"If these changes are intended, apply them to `{BaselinePath}`, or replace it with the `bench-gate-baseline` artifact of this run (the whole file re-recorded):"));
            text.AppendLine().AppendLine("```diff");
            foreach (GateFinding finding in baselineChanges)
            {
                string key = JsonSerializer.Serialize(finding.Benchmark, JsonOptions);
                if (finding.Baseline is long before)
                {
                    text.AppendLine(Invariant($"-    {key}: {before},"));
                }

                if (finding.Measured is long after)
                {
                    text.AppendLine(Invariant($"+    {key}: {after},"));
                }
            }

            text.AppendLine("```").AppendLine();
        }

        if (!Passed)
        {
            text.AppendLine("An allocation change or a new, renamed or removed benchmark that is intended is accepted by recording the baseline in this pull request; a slowdown that is intended needs the owner's review. Both procedures are in `bench/baselines/gate/README.md`.");
        }

        return text.ToString();
    }

    /// <summary>Formats nanoseconds with the unit BenchmarkDotNet would choose.</summary>
    public static string FormatTime(double nanoseconds) => nanoseconds switch
    {
        >= 1_000_000_000 => Invariant($"{nanoseconds / 1_000_000_000:0.000} s"),
        >= 1_000_000 => Invariant($"{nanoseconds / 1_000_000:0.000} ms"),
        >= 1_000 => Invariant($"{nanoseconds / 1_000:0.000} us"),
        _ => Invariant($"{nanoseconds:0.00} ns"),
    };

    private static void AppendTable(StringBuilder text, IEnumerable<GateFinding> findings)
    {
        text.AppendLine("| Benchmark | Finding | Detail |");
        text.AppendLine("|---|---|---|");
        foreach (GateFinding finding in findings)
        {
            text.AppendLine(Invariant($"| {Code(finding.Benchmark)} | {Describe(finding.Kind)} | {finding.Message.Replace("|", "\\|", StringComparison.Ordinal)} |"));
        }

        text.AppendLine();
    }

    private static string Describe(FindingKind kind) => kind switch
    {
        FindingKind.AllocationChanged => "allocation changed",
        FindingKind.NotInBaseline => "not in the baseline",
        FindingKind.NotRun => "did not run",
        FindingKind.NoAllocationFigure => "no allocation figure",
        FindingKind.Slower => "slower",
        FindingKind.SlowerWithinTolerance => "slower, within tolerance",
        FindingKind.BaselineStale => "baseline stale",
        FindingKind.Faster => "faster",
        _ => kind.ToString(),
    };

    private static string Code(string name)
    {
        const string prefix = "Broadside.Benchmarks.";
        string shortName = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
        return "`" + shortName.Replace("|", "\\|", StringComparison.Ordinal) + "`";
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
