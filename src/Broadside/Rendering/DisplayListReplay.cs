using System.Diagnostics;

namespace Broadside.Rendering;

/// <summary>
/// A replay of a <see cref="DisplayList"/> into one backend that runs in time slices: <see cref="Continue"/> hands operations over
/// until its budget is used, and the next call resumes at the operation where it stopped. Start one with
/// <see cref="DisplayList.BeginReplay"/>.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0006; viewer capability 2 (issue #97), PDFium's progressive rendering. Between slices the backend keeps the state the
/// operations so far gave it (graphics state stack, transform, clip) and whatever it drew, so its output after any slice is a valid
/// partial image, and the whole replay hands the backend exactly the calls of <see cref="DisplayList.Replay(IRenderingBackend)"/>.
/// The budget is checked after each operation, so a slice runs at least one operation and ends at most one operation after its
/// budget; one operation (a large image, a long glyph run) is never split.
/// </para>
/// <para>
/// Nothing here blocks or waits, and no thread is used: a single-threaded host (Blazor WebAssembly) replays a slice, yields to its
/// event loop, and continues, for example <c>while (!replay.Continue(TimeSpan.FromMilliseconds(8))) await Task.Yield();</c>.
/// Cancelling the token stops the replay before its next operation; <see cref="Continue"/> then throws
/// <see cref="OperationCanceledException"/>, now and on every later call until the replay is complete. Not thread-safe: one thread at a time continues a replay.
/// </para>
/// </remarks>
public sealed class DisplayListReplay
{
    private readonly CancellationToken _cancellationToken;
    private readonly IProgress<double>? _progress;
    private bool _completionReported;

    internal DisplayListReplay(DisplayList list, IRenderingBackend backend, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        List = list;
        Backend = backend;
        _cancellationToken = cancellationToken;
        _progress = progress;
    }

    /// <summary>Gets the list being replayed.</summary>
    public DisplayList List { get; }

    /// <summary>Gets the backend the operations go to.</summary>
    public IRenderingBackend Backend { get; }

    /// <summary>Gets the number of operations handed over so far: the index of the operation the next slice starts at.</summary>
    public int Position { get; private set; }

    /// <summary>Gets a value indicating whether every operation has been handed over.</summary>
    public bool IsCompleted => Position == List.OperationCount;

    /// <summary>Gets the share of the operations handed over so far, from 0 to 1 (1 for an empty list).</summary>
    public double Progress => List.OperationCount == 0 ? 1 : (double)Position / List.OperationCount;

    /// <summary>
    /// Hands operations over from <see cref="Position"/> until the end of the list or, after at least one, until
    /// <paramref name="budget"/> has passed.
    /// </summary>
    /// <param name="budget">
    /// How long the slice may run, measured with <see cref="Stopwatch"/>; <see cref="TimeSpan.Zero"/> hands over exactly one operation,
    /// <see cref="Timeout.InfiniteTimeSpan"/> runs to the end.
    /// </param>
    /// <returns><see langword="true"/> when the replay is complete (also when it already was), <see langword="false"/> when it stopped for the budget.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative and not <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    /// <exception cref="OperationCanceledException">The replay's token was cancelled; <see cref="Position"/> is where it stopped.</exception>
    public bool Continue(TimeSpan budget)
    {
        if (budget < TimeSpan.Zero && budget != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "The budget must not be negative, except for Timeout.InfiniteTimeSpan.");
        }

        if (!IsCompleted)
        {
            Position = List.ReplayRange(Backend, Position, Deadline(budget), _cancellationToken, _progress);
        }

        if (!IsCompleted)
        {
            _progress?.Report(Progress);
            return false;
        }

        if (!_completionReported)
        {
            _completionReported = true;
            _progress?.Report(1);
        }

        return true;
    }

    /// <summary>Returns the <see cref="Stopwatch"/> timestamp at which a slice of <paramref name="budget"/> starting now ends.</summary>
    private static long Deadline(TimeSpan budget)
    {
        if (budget == Timeout.InfiniteTimeSpan)
        {
            return long.MaxValue;
        }

        double ticks = budget.Ticks * ((double)Stopwatch.Frequency / TimeSpan.TicksPerSecond);
        long now = Stopwatch.GetTimestamp();
        return ticks >= long.MaxValue - now - 1 ? long.MaxValue - 1 : now + (long)ticks;
    }
}
