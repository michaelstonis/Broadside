namespace Broadside.Objects;

/// <summary>
/// A process-wide count of changes to COS containers: every change to a <see cref="CosDictionary"/> or <see cref="CosArray"/> through
/// the public API advances it, reading and loading never do.
/// </summary>
/// <remarks>
/// <para>
/// A cache built from many containers (the structure tree's indexes, ISO 32000-2 §14.7) records the epoch it was last known current
/// at, with each container's <c>Version</c>. While the epoch has not moved nothing can have changed, so the cache is used without
/// looking at its containers; once it has moved, the cache compares its containers' versions once and, when they all still match,
/// records the new epoch. A change to an unrelated document only costs that one comparison.
/// </para>
/// <para>
/// Mutation is single-threaded per document, but documents are independent, so the counter is advanced atomically.
/// </para>
/// </remarks>
internal static class CosMutation
{
    private static long _epoch;

    /// <summary>Gets the current epoch.</summary>
    public static long Epoch => Volatile.Read(ref _epoch);

    /// <summary>Records one change.</summary>
    public static void Advance() => Interlocked.Increment(ref _epoch);
}
