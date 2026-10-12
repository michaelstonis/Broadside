namespace Broadside.Tools.BenchGate;

/// <summary>
/// The time tolerance. A case fails only when every condition holds, so one noisy iteration, one noisy round or one noisy
/// pass cannot fail a pull request on its own:
/// <list type="number">
/// <item>the median of all its pull-request iterations is at least <see cref="FailRatio"/> times the median of all its base iterations;</item>
/// <item>every round on its own is at least <see cref="RoundRatio"/> times slower (no drift of the runner between rounds explains it);</item>
/// <item>the two samples do not overlap in their middle halves (the pull request's first quartile is above the base's third quartile);</item>
/// <item>a fresh base/pull-request pass of that case, run after the rounds, is again at least <see cref="FailRatio"/> times slower.</item>
/// </list>
/// The values are justified in <c>bench/baselines/gate/README.md</c> from ten runs of the gate on the CI runner.
/// </summary>
public sealed record GateOptions
{
    /// <summary>The defaults the CI job uses.</summary>
    public static GateOptions Default { get; } = new();

    /// <summary>Pooled median ratio (pull request over base) at or above which a case is slower. Default 1.20.</summary>
    public double FailRatio { get; init; } = 1.20;

    /// <summary>Ratio every round must reach for a slower case. Default 1.10.</summary>
    public double RoundRatio { get; init; } = 1.10;

    /// <summary>Pooled median ratio at or above which a case that does not fail is listed as a notice. Default 1.10.</summary>
    public double NoticeRatio { get; init; } = 1.10;

    /// <summary>
    /// Absolute difference of the medians below which a case is never slower, in nanoseconds per operation. Default 1 ns:
    /// below that a ratio measures code alignment and timer resolution, not the code.
    /// </summary>
    public double MinimumDifferenceNanoseconds { get; init; } = 1.0;

    /// <summary>
    /// True when the results cover only some benchmarks on purpose (a local run with <c>--methods</c>): baseline cases that did
    /// not run are then not findings. CI never sets it.
    /// </summary>
    public bool Subset { get; init; }
}
