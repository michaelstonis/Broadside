namespace Broadside.TestSupport;

/// <summary>
/// Measures what a hot path allocates in steady state, for the build-breaking allocation tests (CLAUDE.md, "Code conventions":
/// hot paths allocate nothing per token, operator, glyph or scanline). Every allocation test goes through <see cref="Measure"/>
/// and runs in the non-parallel <c>Heavy</c> collection of its test project.
/// </summary>
/// <remarks>
/// One measured call after a fixed warm-up is flaky under load (issue #48 saw it at a load average of 260), because
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> also counts what the runtime allocates on the thread for reasons that have
/// nothing to do with the code under test: (1) tiered compilation installs optimized code from a background thread after a delay,
/// and on a busy machine the measured call can still run unoptimized code, which does not stack-allocate the objects the optimizing
/// JIT proves do not escape; (2) a gen-2 collection caused by any other test trims <c>ArrayPool&lt;T&gt;.Shared</c>, emptying the
/// thread's cached arrays under memory pressure, so the next rent allocates a fresh array; (3) first-use runtime work (type loads,
/// static constructors) lands on whichever call comes first. None of these repeats on every call, while an allocation per token
/// or per row does. So the warm-up is followed by several measured calls, a short pause between them for the background compiler,
/// and the fewest bytes any of them allocated is the answer; it stops early at zero.
/// </remarks>
public static class Allocations
{
    /// <summary>Calls before measuring: past tiered compilation's call-count threshold (30), so the methods are queued for tier 1.</summary>
    public const int WarmUpCalls = 40;

    /// <summary>The most measured calls.</summary>
    public const int Attempts = 10;

    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(20);

    /// <summary>Returns the fewest bytes one call of <paramref name="action"/> allocated on this thread, after a warm-up.</summary>
    /// <param name="action">The work to measure. Create the delegate (and anything it captures) before calling, not inside it.</param>
    /// <param name="warmUpCalls">Calls before measuring.</param>
    /// <returns>The bytes the cheapest measured call allocated.</returns>
    public static long Measure(Action action, int warmUpCalls = WarmUpCalls)
    {
        ArgumentNullException.ThrowIfNull(action);
        for (int call = 0; call < warmUpCalls; call++)
        {
            action();
        }

        long fewest = long.MaxValue;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(Pause);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
            if (fewest == 0)
            {
                break;
            }
        }

        return fewest;
    }
}
