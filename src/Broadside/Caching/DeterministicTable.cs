using System.Collections.Concurrent;

namespace Broadside.Caching;

/// <summary>
/// Creates the concurrent dictionaries behind a document's caches so that what they allocate depends only on how many entries they
/// hold (issue #288).
/// </summary>
/// <remarks>
/// A <see cref="ConcurrentDictionary{TKey, TValue}"/> made with the default constructor has one lock per processor, doubles its lock
/// array at every resize, and resizes as soon as any one lock's share of the entries passes its budget, so when it grows, and what it
/// allocates, depends on how the keys hash across the locks: on per-process random seeds (<see cref="HashCode"/>, strings, object
/// identity) and on the processor count. The same file then allocates different bytes in every process and on every machine, which
/// breaks the exact allocation baselines of the benchmark gate. With one fixed lock the table grows only when the entry count passes
/// the bucket count. Reads take no lock either way, and a write takes it once per key for a few instructions (factories run outside
/// it), so concurrent loading does not queue on it.
/// </remarks>
internal static class DeterministicTable
{
    /// <summary>The one lock.</summary>
    public const int ConcurrencyLevel = 1;

    /// <summary>The initial bucket count, the dictionary's own default.</summary>
    public const int InitialCapacity = 31;

    /// <summary>Creates an empty dictionary comparing keys with their default equality.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <returns>The dictionary.</returns>
    public static ConcurrentDictionary<TKey, TValue> Create<TKey, TValue>()
        where TKey : notnull => new(ConcurrencyLevel, InitialCapacity);

    /// <summary>Creates an empty dictionary comparing keys with <paramref name="comparer"/>.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="comparer">The key comparer.</param>
    /// <returns>The dictionary.</returns>
    public static ConcurrentDictionary<TKey, TValue> Create<TKey, TValue>(IEqualityComparer<TKey> comparer)
        where TKey : notnull => new(ConcurrencyLevel, InitialCapacity, comparer);
}
