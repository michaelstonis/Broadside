using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace Broadside.Caching;

/// <summary>What a <see cref="OnceCache{TKey, TValue}"/> factory produced, and whether the cache keeps it.</summary>
/// <typeparam name="TValue">The value type.</typeparam>
/// <param name="Value">The value, returned to the caller (and to the threads that waited for it).</param>
/// <param name="Keep">
/// <see langword="false"/> when the value depends on how it was reached (it was cut short by a cycle) rather than on the key alone:
/// it is returned but not cached, and the next request computes the value again.
/// </param>
internal readonly record struct Created<TValue>(TValue Value, bool Keep = true);

/// <summary>
/// The thread-safety rule for every lazily computed cache of a document (CLAUDE.md, spec #33 "Thread safety"): each value is
/// computed at most once per key however many threads ask for it at the same time; threads that ask while it is being computed wait
/// for it; a value whose computation needs itself, on the same thread or through threads waiting on each other, does not deadlock.
/// </summary>
/// <remarks>
/// <para>
/// Why not <see cref="Lazy{T}"/> with <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>: a factory that reaches its own
/// value on the same thread makes <see cref="Lazy{T}"/> throw, and two threads each computing a value the other needs deadlock. A
/// malformed file can do both (a stream whose <c>Length</c> is a stream whose <c>Length</c> is the first stream, an object stream
/// whose <c>N</c> is one of its own members). Here the thread that would close such a cycle gets the cycle fallback instead of
/// waiting, and that fallback is not cached.
/// </para>
/// <para>
/// A cached value is read without a lock. A factory exception is cached and rethrown to every later caller: the same input gives the
/// same outcome (strict mode throws the same diagnostic again rather than recording it twice).
/// </para>
/// </remarks>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
internal sealed class OnceCache<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();

    /// <summary>Returns the cached value for <paramref name="key"/>, when one has been computed.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when cached; a cached exception is rethrown.</returns>
    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_entries.TryGetValue(key, out Entry? entry) && entry.IsCompleted)
        {
            value = entry.Result;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Caches <paramref name="value"/> for <paramref name="key"/> unless a value is cached or being computed already.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>The cached value when there is one, else <paramref name="value"/>.</returns>
    public TValue Add(TKey key, TValue value)
    {
        Entry entry = _entries.GetOrAdd(key, static (_, value) => Entry.Completed(value), value);
        return entry.IsCompleted ? entry.Result : value;
    }

    /// <summary>Returns the value for <paramref name="key"/>, computing it with <paramref name="create"/> when nobody has.</summary>
    /// <typeparam name="TState">What the factories need, passed through so they can be static.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="state">The factories' state.</param>
    /// <param name="create">Computes the value; runs at most once per key while its results are kept.</param>
    /// <param name="cycle">
    /// The value for a request that would wait on itself: the key is being computed further up this thread's stack, or by a thread
    /// that is (directly or through others) waiting for this one. Not cached.
    /// </param>
    /// <returns>The value.</returns>
    public TValue GetOrCreate<TState>(TKey key, TState state, Func<TKey, TState, Created<TValue>> create, Func<TKey, TState, TValue> cycle)
    {
        while (true)
        {
            if (_entries.TryGetValue(key, out Entry? entry))
            {
                if (entry.IsCompleted)
                {
                    return entry.Result;
                }

                if (entry.IsAbandoned)
                {
                    _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
                    continue;
                }

                if (entry.Owner == Environment.CurrentManagedThreadId || !WaitGraph.Wait(entry))
                {
                    return cycle(key, state);
                }

                continue;
            }

            var mine = new Entry();
            if (!_entries.TryAdd(key, mine))
            {
                continue;
            }

            Created<TValue> created;
            try
            {
                created = create(key, state);
            }
            catch (Exception exception)
            {
                mine.Fail(ExceptionDispatchInfo.Capture(exception));
                throw;
            }

            if (created.Keep)
            {
                mine.Complete(created.Value);
            }
            else
            {
                _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, mine));
                mine.Abandon();
            }

            return created.Value;
        }
    }

    /// <summary>One key's computation: running on its owner thread, then completed, failed or abandoned.</summary>
    private sealed class Entry : OnceEntry
    {
        private TValue? _value;
        private ExceptionDispatchInfo? _error;

        /// <summary>Gets the value of a completed entry, or rethrows the exception of a failed one.</summary>
        public TValue Result
        {
            get
            {
                _error?.Throw();
                return _value!;
            }
        }

        public static Entry Completed(TValue value)
        {
            var entry = new Entry { _value = value };
            entry.Finish(OnceEntryState.Completed);
            return entry;
        }

        public void Complete(TValue value)
        {
            _value = value;
            Finish(OnceEntryState.Completed);
        }

        public void Fail(ExceptionDispatchInfo error)
        {
            _error = error;
            Finish(OnceEntryState.Completed);
        }

        public void Abandon() => Finish(OnceEntryState.Abandoned);
    }
}

/// <summary>The state of a <see cref="OnceEntry"/>.</summary>
internal enum OnceEntryState
{
    /// <summary>The owner thread is computing the value.</summary>
    Running,

    /// <summary>The value (or the exception) is available.</summary>
    Completed,

    /// <summary>The owner computed a value it does not keep; ask again.</summary>
    Abandoned,
}

/// <summary>The part of a cache entry the process-wide wait graph sees: its owner and whether it is still running.</summary>
internal abstract class OnceEntry
{
    private int _state;
    private object? _gate;

    /// <summary>Gets the managed thread id of the thread computing the value.</summary>
    public int Owner { get; } = Environment.CurrentManagedThreadId;

    /// <summary>Gets a value indicating whether the owner is still computing the value.</summary>
    public bool IsRunning => State == OnceEntryState.Running;

    /// <summary>Gets a value indicating whether the value or exception is available.</summary>
    public bool IsCompleted => State == OnceEntryState.Completed;

    /// <summary>Gets a value indicating whether the owner dropped its value.</summary>
    public bool IsAbandoned => State == OnceEntryState.Abandoned;

    private OnceEntryState State => (OnceEntryState)Volatile.Read(ref _state);

    /// <summary>Publishes the final state and wakes the waiting threads, if any.</summary>
    /// <param name="state">The final state.</param>
    protected void Finish(OnceEntryState state)
    {
        // A full fence between the state write and the gate read pairs with the one in WaitUntilFinished between the gate write and
        // the state read: either the waiter sees the final state, or the owner sees the waiter's gate and pulses it.
        Interlocked.Exchange(ref _state, (int)state);
        if (Volatile.Read(ref _gate) is { } gate)
        {
            lock (gate)
            {
                Monitor.PulseAll(gate);
            }
        }
    }

    /// <summary>Blocks until the owner finishes. The wait gate is created by the first waiter: an uncontended entry has none.</summary>
    internal void WaitUntilFinished()
    {
        object gate = Volatile.Read(ref _gate) ?? Interlocked.CompareExchange(ref _gate, new object(), null) ?? _gate!;
        lock (gate)
        {
            while (IsRunning)
            {
                Monitor.Wait(gate);
            }
        }
    }
}

/// <summary>
/// Which thread waits for which running entry, across every <see cref="OnceCache{TKey, TValue}"/> of the process, so a wait that
/// would close a cycle (thread A waits for B's entry while B waits for A's) is refused instead of deadlocking. Touched only when a
/// thread finds a value still being computed by another thread.
/// </summary>
internal static class WaitGraph
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<int, OnceEntry> WaitingOn = [];

    /// <summary>Waits until <paramref name="entry"/> finishes, unless waiting would deadlock.</summary>
    /// <param name="entry">An entry another thread owns.</param>
    /// <returns><see langword="false"/> when waiting would close a cycle; the caller takes its cycle fallback.</returns>
    public static bool Wait(OnceEntry entry)
    {
        int self = Environment.CurrentManagedThreadId;
        lock (Gate)
        {
            // Follow owner -> the entry that owner waits for -> its owner ...; reaching this thread means a cycle. No other cycle can
            // exist in the graph: the thread that would have closed it was refused.
            OnceEntry? next = entry;
            while (next is { IsRunning: true })
            {
                if (next.Owner == self)
                {
                    return false;
                }

                WaitingOn.TryGetValue(next.Owner, out next);
            }

            WaitingOn[self] = entry;
        }

        try
        {
            entry.WaitUntilFinished();
        }
        finally
        {
            lock (Gate)
            {
                WaitingOn.Remove(self);
            }
        }

        return true;
    }
}
