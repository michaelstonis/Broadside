using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The time limit the corpus gates put on one file (issue #280): a walk is charged for the processor time its own thread uses, so a
/// machine loaded by parallel tests cannot turn a fast walk into a timeout, and a walk that never returns is still abandoned at the
/// wall-clock hang limit.
/// </summary>
public sealed class TimeLimitedWalkTests
{
    private static readonly TimeSpan GenerousHangLimit = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Time_the_walk_spends_off_the_processor_is_not_charged_to_it()
    {
        SkipWithoutThreadProcessorTime();

        // A walk descheduled by a loaded machine: long on the wall clock, almost nothing on the processor.
        TimeLimitedResult<int> result = await TimeLimitedWalk.RunAsync(
            () =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(600));
                return 42;
            },
            processorTimeLimit: TimeSpan.FromMilliseconds(200),
            hangLimit: GenerousHangLimit);

        Assert.Equal(TimeLimitedVerdict.Finished, result.Verdict);
        Assert.Equal(42, result.Value);
        Assert.True(result.Elapsed >= TimeSpan.FromMilliseconds(600), $"Elapsed {result.Elapsed}");
        Assert.True(result.ProcessorTime < TimeSpan.FromMilliseconds(200), $"Processor time {result.ProcessorTime}");
    }

    [Fact]
    public async Task A_walk_that_uses_more_processor_time_than_its_limit_is_over_budget()
    {
        SkipWithoutThreadProcessorTime();

        TimeLimitedResult<int> result = await TimeLimitedWalk.RunAsync(
            () =>
            {
                // Busy until this thread itself has used 300 ms of processor time, however loaded the machine is.
                TimeSpan start = ThreadProcessorTime.Current!.Value;
                while (ThreadProcessorTime.Current!.Value - start < TimeSpan.FromMilliseconds(300))
                {
                    Thread.SpinWait(1000);
                }

                return 7;
            },
            processorTimeLimit: TimeSpan.FromMilliseconds(100),
            hangLimit: GenerousHangLimit);

        Assert.Equal(TimeLimitedVerdict.OverBudget, result.Verdict);
        Assert.Equal(7, result.Value);
        Assert.True(result.ProcessorTime >= TimeSpan.FromMilliseconds(300), $"Processor time {result.ProcessorTime}");
    }

    [Fact]
    public async Task A_walk_that_never_returns_is_abandoned_at_the_hang_limit()
    {
        using var release = new ManualResetEventSlim();

        TimeLimitedResult<int> result = await TimeLimitedWalk.RunAsync(
            () =>
            {
                release.Wait();
                return 1;
            },
            processorTimeLimit: TimeSpan.FromSeconds(30),
            hangLimit: TimeSpan.FromMilliseconds(300));
        release.Set();

        Assert.Equal(TimeLimitedVerdict.Hung, result.Verdict);
        Assert.Equal(default, result.Value);
        Assert.True(result.Elapsed >= TimeSpan.FromMilliseconds(300), $"Elapsed {result.Elapsed}");
    }

    private static void SkipWithoutThreadProcessorTime()
    {
        if (ThreadProcessorTime.Current is null)
        {
            Assert.Skip("This platform does not report a thread's processor time; the limit falls back to the wall clock.");
        }
    }
}
