using System.Diagnostics;

namespace Broadside.TestSupport;

/// <summary>How a <see cref="TimeLimitedWalk"/> ended.</summary>
public enum TimeLimitedVerdict
{
    /// <summary>Returned within its processor-time limit.</summary>
    Finished,

    /// <summary>Returned, but its thread used more processor time than the limit allows: too slow, a bug.</summary>
    OverBudget,

    /// <summary>Had not returned when the wall-clock hang limit passed, and was abandoned: a hang, a bug.</summary>
    Hung,
}

/// <summary>The result of one <see cref="TimeLimitedWalk"/>.</summary>
/// <typeparam name="T">What the walk returns.</typeparam>
/// <param name="Verdict">How it ended.</param>
/// <param name="Value">What the walk returned; the default for <see cref="TimeLimitedVerdict.Hung"/>.</param>
/// <param name="Elapsed">The wall-clock time from the start to the verdict.</param>
/// <param name="ProcessorTime">
/// The processor time the walk's thread used (<see cref="ThreadProcessorTime"/>; the wall-clock time where the platform does not
/// report it); zero for <see cref="TimeLimitedVerdict.Hung"/>.
/// </param>
public sealed record TimeLimitedResult<T>(TimeLimitedVerdict Verdict, T? Value, TimeSpan Elapsed, TimeSpan ProcessorTime);

/// <summary>
/// Runs one corpus file's work on a thread of its own under two limits (issue #280). The work is charged for the processor time
/// its thread uses, so a machine loaded by parallel tests, which stretches the wall-clock time of every walk many times over, cannot
/// turn a fast walk into a failure; a walk that is genuinely slow still is one. A walk that never returns (a loop or a deadlock)
/// uses processor time nobody can read until it returns, so it is caught by the wall clock instead: it is abandoned at the hang
/// limit, which is set well above the processor-time limit, and keeps its thread (the library has no cancellation).
/// </summary>
public static class TimeLimitedWalk
{
    /// <summary>Runs <paramref name="walk"/> under the two limits.</summary>
    /// <typeparam name="T">What the walk returns.</typeparam>
    /// <param name="walk">The work. An exception it throws propagates to the caller.</param>
    /// <param name="processorTimeLimit">The most processor time the walk's thread may use.</param>
    /// <param name="hangLimit">The wall-clock time after which a walk that has not returned is abandoned.</param>
    /// <returns>The verdict, with what the walk returned.</returns>
    public static async Task<TimeLimitedResult<T>> RunAsync<T>(Func<T> walk, TimeSpan processorTimeLimit, TimeSpan hangLimit)
    {
        ArgumentNullException.ThrowIfNull(walk);
        var stopwatch = Stopwatch.StartNew();
        Task<(T Value, TimeSpan ProcessorTime)> run = Task.Factory.StartNew(
            () =>
            {
                TimeSpan? start = ThreadProcessorTime.Current;
                T value = walk();
                TimeSpan used = start is { } from && ThreadProcessorTime.Current is { } to ? to - from : stopwatch.Elapsed;
                return (value, used);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        try
        {
            (T value, TimeSpan used) = await run.WaitAsync(hangLimit).ConfigureAwait(false);
            return new TimeLimitedResult<T>(used > processorTimeLimit ? TimeLimitedVerdict.OverBudget : TimeLimitedVerdict.Finished, value, stopwatch.Elapsed, used);
        }
        catch (TimeoutException)
        {
            return new TimeLimitedResult<T>(TimeLimitedVerdict.Hung, default, stopwatch.Elapsed, TimeSpan.Zero);
        }
    }
}
